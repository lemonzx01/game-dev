using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace FantasyWorld.Simulation;

public sealed partial class SimulationWorld : ISimulation
{
    public const uint WorldGenerationStream = 1;
    private const int RowsPerChunk = 2048; // 2048 * 28-byte rows ≈ 57 KB < 85,000-byte LOH threshold

    private readonly Thread owner = Thread.CurrentThread;
    private readonly SimulationOptions options;
    private readonly WorldId worldId;
    private readonly Dictionary<uint, RandomStream> streams = new();
    private readonly uint[] streamTags;
    private readonly EntityIds ids = new();
    private readonly EventLog events;
    private readonly Queue<QueuedCommand> commands = new();
    private readonly Scheduler scheduler;
    private readonly Terrain[] terrain;
    private MapView map; // rebuilt when God SetTerrain changes a tile
    private readonly ResourceStore resources;
    private readonly SpatialGrid grid; // derived index of living entities (not hashed)
    private readonly List<EntityState> entities = new(); // ascending EntityId; dead rows leave the tick after death
    private long tick;
    private long nextCommandSequence = 1;
    private bool stepping;
    private int emittedThisTick;
    private long droppedThisTick;
    private WorldSnapshot? snapshot;

    public SimulationWorld(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
        worldId = options.WorldId;
        events = new EventLog(options.EventCapacity);
        scheduler = new Scheduler(options.DiscretionaryWorkBudget);

        var generation = new RandomStream(options.Seed, WorldGenerationStream, 0);
        streams.Add(WorldGenerationStream, generation);
        if (options.Map is { } custom)
        {
            terrain = custom.Cells.ToArray();
            resources = ResourceStore.FromPlacements(custom, options.Resources);
        }
        else
        {
            terrain = MapGenerator.Generate(options.MapWidth, options.MapHeight, generation);
            resources = ResourceStore.Generate(terrain, options.MapWidth, options.MapHeight, options.Resources, generation);
        }
        map = new MapView(options.MapWidth, options.MapHeight, ImmutableArray.Create(terrain));
        grid = new SpatialGrid(options.MapWidth, options.MapHeight);
        streams.Add(AiStream, new RandomStream(options.Seed, AiStream, 0));
        streams.Add(EcologyStream, new RandomStream(options.Seed, EcologyStream, 0));
        streams.Add(PsycheStream, new RandomStream(options.Seed, PsycheStream, 0));

        // Prewarm is part of the baseline configuration; insertion order must not affect the hash.
        foreach (uint tag in options.PrewarmStreams)
            streams.TryAdd(tag, new RandomStream(options.Seed, tag, 0));
        streamTags = streams.Keys.Order().ToArray();
    }

    /// <summary>T002-compatible convenience constructor (no grants, default map).</summary>
    public SimulationWorld(WorldId worldId, ulong seed, IEnumerable<uint>? prewarm = null, int eventCapacity = 4096)
        : this(new SimulationOptions
        {
            WorldId = worldId,
            Seed = seed,
            EventCapacity = eventCapacity,
            MaxEventsPerTick = Math.Min(2048, eventCapacity),
            CommandQueueCapacity = Math.Min(1024, eventCapacity - 1),
            PrewarmStreams = (prewarm ?? []).ToImmutableArray(),
        })
    {
    }

    public SimulationOptions Options => options;
    public long LastCompletedTick { get { CheckAccess(); return tick; } }

    private void CheckAccess()
    {
        if (Thread.CurrentThread != owner || stepping)
            throw new InvalidOperationException("Simulation API requires its idle owner thread.");
    }

    public void Step() => Advance(null);

    /// <summary>
    /// One fixed tick: queued commands (ascending CommandSequence), then due scheduled work, then the optional
    /// internal test phase. Runs inside the access guard; publishes only the completed tick.
    /// </summary>
    internal void Advance(Action? phase, int? eventKind = null)
    {
        CheckAccess();
        long next = checked(tick + 1);
        stepping = true;
        try
        {
            emittedThisTick = 0;
            droppedThisTick = 0;
            // Entities that died on the previous tick were visible as Dead in that tick's snapshot; drop them now.
            entities.RemoveAll(e => e.LifeState == LifeStates.Dead);
            while (commands.Count > 0 && commands.Peek().TargetTick == next)
                Execute(commands.Dequeue(), next);
            LastDiscretionaryRun = scheduler.Run(next, item => RunWork(item, next));
            ProcessPaths(next);
            phase?.Invoke();
            if (eventKind.HasValue)
                Emit(new WorldEvent(0, next, eventKind.Value));
            if (droppedThisTick > 0)
                events.Append(new WorldEvent(0, next, EventKinds.EventsDropped, Amount: droppedThisTick));
            tick = next;
            snapshot = null;
        }
        finally
        {
            stepping = false;
        }
    }

    /// <summary>Appends within the per-tick cap; excess is counted into one EventsDropped summary (N1).</summary>
    /// <remarks>Critical events (deaths) are never dropped; they still count toward the cap. Returns the assigned EventSequence, or 0 if dropped.</remarks>
    internal long Emit(in WorldEvent entry, bool critical = false)
    {
        if (critical || emittedThisTick < options.MaxEventsPerTick - 1)
        {
            long sequence = events.NextSequence;
            events.Append(entry);
            emittedThisTick++;
            return sequence;
        }
        droppedThisTick++;
        return 0;
    }

    // ---- Commands -------------------------------------------------------------------------------

    public CommandReceipt SubmitCommand(GameCommand command)
    {
        CheckAccess();
        var reason = Admit(command);
        if (reason != CommandReason.None)
            return new("Rejected", reason.ToString());
        if (commands.Count >= options.CommandQueueCapacity)
            return new("Rejected", CommandReason.QueueFull.ToString());
        long sequence = nextCommandSequence;
        nextCommandSequence = checked(sequence + 1);
        long target = checked(tick + 1);
        commands.Enqueue(new QueuedCommand(sequence, target, command));
        return new("Queued", "", target, sequence);
    }

    // Shape, world and authority at submission. Never mutates.
    private CommandReason Admit(GameCommand? command)
    {
        if (command is null) return CommandReason.InvalidCommand;
        if (command.WorldId != worldId) return CommandReason.InvalidWorld;
        if (command.Mode is not (CommandModes.Observer or CommandModes.God)) return CommandReason.InvalidMode;
        var authority = CheckAuthority(command);
        if (authority != CommandReason.None) return authority;
        return command switch
        {
            SpawnEntityCommand s => !InBounds(s.X, s.Y) ? CommandReason.OutOfBounds
                : s.SpeciesKey < 1 || !Enum.IsDefined(s.Sex) ? CommandReason.InvalidCommand : CommandReason.None,
            TakeResourceCommand t => !InBounds(t.X, t.Y) ? CommandReason.OutOfBounds
                : !Enum.IsDefined(t.Resource) ? CommandReason.InvalidCommand
                : t.Amount < 1 ? CommandReason.InvalidAmount : CommandReason.None,
            SetTerrainCommand g => !InBounds(g.X, g.Y) ? CommandReason.OutOfBounds
                : !Enum.IsDefined(g.Terrain) ? CommandReason.InvalidCommand : CommandReason.None,
            FoundSettlementCommand f => !InBounds(f.X, f.Y) ? CommandReason.OutOfBounds
                : f.Faction is not (Faction.Human or Faction.Demon) || string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 32
                    ? CommandReason.InvalidCommand : CommandReason.None,
            _ => CommandReason.InvalidCommand,
        };
    }

    // Every T003 command mutates the world, so it needs Mode=God backed by a trusted God grant.
    // The Mode string is only a request; authority comes from options.Grants (N4).
    private CommandReason CheckAuthority(GameCommand command)
    {
        options.Grants.TryGetValue(command.IssuerId, out var grant);
        if (command.Mode != CommandModes.God || (grant & AuthorityGrant.God) == 0)
            return CommandReason.Unauthorized;
        return CommandReason.None;
    }

    private void Execute(QueuedCommand queued, long now)
    {
        var command = queued.Command;
        long created = 0;
        int amount = 0;
        var reason = CheckAuthority(command);
        if (reason == CommandReason.None)
        {
            switch (command)
            {
                case SpawnEntityCommand s:
                    if (!TerrainRules.IsPassable(terrain[Cell(s.X, s.Y)]))
                    {
                        reason = CommandReason.Impassable;
                        break;
                    }
                    created = SpawnEntity(s.X, s.Y, s.SpeciesKey, now, s.Sex);
                    break;
                case TakeResourceCommand t:
                    int node = resources.Find(Cell(t.X, t.Y), t.Resource);
                    if (node < 0) { reason = CommandReason.NoResource; break; }
                    ref var stock = ref resources[node];
                    if (stock.Amount < t.Amount) { reason = CommandReason.InsufficientStock; break; }
                    stock.Amount -= t.Amount;
                    amount = t.Amount;
                    ScheduleRegrowth(node, now);
                    break;
                case SetTerrainCommand g:
                    reason = ApplyTerrain(g);
                    break;
                case FoundSettlementCommand f:
                    reason = Found(f, now);
                    if (reason == CommandReason.None) created = settlements.Count;
                    break;
                default:
                    reason = CommandReason.InvalidCommand;
                    break;
            }
        }
        var (x, y) = command switch
        {
            SpawnEntityCommand s => (s.X, s.Y),
            TakeResourceCommand t => (t.X, t.Y),
            SetTerrainCommand g => (g.X, g.Y),
            FoundSettlementCommand f => (f.X, f.Y),
            _ => (-1, -1),
        };
        Emit(new WorldEvent(0, now, reason == CommandReason.None ? EventKinds.CommandApplied : EventKinds.CommandRejected,
            queued.Sequence, command.IssuerId, created, x, y, (int)reason, amount));
    }

    // ---- Scheduled systems ----------------------------------------------------------------------

    private void ScheduleRegrowth(int node, long now)
    {
        ref var stock = ref resources[node];
        var rule = resources.Config.RuleFor(stock.Kind);
        if (stock.RegrowScheduled || stock.Amount >= rule.Capacity || rule.RegrowAmount == 0) return;
        stock.RegrowScheduled = true;
        var (interval, _) = Growth(node, now);
        scheduler.Schedule(checked(now + interval), 10, 0, new WorkItem(WorkKind.RegrowResource, node), isRequired: true);
    }

    /// <summary>
    /// T012 seasonal growth for a node at a tick: plants and wood grow every RegrowIntervalTicks × 100 / growth% of the
    /// current season, and not at all in a dormant season or on a tile colder than MinGrowthTemperature (then the node is
    /// re-checked after one base interval). Water regrows at its base rate all year.
    /// </summary>
    private (long Interval, bool Grows) Growth(int node, long now)
    {
        var stock = resources[node];
        var rule = resources.Config.RuleFor(stock.Kind);
        if (stock.Kind == ResourceKind.Water) return (rule.RegrowIntervalTicks, true);
        var climate = options.Climate;
        int percent = climate.GrowthPercent(WorldTime.From(now, options.Calendar).Season);
        if (percent == 0 || TemperatureAt(stock.Cell, now) < climate.MinGrowthTemperature) return (rule.RegrowIntervalTicks, false);
        return (Math.Max(1, (long)rule.RegrowIntervalTicks * 100 / percent), true);
    }

    private int TemperatureAt(int cell, long at) =>
        options.Climate.AirTemperature(at, options.Calendar) + options.Climate.TerrainOffset(terrain[cell]);

    /// <summary>Temperature (°C) of a tile at the last completed tick.</summary>
    public int ReadTemperature(int x, int y)
    {
        CheckAccess();
        if (!InBounds(x, y)) throw new ArgumentOutOfRangeException(nameof(x));
        return TemperatureAt(Cell(x, y), tick);
    }

    /// <returns>False when the item was stale (superseded token, dead entity) and did no work.</returns>
    private bool RunWork(WorkItem item, long now)
    {
        switch (item.Kind)
        {
            case WorkKind.RegrowResource:
                int node = (int)item.Subject;
                ref var stock = ref resources[node];
                var rule = resources.Config.RuleFor(stock.Kind);
                // Widen before adding: Amount + RegrowAmount may exceed int.MaxValue for large configured rules.
                if (Growth(node, now).Grows) // dormant season or too cold: no growth this time, check again later
                    stock.Amount = (int)Math.Min(rule.Capacity, (long)stock.Amount + rule.RegrowAmount);
                stock.RegrowScheduled = false;
                ScheduleRegrowth(node, now);
                break;
            case WorkKind.EntityDeath:
                RunDeath(item.Subject, now);
                break;
            case WorkKind.OldAge:
                RunOldAge(item, now);
                break;
            case WorkKind.Birth:
                RunBirth(item, now);
                break;
            case WorkKind.CropGrow:
                RunCropGrow(item, now);
                break;
            case WorkKind.CarcassDecay:
                RunDecay(item, now);
                break;
            case WorkKind.Think:
            case WorkKind.ActionStep:
                return RunMindWork(item, now);
            case WorkKind.TestNoop:
                break;
            default:
                throw new InvalidOperationException($"Unknown work kind {item.Kind}.");
        }
        return true;
    }

    /// <summary>Test hook for scheduler ordering/budget checks through the real world loop.</summary>
    internal void ScheduleForTest(long dueTick, int priority, long entityId, bool isRequired) =>
        scheduler.Schedule(dueTick, priority, entityId, new WorkItem(WorkKind.TestNoop, 0), isRequired);

    internal int PendingDiscretionaryWork => scheduler.DiscretionaryCount;

    /// <summary>Guard-free tick for internal test phases running inside Step.</summary>
    internal long LastCompletedTickUnsafe => tick;

    // ---- Reads ----------------------------------------------------------------------------------

    public WorldSnapshot ReadSnapshot()
    {
        CheckAccess();
        if (snapshot is not null) return snapshot;
        var chunks = ImmutableArray.CreateBuilder<ImmutableArray<EntityRow>>((entities.Count + RowsPerChunk - 1) / RowsPerChunk);
        for (int start = 0; start < entities.Count; start += RowsPerChunk)
        {
            int size = Math.Min(RowsPerChunk, entities.Count - start);
            var rows = ImmutableArray.CreateBuilder<EntityRow>(size);
            for (int i = 0; i < size; i++) rows.Add(entities[start + i].Row);
            chunks.Add(rows.MoveToImmutable());
        }
        var time = WorldTime.From(tick, options.Calendar) with { Temperature = options.Climate.AirTemperature(tick, options.Calendar) };
        return snapshot = new(worldId, tick, time, chunks.MoveToImmutable());
    }

    /// <summary>Static terrain view. Not on ISimulation yet (A007 frozen); see T003 handoff.</summary>
    public MapView ReadMap()
    {
        CheckAccess();
        return map;
    }

    public ResourceView? ReadResource(int x, int y, ResourceKind kind)
    {
        CheckAccess();
        if (!InBounds(x, y)) return null;
        int node = resources.Find(Cell(x, y), kind);
        if (node < 0) return null;
        var stock = resources[node];
        return new ResourceView(x, y, kind, stock.Amount, resources.Config.RuleFor(kind).Capacity);
    }

    public int ResourceNodeCount { get { CheckAccess(); return resources.Count; } }

    /// <summary>All resource nodes with their current stock (read model for renderers; ascending cell, then kind).</summary>
    public IReadOnlyList<ResourceView> ReadResourceNodes()
    {
        CheckAccess();
        var list = new List<ResourceView>(resources.Count);
        for (int i = 0; i < resources.Count; i++)
        {
            var n = resources[i];
            list.Add(new ResourceView(n.Cell % options.MapWidth, n.Cell / options.MapWidth, n.Kind, n.Amount, resources.Config.RuleFor(n.Kind).Capacity));
        }
        return list;
    }

    public EventPage ReadEvents(long afterSequence, int limit)
    {
        CheckAccess();
        return events.Read(afterSequence, limit);
    }

    private bool InBounds(int x, int y) => (uint)x < (uint)options.MapWidth && (uint)y < (uint)options.MapHeight;
    private int Cell(int x, int y) => y * options.MapWidth + x;

    // ---- Canonical state hash (schema 2, ARCHITECTURE §4/§9 order) -------------------------------

    public string StateHash()
    {
        CheckAccess();
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        // 1. Versions, identity, configuration, clock.
        writer.Write(StateSchema); // schema (shared with the save format; bump both together)
        writer.Write(1u); // RNG version
        writer.Write(worldId.Value);
        writer.Write(options.Seed);
        writer.Write(options.MapWidth);
        writer.Write(options.MapHeight);
        writer.Write(options.EventCapacity);
        writer.Write(options.MaxEventsPerTick);
        writer.Write(options.CommandQueueCapacity);
        writer.Write(options.DiscretionaryWorkBudget);
        writer.Write(options.Calendar.TicksPerDay);
        writer.Write(options.Calendar.DaysPerSeason);
        var rc = options.Resources;
        writer.Write(rc.FoodOnGrassPerMille);
        writer.Write(rc.FoodOnForestPerMille);
        writer.Write(rc.WoodOnForestPerMille);
        writer.Write(rc.WaterOnShore);
        foreach (var rule in new[] { rc.Food, rc.Water, rc.Wood })
        {
            writer.Write(rule.Capacity);
            writer.Write(rule.RegrowAmount);
            writer.Write(rule.RegrowIntervalTicks);
        }
        var vc = options.Vitals;
        foreach (long v in new[] { vc.HungerPerTick, vc.ThirstPerTick, vc.StarvationDamagePerTick, vc.DehydrationDamagePerTick,
                     vc.RegenPerTick, vc.FoodPerUnit, vc.WaterPerUnit, vc.MaxUnitsPerAct })
            writer.Write(v);
        options.Mind.WriteCanonical(writer);
        SpeciesCatalog.WriteCanonical(writer, options.Species);
        options.Climate.WriteCanonical(writer);
        options.Settlements.WriteCanonical(writer);
        options.Trade.WriteCanonical(writer);
        var psy = options.Psyche;
        foreach (int v in new[] { psy.MemoryCap, psy.RelationshipCap, psy.FearDecayPerTick, psy.TraitJitter, psy.DangerMemoryTicks, psy.DangerAvoidRadius, psy.FearBlocksMatingAbove })
            writer.Write(v);
        var eco = options.Ecology;
        foreach (long v in new long[] { eco.CarcassDecayTicks, eco.AttackIntervalTicks, eco.HuntGiveUpTicks, eco.HuntCooldownTicks,
                     eco.MaxEntities, eco.LifespanJitterPercent, eco.ReproMaxNeed, eco.ReproMinHealth, eco.MateGiveUpTicks })
            writer.Write(v);
        writer.Write(tick);
        // 2. Counters, accepted command queue in execution order, trusted grants.
        writer.Write(ids.Next);
        writer.Write(nextCommandSequence);
        writer.Write(events.NextSequence);
        writer.Write(scheduler.NextOrdinal);
        writer.Write((uint)commands.Count);
        foreach (var q in commands)
        {
            writer.Write(q.Sequence);
            writer.Write(q.TargetTick);
            WriteCommand(writer, q.Command);
        }
        writer.Write((uint)options.Grants.Count);
        foreach (var (issuer, grant) in options.Grants) // ImmutableSortedDictionary: ascending issuer
        {
            writer.Write(issuer);
            writer.Write((int)grant);
        }
        // 3. Entities, terrain, resources.
        WriteEntities(writer); // live entities, then durable death records
        writer.Write((uint)terrain.Length);
        foreach (var t in terrain) writer.Write((byte)t);
        resources.WriteCanonical(writer);
        // 4. Scheduled work.
        scheduler.WriteCanonical(writer);
        WritePathQueue(writer);
        WriteEcology(writer);
        WriteLife(writer);
        WriteSettlements(writer);
        writer.Write(nextMemoryId);
        // 5. RNG streams, retained events, durable milestones.
        writer.Write((uint)streamTags.Length);
        foreach (uint tag in streamTags)
        {
            writer.Write(tag);
            writer.Write(0UL); // world scope
            writer.Write(streams[tag].State);
        }
        events.WriteCanonical(writer);
        writer.Write(0u); // durable milestones
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)));
    }

    private static void WriteCommand(BinaryWriter writer, GameCommand command)
    {
        writer.Write(command.WorldId.Value);
        writer.Write(command.IssuerId);
        byte[] mode = Encoding.UTF8.GetBytes(command.Mode);
        writer.Write((uint)mode.Length);
        writer.Write(mode);
        writer.Write((int)command.Kind);
        switch (command)
        {
            case SpawnEntityCommand s:
                writer.Write(s.X); writer.Write(s.Y); writer.Write(s.SpeciesKey); writer.Write((int)s.Sex);
                break;
            case TakeResourceCommand t:
                writer.Write(t.X); writer.Write(t.Y); writer.Write((byte)t.Resource); writer.Write(t.Amount);
                break;
            case SetTerrainCommand g:
                writer.Write(g.X); writer.Write(g.Y); writer.Write((byte)g.Terrain);
                break;
            case FoundSettlementCommand f:
                writer.Write(f.X); writer.Write(f.Y); writer.Write((int)f.Faction);
                byte[] townName = Encoding.UTF8.GetBytes(f.Name);
                writer.Write((uint)townName.Length);
                writer.Write(townName);
                break;
            default:
                throw new InvalidOperationException("Unknown command in queue.");
        }
    }
}
