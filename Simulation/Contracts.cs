using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

public readonly record struct WorldId(long Value);
public readonly record struct EntityId(long Value);
public abstract record GameCommand(WorldId WorldId, long IssuerId, string Mode);
public sealed record CommandReceipt(string Status, string Reason, long? TargetTick = null, long? CommandSequence = null);
public readonly record struct EntityRow(EntityId Id, int X, int Y, int VisualKey, int Action, int LifeState);
public sealed record WorldSnapshot(WorldId WorldId, long Tick, ImmutableArray<ImmutableArray<EntityRow>> Chunks);
public sealed record EntityDetails(WorldId WorldId, EntityId EntityId, long Tick);
public readonly record struct WorldEvent(long EventSequence, long Tick, int Kind);
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
