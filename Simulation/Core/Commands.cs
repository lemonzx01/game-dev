namespace FantasyWorld.Simulation;

public enum CommandKind { SpawnEntity = 1, TakeResource = 2, SetTerrain = 3 }

public static class CommandModes
{
    public const string Observer = "Observer";
    public const string God = "God";
}

/// <summary>Trusted session grants. Supplied only through <see cref="SimulationOptions.Grants"/> (N4), never by a command.</summary>
[Flags]
public enum AuthorityGrant { None = 0, Observe = 1, God = 2 }

/// <summary>Stable numeric reason codes; values are part of the event/hash schema.</summary>
public enum CommandReason
{
    None = 0,
    InvalidWorld = 1,
    InvalidCommand = 2,
    InvalidMode = 3,
    Unauthorized = 4,
    QueueFull = 5,
    OutOfBounds = 6,
    Impassable = 7,
    NoResource = 8,
    InsufficientStock = 9,
    InvalidAmount = 10,
    ActorDead = 11,
    UnknownEntity = 12,
    NotNeeded = 13,
    HasResource = 14,
    Occupied = 15,
}

public static class EventKinds
{
    public const int CommandApplied = 100;
    public const int CommandRejected = 101;
    /// <summary>Summary of system events that exceeded the per-tick cap (Amount = dropped count).</summary>
    public const int EventsDropped = 199;
    /// <summary>Target = EntityId, Reason = <see cref="DeathCause"/>.</summary>
    public const int EntityDied = 200;
    /// <summary>Actor = EntityId, X/Y = resource tile, Amount = units consumed.</summary>
    public const int EntityAte = 201;
    public const int EntityDrank = 202;
    /// <summary>Actor = EntityId, Target = threat id (Flee), X/Y = position, Reason = <see cref="ActionKind"/>.</summary>
    public const int ActionStarted = 300;
    /// <summary>Actor = EntityId, Reason = <see cref="ActionKind"/> whose path search found nothing reachable.</summary>
    public const int PathFailed = 301;
}

public static class LifeStates
{
    public const int Alive = 1;
    public const int Dead = 2;
}

/// <summary>God tool: create an entity on a passable tile. Full components arrive in T004.</summary>
public sealed record SpawnEntityCommand(WorldId WorldId, long IssuerId, string Mode, int X, int Y, int SpeciesKey)
    : GameCommand(WorldId, IssuerId, Mode)
{
    public override CommandKind Kind => CommandKind.SpawnEntity;
}

/// <summary>God tool: remove resource units from a tile (explicit sink). Fails if stock is insufficient at execution.</summary>
public sealed record TakeResourceCommand(WorldId WorldId, long IssuerId, string Mode, int X, int Y, ResourceKind Resource, int Amount)
    : GameCommand(WorldId, IssuerId, Mode)
{
    public override CommandKind Kind => CommandKind.TakeResource;
}

/// <summary>God tool: change one tile's terrain. Rejected on tiles with resource nodes, or when it would trap a living entity.</summary>
public sealed record SetTerrainCommand(WorldId WorldId, long IssuerId, string Mode, int X, int Y, Terrain Terrain)
    : GameCommand(WorldId, IssuerId, Mode)
{
    public override CommandKind Kind => CommandKind.SetTerrain;
}

internal readonly record struct QueuedCommand(long Sequence, long TargetTick, GameCommand Command);
