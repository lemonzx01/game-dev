namespace FantasyWorld.Simulation;

public enum CommandKind { SpawnEntity = 1, TakeResource = 2, SetTerrain = 3, FoundSettlement = 4 }

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
    WrongDiet = 16,
    TooClose = 17,
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
    /// <summary>Actor = attacker, Target = victim, Amount = health removed.</summary>
    public const int Attacked = 203;
    /// <summary>X/Y = carcass tile, Amount = meat removed by decay (explicit sink).</summary>
    public const int CarcassDecayed = 204;
    /// <summary>Actor = mother, Target = child, Amount = father id. Durable milestone.</summary>
    public const int EntityBorn = 205;
    /// <summary>Actor = mother, Target = father.</summary>
    public const int Conceived = 206;
    /// <summary>Actor = mother, Target = father: the mother could not afford any child (or the entity cap was reached).</summary>
    public const int BirthFailed = 207;
    /// <summary>Target = settlement id, Reason = Faction, Amount = farm plots.</summary>
    public const int SettlementFounded = 210;
    /// <summary>Actor = member, Target = settlement, Reason = ResourceKind, Amount = units moved into storage.</summary>
    public const int Deposited = 211;
    /// <summary>Actor = member, Target = settlement, Amount = crop units taken from a farm plot.</summary>
    public const int Harvested = 212;
    /// <summary>Actor = member, Target = settlement, Reason = ResourceKind, Amount = units taken from a wild node.</summary>
    public const int Gathered = 213;
    /// <summary>Actor = member who died carrying, Reason = ResourceKind, Amount = units lost (explicit sink).</summary>
    public const int CarryLost = 214;
    /// <summary>Actor = trader, Target = home settlement, Reason = ResourceKind, Amount = units taken from storage.</summary>
    public const int TradeLoaded = 215;
    /// <summary>Actor = trader, Target = buyer settlement, Reason = ResourceKind, Amount = units moved into the buyer's storage.</summary>
    public const int TradeSold = 216;
    /// <summary>Actor = buyer settlement, Target = seller settlement, Reason = unit price, Amount = coins moved.</summary>
    public const int TradePaid = 217;
    /// <summary>Actor = guard, Target = intruder, Reason = settlement id.</summary>
    public const int GuardAlert = 218;
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
public sealed record SpawnEntityCommand(WorldId WorldId, long IssuerId, string Mode, int X, int Y, int SpeciesKey, Sex Sex = Sex.Unspecified)
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
