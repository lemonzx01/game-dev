using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace FantasyWorld.Simulation;

public sealed class SimulationWorld : ISimulation
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
    private readonly MapView map;
    private readonly ResourceStore resources;
    private readonly List<EntityRow> entities = new(); // ascending EntityId (allocation order)
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
        terrain = MapGenerator.Generate(options.MapWidth, options.MapHeight, generation);
        map = new MapView(options.MapWidth, options.MapHeight, ImmutableArray.Create(terrain));
        resources = ResourceStore.Generate(terrain, options.MapWidth, options.MapHeight, options.Resources, generation);

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
            while (commands.Count > 0 && commands.Peek().TargetTick == next)
                Execute(commands.Dequeue(), next);
            scheduler.Run(next, item => RunWork(item, next));
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
    internal void Emit(in WorldEvent entry)
    {
        if (emittedThisTick < options.MaxEventsPerTick - 1)
        {
            events.Append(entry);
            emittedThisTick++;
        }
        else
        {
            droppedThisTick++;
        }
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
                : s.SpeciesKey < 1 ? CommandReason.InvalidCommand : CommandReason.None,
            TakeResourceCommand t => !InBounds(t.X, t.Y) ? CommandReason.OutOfBounds
                : !Enum.IsDefined(t.Resource) ? CommandReason.InvalidCommand
                : t.Amount < 1 ? CommandReason.InvalidAmount : CommandReason.None,
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
                    var id = ids.Allocate();
                    entities.Add(new EntityRow(id, s.X, s.Y, s.SpeciesKey, 0, LifeStates.Alive));
                    created = id.Value;
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
                default:
                    reason = CommandReason.InvalidCommand;
                    break;
            }
        }
        var (x, y) = command switch
        {
            SpawnEntityCommand s => (s.X, s.Y),
            TakeResourceCommand t => (t.X, t.Y),
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
        scheduler.Schedule(checked(now + rule.RegrowIntervalTicks), 10, 0, new WorkItem(WorkKind.RegrowResource, node), isRequired: true);
    }

    private void RunWork(WorkItem item, long now)
    {
        switch (item.Kind)
        {
            case WorkKind.RegrowResource:
                int node = (int)item.Subject;
                ref var stock = ref resources[node];
                var rule = resources.Config.RuleFor(stock.Kind);
                // Widen before adding: Amount + RegrowAmount may exceed int.MaxValue for large configured rules.
                stock.Amount = (int)Math.Min(rule.Capacity, (long)stock.Amount + rule.RegrowAmount);
                stock.RegrowScheduled = false;
                ScheduleRegrowth(node, now);
                break;
            case WorkKind.TestNoop:
                break;
            default:
                throw new InvalidOperationException($"Unknown work kind {item.Kind}.");
        }
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
            for (int i = 0; i < size; i++) rows.Add(entities[start + i]);
            chunks.Add(rows.MoveToImmutable());
        }
        return snapshot = new(worldId, tick, WorldTime.From(tick, options.Calendar), chunks.MoveToImmutable());
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

    public EventPage ReadEvents(long afterSequence, int limit)
    {
        CheckAccess();
        return events.Read(afterSequence, limit);
    }

    public HistoryPage ReadHistory(long afterSequence, int limit)
    {
        CheckAccess();
        if (afterSequence < 0 || afterSequence > events.Latest || limit is < 1 or > 256)
            return new(PageStatus.InvalidRequest, [], afterSequence);
        return new(PageStatus.Ok, [], events.Latest); // No durable milestones before T004/T011.
    }

    public EntityDetails? ReadEntityDetails(WorldId worldId, EntityId entityId)
    {
        CheckAccess();
        if (worldId != this.worldId || entityId.Value < 1 || entityId.Value >= ids.Next) return null;
        int i = entities.BinarySearch(new EntityRow(entityId, 0, 0, 0, 0, 0), RowById.Instance);
        return i < 0 ? null : new EntityDetails(worldId, entityId, tick);
    }

    private sealed class RowById : IComparer<EntityRow>
    {
        public static readonly RowById Instance = new();
        public int Compare(EntityRow a, EntityRow b) => a.Id.Value.CompareTo(b.Id.Value);
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
        writer.Write(2u); // schema
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
        writer.Write((uint)entities.Count);
        foreach (var e in entities)
        {
            writer.Write(e.Id.Value);
            writer.Write(e.X);
            writer.Write(e.Y);
            writer.Write(e.VisualKey);
            writer.Write(e.Action);
            writer.Write(e.LifeState);
        }
        writer.Write((uint)terrain.Length);
        foreach (var t in terrain) writer.Write((byte)t);
        resources.WriteCanonical(writer);
        // 4. Scheduled work.
        scheduler.WriteCanonical(writer);
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
                writer.Write(s.X); writer.Write(s.Y); writer.Write(s.SpeciesKey);
                break;
            case TakeResourceCommand t:
                writer.Write(t.X); writer.Write(t.Y); writer.Write((byte)t.Resource); writer.Write(t.Amount);
                break;
            default:
                throw new InvalidOperationException("Unknown command in queue.");
        }
    }
}
