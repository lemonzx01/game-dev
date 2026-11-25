using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

/// <summary>
/// Construction-time configuration. Grants are the only source of trusted authority (N4) and are part of
/// the hashed/saved world state. Numeric limits are proposed defaults pending measurement (T014).
/// </summary>
public sealed record SimulationOptions
{
    public required WorldId WorldId { get; init; }
    public ulong Seed { get; init; }
    public int MapWidth { get; init; } = 128;
    public int MapHeight { get; init; } = 128;
    public int EventCapacity { get; init; } = 4096;
    /// <summary>Events one tick may emit (N1). Must be ≤ EventCapacity and &gt; CommandQueueCapacity (one slot reserved for the overflow summary).</summary>
    public int MaxEventsPerTick { get; init; } = 2048;
    public int CommandQueueCapacity { get; init; } = 1024;
    /// <summary>Discretionary scheduled work items per tick (a count, never milliseconds).</summary>
    public int DiscretionaryWorkBudget { get; init; } = 256;
    public CalendarConfig Calendar { get; init; } = CalendarConfig.Default;
    public ResourceConfig Resources { get; init; } = ResourceConfig.Default;
    public VitalsConfig Vitals { get; init; } = VitalsConfig.Default;
    public MindConfig Mind { get; init; } = MindConfig.Default;
    /// <summary>Species definitions by key (see Data/species.json). Empty: every species is a plain herbivore.</summary>
    public ImmutableSortedDictionary<int, SpeciesDef> Species { get; init; } = ImmutableSortedDictionary<int, SpeciesDef>.Empty;
    public EcologyConfig Ecology { get; init; } = EcologyConfig.Default;
    public PsycheConfig Psyche { get; init; } = PsycheConfig.Default;
    public ClimateConfig Climate { get; init; } = ClimateConfig.Default;
    public SettlementConfig Settlements { get; init; } = SettlementConfig.Default;
    public TradeConfig Trade { get; init; } = TradeConfig.Default;
    public SkillConfig Skills { get; init; } = SkillConfig.Default;
    public CombatConfig Combat { get; init; } = CombatConfig.Default;
    public MagicConfig Magic { get; init; } = MagicConfig.Default;
    public TamingConfig Taming { get; init; } = TamingConfig.Default;
    public BodyConfig Body { get; init; } = BodyConfig.Default;
    /// <summary>Optional hand-built map; when set, MapWidth/MapHeight must equal its size and no terrain/resources are generated.</summary>
    public MapOverride? Map { get; init; }
    public ImmutableSortedDictionary<long, AuthorityGrant> Grants { get; init; } = ImmutableSortedDictionary<long, AuthorityGrant>.Empty;
    /// <summary>Extra world-scope RNG streams to initialize up front (stream 1 = world generation always exists).</summary>
    public ImmutableArray<uint> PrewarmStreams { get; init; } = [];

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(WorldId.Value, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MapWidth, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(MapHeight, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MapWidth, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MapHeight, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(EventCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxEventsPerTick, EventCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(CommandQueueCapacity);
        if (MaxEventsPerTick < CommandQueueCapacity + 1)
            throw new ArgumentException("MaxEventsPerTick must exceed CommandQueueCapacity so command outcomes are never dropped (N1).");
        ArgumentOutOfRangeException.ThrowIfLessThan(DiscretionaryWorkBudget, 1);
        Calendar.Validate();
        Resources.Validate();
        Vitals.Validate();
        Mind.Validate();
        SpeciesCatalog.Validate(Species);
        Ecology.Validate();
        Psyche.Validate();
        Climate.Validate();
        Settlements.Validate();
        Trade.Validate();
        Skills.Validate();
        Combat.Validate();
        Magic.Validate();
        Taming.Validate();
        Body.Validate();
        if (Map is not null && (Map.Width != MapWidth || Map.Height != MapHeight || Map.Cells.Length != MapWidth * MapHeight))
            throw new ArgumentException("Map override size must match MapWidth/MapHeight.");
        foreach (var (issuer, grant) in Grants)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(issuer, 1);
            if ((grant & ~(AuthorityGrant.Observe | AuthorityGrant.God)) != 0)
                throw new ArgumentException($"Unknown grant bits for issuer {issuer}.");
        }
    }
}
