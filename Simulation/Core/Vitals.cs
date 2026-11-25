namespace FantasyWorld.Simulation;

/// <summary>
/// Vital rates in fixed-point milli-units per tick (needs and health range 0..1,000,000).
/// Defaults are provisional balance values (GDD open decision): hunger maxes in ~3 days,
/// thirst in ~1.5 days at 2400 ticks/day; at max need, health drains in ~1 day (starvation)
/// or ~0.5 day (dehydration); health regenerates slowly while no need is at max.
/// </summary>
public sealed record VitalsConfig(
    long HungerPerTick, long ThirstPerTick, long StarvationDamagePerTick, long DehydrationDamagePerTick,
    long RegenPerTick, long FoodPerUnit, long WaterPerUnit, int MaxUnitsPerAct)
{
    public const long MaxNeed = 1_000_000;
    public const long MaxHealth = 1_000_000;
    public static VitalsConfig Default { get; } = new(139, 278, 417, 833, 100, 250_000, 100_000, 4);

    internal void Validate()
    {
        foreach (long v in new[] { HungerPerTick, ThirstPerTick, StarvationDamagePerTick, DehydrationDamagePerTick, RegenPerTick })
            ArgumentOutOfRangeException.ThrowIfNegative(v);
        foreach (long v in new[] { HungerPerTick, ThirstPerTick, StarvationDamagePerTick, DehydrationDamagePerTick, RegenPerTick })
            ArgumentOutOfRangeException.ThrowIfGreaterThan(v, MaxHealth);
        ArgumentOutOfRangeException.ThrowIfLessThan(FoodPerUnit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(WaterPerUnit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxUnitsPerAct, 1);
    }
}

/// <summary>Stable numeric death causes; values are part of the event/hash schema.</summary>
public enum DeathCause { None = 0, Starvation = 1, Dehydration = 2, Predation = 3, OldAge = 4, Combat = 5 }

/// <summary>Vital values at one tick, derived from an anchor; never stored per tick.</summary>
public readonly record struct VitalsView(long Hunger, long Thirst, long Health);

/// <summary>
/// Piecewise-linear vitals from an anchor (tick, hunger, thirst, health). Needs rise linearly and cap at
/// MaxNeed. Health regenerates (capped) while both needs are below max, and loses the damage rate of each
/// maxed need. Damage starts on the tick a need reaches max. All integer arithmetic.
/// </summary>
internal readonly record struct VitalsAnchor(long Tick, long Hunger, long Thirst, long Health)
{
    // R1: per-entity rate percentages (species hunger/thirst, rest/sleep healing); 0 means 100. They are derived from
    // hashed state (species table, rest state) and stamped by EntityState.Vitals, so they are not stored separately.
    public int HungerPct { get; init; }
    public int ThirstPct { get; init; }
    public int RegenPct { get; init; }

    private static long Scale(long rate, int pct) => pct <= 0 ? rate : rate * pct / 100;
    private long HungerRate(VitalsConfig c) => Scale(c.HungerPerTick, HungerPct);
    private long ThirstRate(VitalsConfig c) => Scale(c.ThirstPerTick, ThirstPct);

    private static long Need(long start, long rate, long elapsed) =>
        rate == 0 ? start : Math.Min(VitalsConfig.MaxNeed, start + rate * elapsed);

    // First tick at which a need equals MaxNeed (long.MaxValue if never).
    private static long MaxTick(long anchor, long start, long rate)
    {
        if (start >= VitalsConfig.MaxNeed) return anchor;
        if (rate == 0) return long.MaxValue;
        return anchor + (VitalsConfig.MaxNeed - start + rate - 1) / rate;
    }

    public VitalsView At(long tick, VitalsConfig c)
    {
        long elapsed = tick - Tick;
        if (elapsed < 0) throw new InvalidOperationException("Vitals read before anchor.");
        var (health, _) = Walk(tick, c);
        return new VitalsView(Need(Hunger, HungerRate(c), elapsed), Need(Thirst, ThirstRate(c), elapsed), health);
    }

    /// <summary>Tick on which health reaches 0 and the cause, or (long.MaxValue, None) if never.</summary>
    public (long Tick, DeathCause Cause) DeathTick(VitalsConfig c)
    {
        var (_, death) = Walk(long.MaxValue, c);
        return death;
    }

    // Walks the (at most three) constant-rate segments from the anchor to `until`, returning health at
    // `until` (floored at 0) and the death tick if it falls within the walk.
    private (long Health, (long Tick, DeathCause Cause) Death) Walk(long until, VitalsConfig c)
    {
        long th = MaxTick(Tick, Hunger, HungerRate(c));
        long tt = MaxTick(Tick, Thirst, ThirstRate(c));
        long health = Health;
        long start = Tick;
        if (health <= 0) return (0, (Tick, CauseAt(th, tt)));
        while (start < until)
        {
            long end = Math.Min(until, NextBoundary(start, th, tt));
            bool starving = start >= th, parched = start >= tt;
            long rate = (starving ? -c.StarvationDamagePerTick : 0) + (parched ? -c.DehydrationDamagePerTick : 0);
            if (!starving && !parched) rate = Scale(c.RegenPerTick, RegenPct);
            if (rate < 0)
            {
                long ticksToDeath = (health + -rate - 1) / -rate;
                if (end == long.MaxValue || ticksToDeath <= end - start)
                {
                    long deathTick = start + ticksToDeath;
                    if (deathTick <= until)
                        return (0, (deathTick, CauseAt(th, tt)));
                }
                health -= -rate * (end - start);
            }
            else if (rate > 0 && end != long.MaxValue)
            {
                health = Math.Min(VitalsConfig.MaxHealth, health + Math.Min(VitalsConfig.MaxHealth, rate * Math.Min(end - start, VitalsConfig.MaxHealth)));
            }
            if (end == long.MaxValue) break;
            start = end;
        }
        return (Math.Max(0, health), (long.MaxValue, DeathCause.None));
    }

    private static long NextBoundary(long start, long th, long tt)
    {
        long next = long.MaxValue;
        if (th > start) next = Math.Min(next, th);
        if (tt > start) next = Math.Min(next, tt);
        return next;
    }

    // The need that maxed first is the cause; dehydration wins a tie (it kills faster).
    private static DeathCause CauseAt(long th, long tt) => th < tt ? DeathCause.Starvation : DeathCause.Dehydration;
}
