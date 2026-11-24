using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

/// <summary>Authoritative per-entity state. Vitals are an anchor; current values are derived from elapsed ticks.</summary>
internal sealed class EntityState
{
    public required EntityId Id { get; init; }
    public int X, Y, Species, Action, LifeState;
    public long BirthTick;
    public VitalsAnchor Vitals;
    public long ScheduledDeathTick = long.MaxValue; // only the item due at this tick is live; older items are stale
    public long DeathTick = -1;
    public DeathCause Cause;

    public EntityRow Row => new(Id, X, Y, Species, Action, LifeState);
}

/// <summary>Durable death milestone (ARCHITECTURE §9): kept after the entity leaves the live list.</summary>
internal readonly record struct DeathRecord(long EntityId, int Species, int X, int Y, long BirthTick, long DeathTick,
    DeathCause Cause, long Hunger, long Thirst, long EventSequence);

public sealed partial class SimulationWorld
{
    private readonly List<DeathRecord> deaths = new();          // ascending EventSequence
    private readonly Dictionary<long, int> deathIndex = new();  // EntityId -> index; lookup only, never iterated

    private long SpawnEntity(int x, int y, int species, long now)
    {
        var id = ids.Allocate();
        var entity = new EntityState
        {
            Id = id, X = x, Y = y, Species = species, LifeState = LifeStates.Alive, BirthTick = now,
            Vitals = new VitalsAnchor(now, 0, 0, VitalsConfig.MaxHealth),
        };
        entities.Add(entity); // IDs are monotonic, so the list stays sorted by ID
        ScheduleDeath(entity);
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
        var v = e.Vitals.At(now, options.Vitals);
        e.LifeState = LifeStates.Dead;
        e.Action = 0; // actions invalidated immediately
        e.DeathTick = now;
        e.Cause = cause;
        e.ScheduledDeathTick = long.MaxValue;
        // Death event and durable record are written together; deaths are never dropped by the per-tick cap.
        long sequence = Emit(new WorldEvent(0, now, EventKinds.EntityDied, 0, 0, e.Id.Value, e.X, e.Y, (int)cause), critical: true);
        deathIndex.Add(e.Id.Value, deaths.Count);
        deaths.Add(new DeathRecord(e.Id.Value, e.Species, e.X, e.Y, e.BirthTick, now, cause, v.Hunger, v.Thirst, sequence));
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
        if (kind is not (ResourceKind.Food or ResourceKind.Water)) return CommandReason.InvalidCommand;

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
            0, e.Id.Value, 0, nx, ny, 0, units));
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
                    v.Hunger, v.Thirst, v.Health);
            }
        }
        if (deathIndex.TryGetValue(entityId.Value, out int d))
        {
            var r = deaths[d];
            return new EntityDetails(worldId, entityId, tick, LifeStates.Dead, r.Species, r.X, r.Y, r.BirthTick,
                r.Hunger, r.Thirst, 0, r.DeathTick, r.Cause);
        }
        return null;
    }

    public HistoryPage ReadHistory(long afterSequence, int limit)
    {
        CheckAccess();
        if (afterSequence < 0 || afterSequence > events.Latest || limit is < 1 or > 256)
            return new(PageStatus.InvalidRequest, [], afterSequence);
        int lo = 0, hi = deaths.Count;
        while (lo < hi) // first record with EventSequence > afterSequence
        {
            int mid = (lo + hi) >>> 1;
            if (deaths[mid].EventSequence <= afterSequence) lo = mid + 1; else hi = mid;
        }
        int count = Math.Min(limit, deaths.Count - lo);
        if (count == 0) return new(PageStatus.Ok, [], events.Latest);
        var page = ImmutableArray.CreateBuilder<WorldEvent>(count);
        for (int k = lo; k < lo + count; k++)
        {
            var r = deaths[k];
            page.Add(new WorldEvent(r.EventSequence, r.DeathTick, EventKinds.EntityDied, 0, 0, r.EntityId, r.X, r.Y, (int)r.Cause));
        }
        var records = page.MoveToImmutable();
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
        }
    }

    // ---- Test hooks -----------------------------------------------------------------------------
    internal long NextTickUnsafe => tick + 1;
    internal int LiveEntityCount => entities.Count;
    internal int RequiredWorkCount => scheduler.RequiredCount;
}
