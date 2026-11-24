using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

public readonly record struct WorldId(long Value);
public readonly record struct EntityId(long Value);

/// <summary>Every command carries world, issuer, requested authority mode, kind and a typed payload (subclass fields).</summary>
public abstract record GameCommand(WorldId WorldId, long IssuerId, string Mode)
{
    public abstract CommandKind Kind { get; }
}

public sealed record CommandReceipt(string Status, string Reason, long? TargetTick = null, long? CommandSequence = null);
public readonly record struct EntityRow(EntityId Id, int X, int Y, int VisualKey, int Action, int LifeState);
public sealed record WorldSnapshot(WorldId WorldId, long Tick, WorldTime Time, ImmutableArray<ImmutableArray<EntityRow>> Chunks);
/// <summary>Identity, life state and vitals at <paramref name="Tick"/>; for dead entities DeathTick/DeathCause come from the durable record.</summary>
public sealed record EntityDetails(WorldId WorldId, EntityId EntityId, long Tick, int LifeState = 0, int SpeciesKey = 0,
    int X = -1, int Y = -1, long BirthTick = 0, long Hunger = 0, long Thirst = 0, long Health = 0,
    long? DeathTick = null, DeathCause DeathCause = DeathCause.None);

/// <summary>
/// One committed event. Command outcome events use Actor = IssuerId, Target = created EntityId (if any),
/// Reason = <see cref="CommandReason"/> code. Amount carries a quantity (resource units, dropped-event count).
/// </summary>
public readonly record struct WorldEvent(long EventSequence, long Tick, int Kind, long CommandSequence = 0,
    long Actor = 0, long Target = 0, int X = -1, int Y = -1, int Reason = 0, long Amount = 0);

public enum PageStatus { Ok, Gap, InvalidRequest }
public sealed record EventPage(PageStatus Status, long OldestAvailable, long LatestSequence,
    ImmutableArray<WorldEvent> Records, long NextCursor, long? MissingFrom = null, long? MissingThrough = null);
public sealed record HistoryPage(PageStatus Status, ImmutableArray<WorldEvent> Records, long NextCursor);

public interface ISimulation
{
    void Step();
    CommandReceipt SubmitCommand(GameCommand command);
    WorldSnapshot ReadSnapshot();
    EventPage ReadEvents(long afterSequence, int limit);
    HistoryPage ReadHistory(long afterSequence, int limit);
    EntityDetails? ReadEntityDetails(WorldId worldId, EntityId entityId);
}
