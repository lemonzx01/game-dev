using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

/// <summary>Authoritative per-entity state. Vitals are an anchor; current values are derived from elapsed ticks.</summary>
internal sealed class EntityState
{
    public required EntityId Id { get; init; }
    public int X, Y, Species, Action, LifeState;
    public int HomeX, HomeY; // spawn tile; domestic animals stay leashed to it
    public Sex Sex;
    public long MotherId, FatherId;
    public long LifespanTicks;      // 0 = does not age
    public long PregnantUntil;      // due tick, 0 = not pregnant
    public long PregnancyFather;
    public long ReproReadyTick;
    public long BirthTick;
    public VitalsAnchor Vitals;
    public long ScheduledDeathTick = long.MaxValue; // only the item due at this tick is live; older items are stale
    public long DeathTick = -1;
    public DeathCause Cause;
    public MindState Mind;

    public EntityRow Row => new(Id, X, Y, Species, Action, LifeState);
}

/// <summary>Durable death milestone (ARCHITECTURE §9): kept after the entity leaves the live list.</summary>
internal readonly record struct DeathRecord(long EntityId, int Species, int X, int Y, long BirthTick, long DeathTick,
    DeathCause Cause, long Hunger, long Thirst, long EventSequence, long Killer, Sex Sex, long MotherId, long FatherId);

public sealed partial class SimulationWorld
{
    private readonly List<DeathRecord> deaths = new();          // ascending EventSequence
    private readonly Dictionary<long, int> deathIndex = new();  // EntityId -> index; lookup only, never iterated

    /// <summary>
    /// Creates an entity. God spawns (born = false) arrive as adults (BirthTick = now − MaturityTicks); births start at age 0
    /// with their parents recorded. Sex Unspecified draws from the ecology RNG stream.
    /// </summary>
    private long SpawnEntity(int x, int y, int species, long now, Sex sex = Sex.Unspecified, long mother = 0, long father = 0, bool born = false)
    {
        var id = ids.Allocate();
        long maturity = options.Species.TryGetValue(species, out var sd) ? sd.MaturityTicks : 0;
        var entity = new EntityState
        {
            Id = id, X = x, Y = y, HomeX = x, HomeY = y, Species = species, LifeState = LifeStates.Alive,
            BirthTick = born ? now : now - maturity, MotherId = mother, FatherId = father,
            Vitals = new VitalsAnchor(now, 0, 0, VitalsConfig.MaxHealth),
        };
        entities.Add(entity); // IDs are monotonic, so the list stays sorted by ID
        grid.Add(id.Value, x, y);
        ScheduleDeath(entity);
        InitLife(entity, now, sex);
        InitMind(entity, now);
        return id.Value;
    }

    // Books the death tick implied by the current anchor as required work. A later re-anchor supersedes it.
    private void ScheduleDeath(EntityState entity)
    {
        var (deathTick, _) = entity.Vitals.DeathTick(options.Vitals);
        entity.ScheduledDeathTick = deathTick;
        if (deathTick != long.MaxValue)
            scheduler.Schedule(deathTick, 0, entity.Id.Value, new WorkItem(WorkKind.EntityDeath, entity.Id.Value), isRequired: true);
    }

    private void RunDeath(long entityId, long now)
    {
        int i = FindLive(entityId);
        if (i < 0) return;
        var e = entities[i];
        if (e.LifeState != LifeStates.Alive || e.ScheduledDeathTick != now) return; // stale booking
        var (deathTick, cause) = e.Vitals.DeathTick(options.Vitals);
        if (deathTick != now) throw new InvalidOperationException($"Entity {entityId} death booked at {now} but due {deathTick}.");
        Die(e, now, cause, 0);
    }

    /// <summary>
    /// Single death path (vitals or predation): actions cleared, one critical EntityDied event plus a durable record,
    /// and the body becomes a carcass with the species' MeatYield.
    /// </summary>
    private void Die(EntityState e, long now, DeathCause cause, long killer)
    {
        var v = e.Vitals.At(now, options.Vitals);
        e.LifeState = LifeStates.Dead;
        StartIdle(e, now);  // actions invalidated immediately (pending step superseded)
        e.Mind.ThinkToken++; // pending think superseded
        grid.Remove(e.Id.Value, e.X, e.Y);
        e.DeathTick = now;
        e.Cause = cause;
        e.ScheduledDeathTick = long.MaxValue;
        // Death event and durable record are written together; deaths are never dropped by the per-tick cap.
        long sequence = Emit(new WorldEvent(0, now, EventKinds.EntityDied, 0, killer, e.Id.Value, e.X, e.Y, (int)cause), critical: true);
        deathIndex.Add(e.Id.Value, deaths.Count);
        deaths.Add(new DeathRecord(e.Id.Value, e.Species, e.X, e.Y, e.BirthTick, now, cause, v.Hunger, v.Thirst, sequence, killer, e.Sex, e.MotherId, e.FatherId));
        milestones.Add(new WorldEvent(sequence, now, EventKinds.EntityDied, 0, killer, e.Id.Value, e.X, e.Y, (int)cause));
        AddCarcass(Cell(e.X, e.Y), SpeciesOf(e)?.MeatYield ?? 0, now);
    }

    /// <summary>
    /// Eat (Food) or drink (Water) from the entity's tile or a 4-neighbour, in ascending cell order. Takes only the
    /// whole units needed (capped by MaxUnitsPerAct and stock), so stock never goes negative. Runs inside a tick;
    /// T005 actions call this. Returns the rejection reason, or None.
    /// </summary>
    internal CommandReason TryConsume(EntityId entityId, ResourceKind kind, long now)
    {
        int i = FindLive(entityId.Value);
        if (i < 0) return deathIndex.ContainsKey(entityId.Value) ? CommandReason.ActorDead : CommandReason.UnknownEntity;
        var e = entities[i];
        if (e.LifeState != LifeStates.Alive) return CommandReason.ActorDead;
        if (kind == ResourceKind.Meat) return TryEatMeat(e, now);
        if (kind is not (ResourceKind.Food or ResourceKind.Water)) return CommandReason.InvalidCommand;
        if (kind == ResourceKind.Food && DietOf(e) == Diet.Carnivore) return CommandReason.WrongDiet;

        int node = -1, nx = -1, ny = -1;
        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (0, 0), (1, 0), (0, 1) }) // ascending cell index
        {
            int x = e.X + dx, y = e.Y + dy;
            if (!InBounds(x, y)) continue;
            int found = resources.Find(Cell(x, y), kind);
            if (found >= 0 && resources[found].Amount > 0) { node = found; nx = x; ny = y; break; }
            if (found >= 0 && node < 0) { node = found; nx = x; ny = y; }
        }
        if (node < 0) return CommandReason.NoResource;

        var c = options.Vitals;
        var v = e.Vitals.At(now, c);
        long need = kind == ResourceKind.Food ? v.Hunger : v.Thirst;
        long perUnit = kind == ResourceKind.Food ? c.FoodPerUnit : c.WaterPerUnit;
        if (need == 0) return CommandReason.NotNeeded;
        ref var stock = ref resources[node];
        int units = (int)Math.Min(Math.Min((need + perUnit - 1) / perUnit, c.MaxUnitsPerAct), stock.Amount);
        if (units == 0) return CommandReason.InsufficientStock;

        stock.Amount -= units;
        long remaining = Math.Max(0, need - units * perUnit);
        e.Vitals = kind == ResourceKind.Food
            ? new VitalsAnchor(now, remaining, v.Thirst, v.Health)
            : new VitalsAnchor(now, v.Hunger, remaining, v.Health);
        ScheduleRegrowth(node, now);
        ScheduleDeath(e);
        Emit(new WorldEvent(0, now, kind == ResourceKind.Food ? EventKinds.EntityAte : EventKinds.EntityDrank,
            0, e.Id.Value, 0, nx, ny, (int)kind, units));
        return CommandReason.None;
    }

    private int FindLive(long entityId)
    {
        int lo = 0, hi = entities.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            long id = entities[mid].Id.Value;
            if (id == entityId) return mid;
            if (id < entityId) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    public EntityDetails? ReadEntityDetails(WorldId worldId, EntityId entityId)
    {
        CheckAccess();
        if (worldId != this.worldId || entityId.Value < 1 || entityId.Value >= ids.Next) return null;
        int i = FindLive(entityId.Value);
        if (i >= 0)
        {
            var e = entities[i];
            if (e.LifeState == LifeStates.Alive)
            {
                var v = e.Vitals.At(tick, options.Vitals);
                return new EntityDetails(worldId, entityId, tick, e.LifeState, e.Species, e.X, e.Y, e.BirthTick,
                    v.Hunger, v.Thirst, v.Health, null, DeathCause.None, e.Sex, e.MotherId, e.FatherId,
                    tick - e.BirthTick, StageOf(e, tick), e.PregnantUntil);
            }
        }
        if (deathIndex.TryGetValue(entityId.Value, out int d))
        {
            var r = deaths[d];
            return new EntityDetails(worldId, entityId, tick, LifeStates.Dead, r.Species, r.X, r.Y, r.BirthTick,
                r.Hunger, r.Thirst, 0, r.DeathTick, r.Cause, r.Sex, r.MotherId, r.FatherId, r.DeathTick - r.BirthTick);
        }
        return null;
    }

    /// <summary>Pages durable milestones (births and deaths) by exclusive EventSequence cursor (ARCHITECTURE §3).</summary>
    public HistoryPage ReadHistory(long afterSequence, int limit)
    {
        CheckAccess();
        if (afterSequence < 0 || afterSequence > events.Latest || limit is < 1 or > 256)
            return new(PageStatus.InvalidRequest, [], afterSequence);
        int lo = 0, hi = milestones.Count;
        while (lo < hi) // first milestone with EventSequence > afterSequence
        {
            int mid = (lo + hi) >>> 1;
            if (milestones[mid].EventSequence <= afterSequence) lo = mid + 1; else hi = mid;
        }
        int count = Math.Min(limit, milestones.Count - lo);
        if (count == 0) return new(PageStatus.Ok, [], events.Latest);
        var records = milestones.GetRange(lo, count).ToImmutableArray();
        return new(PageStatus.Ok, records, records[^1].EventSequence);
    }

    private void WriteEntities(BinaryWriter writer)
    {
        writer.Write((uint)entities.Count);
        foreach (var e in entities)
        {
            writer.Write(e.Id.Value);
            writer.Write(e.X);
            writer.Write(e.Y);
            writer.Write(e.Species);
            writer.Write(e.Action);
            writer.Write(e.LifeState);
            writer.Write(e.BirthTick);
            writer.Write(e.Vitals.Tick);
            writer.Write(e.Vitals.Hunger);
            writer.Write(e.Vitals.Thirst);
            writer.Write(e.Vitals.Health);
            writer.Write(e.ScheduledDeathTick);
            writer.Write(e.DeathTick);
            writer.Write((int)e.Cause);
            WriteMind(writer, e.Mind);
            writer.Write((int)e.Sex);
            writer.Write(e.MotherId);
            writer.Write(e.FatherId);
            writer.Write(e.LifespanTicks);
            writer.Write(e.PregnantUntil);
            writer.Write(e.PregnancyFather);
            writer.Write(e.ReproReadyTick);
        }
        writer.Write((uint)deaths.Count);
        foreach (var r in deaths)
        {
            writer.Write(r.EntityId);
            writer.Write(r.Species);
            writer.Write(r.X);
            writer.Write(r.Y);
            writer.Write(r.BirthTick);
            writer.Write(r.DeathTick);
            writer.Write((int)r.Cause);
            writer.Write(r.Hunger);
            writer.Write(r.Thirst);
            writer.Write(r.EventSequence);
            writer.Write(r.Killer);
            writer.Write((int)r.Sex);
            writer.Write(r.MotherId);
            writer.Write(r.FatherId);
        }
    }

    // ---- Test hooks -----------------------------------------------------------------------------
    internal long NextTickUnsafe => tick + 1;
    internal int LiveEntityCount => entities.Count;
    internal int RequiredWorkCount => scheduler.RequiredCount;
}
