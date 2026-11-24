using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

/// <summary>Current action shown in snapshot rows (EntityRow.Action). Values are part of the hash/event schema.</summary>
public enum ActionKind { Idle = 0, Wander = 1, Eat = 2, Drink = 3, Rest = 4, Flee = 5, Hunt = 6, Mate = 7, Work = 8, Defend = 9, Read = 10 }

/// <summary>
/// Utility AI tuning. Counts and ticks only (never milliseconds). Fatigue uses the same 0..1,000,000 milli scale
/// as needs. FearOf maps a species to the species it flees (minimal stand-in until T009 species data).
/// Defaults are provisional.
/// </summary>
public sealed record MindConfig(
    bool Enabled, int ThinkIntervalTicks, int DangerThinkIntervalTicks, int PerceptionRadius, int SearchNodeBudget,
    int MoveTicksPerTile, int CommitmentTicks, int InertiaBonus,
    long FatiguePerTick, long RestRecoveryPerTick, long RestStartFatigue, long RestEndFatigue,
    long EatStartNeed, long DrinkStartNeed, int FleeSteps,
    ImmutableSortedDictionary<int, ImmutableArray<int>> FearOf,
    int PathNodeBudgetPerTick = 4000, int UnreachableCooldownTicks = 100, int MaxReplans = 2)
{
    public const long MaxFatigue = 1_000_000;

    public static MindConfig Default { get; } = new(true, 10, 2, 8, 400, 2, 30, 150,
        231, 1000, 600_000, 50_000, 300_000, 300_000, 6, ImmutableSortedDictionary<int, ImmutableArray<int>>.Empty);

    public static MindConfig Disabled { get; } = Default with { Enabled = false };

    internal void Validate()
    {
        foreach (int v in new[] { ThinkIntervalTicks, DangerThinkIntervalTicks, PerceptionRadius, SearchNodeBudget, MoveTicksPerTile, FleeSteps })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(PerceptionRadius, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(CommitmentTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(InertiaBonus);
        foreach (long v in new[] { FatiguePerTick, RestRecoveryPerTick, RestStartFatigue, RestEndFatigue, EatStartNeed, DrinkStartNeed })
        {
            ArgumentOutOfRangeException.ThrowIfNegative(v);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(v, MaxFatigue);
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(RestRecoveryPerTick, 1);
        if (PathNodeBudgetPerTick < SearchNodeBudget) throw new ArgumentException("PathNodeBudgetPerTick must cover at least one full search.");
        ArgumentOutOfRangeException.ThrowIfLessThan(UnreachableCooldownTicks, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxReplans);
        if (RestEndFatigue >= RestStartFatigue) throw new ArgumentException("RestEndFatigue must be below RestStartFatigue (hysteresis).");
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        w.Write(Enabled);
        foreach (int v in new[] { ThinkIntervalTicks, DangerThinkIntervalTicks, PerceptionRadius, SearchNodeBudget, MoveTicksPerTile, CommitmentTicks, InertiaBonus, FleeSteps })
            w.Write(v);
        foreach (long v in new[] { FatiguePerTick, RestRecoveryPerTick, RestStartFatigue, RestEndFatigue, EatStartNeed, DrinkStartNeed })
            w.Write(v);
        w.Write(PathNodeBudgetPerTick);
        w.Write(UnreachableCooldownTicks);
        w.Write(MaxReplans);
        w.Write((uint)FearOf.Count);
        foreach (var (species, feared) in FearOf)
        {
            w.Write(species);
            var sorted = feared.Sort();
            w.Write((uint)sorted.Length);
            foreach (int f in sorted) w.Write(f);
        }
    }
}
