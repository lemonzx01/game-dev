namespace FantasyWorld.Simulation;

/// <summary>When a species is asleep (R1). Values are part of the species hash schema.</summary>
public enum SleepPattern { None = 0, Diurnal = 1, Nocturnal = 2 }

/// <summary>
/// R1 body realism: per-species hunger/thirst rates, sleep/wake cycle and faster healing at rest. Off by default so worlds
/// built for earlier scenarios keep their behaviour; the game, the benchmark and R1 tests turn it on (Enabled = true).
/// Windows are per mille of the day (0 = midnight, 500 = noon): diurnal creatures sleep from 20:00 to 05:00, nocturnal ones
/// from 09:00 to 16:00. Healing (VitalsConfig.RegenPerTick) is multiplied while resting or asleep; sleep also restores
/// fatigue faster than resting.
/// </summary>
public sealed record BodyConfig(bool Enabled = false, int DiurnalStart = 833, int DiurnalEnd = 208, int NocturnalStart = 375, int NocturnalEnd = 667,
    int SleepScore = 280, int FatigueRecoveryMultiplier = 2, int RestHealPercent = 250, int SleepHealPercent = 400,
    int AwareDistance = 2, int NoiseRadius = 6, bool GuardsKeepWatch = true,
    long InjuredRestHealth = 500_000, long RecoveredHealth = 800_000, int InjuredRestScore = 220)
{
    public static BodyConfig Default { get; } = new();

    /// <summary>The realistic setting used by the game.</summary>
    public static BodyConfig Realistic { get; } = new(Enabled: true);

    internal void Validate()
    {
        foreach (int v in new[] { DiurnalStart, DiurnalEnd, NocturnalStart, NocturnalEnd })
        {
            ArgumentOutOfRangeException.ThrowIfNegative(v);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(v, 999);
        }
        foreach (int v in new[] { SleepScore, FatigueRecoveryMultiplier, RestHealPercent, SleepHealPercent })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FatigueRecoveryMultiplier, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RestHealPercent, 1000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(SleepHealPercent, 1000);
        ArgumentOutOfRangeException.ThrowIfNegative(AwareDistance);
        ArgumentOutOfRangeException.ThrowIfNegative(NoiseRadius);
        if (DiurnalStart == DiurnalEnd || NocturnalStart == NocturnalEnd) throw new ArgumentException("Sleep windows must not be empty.");
        ArgumentOutOfRangeException.ThrowIfNegative(InjuredRestHealth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RecoveredHealth, VitalsConfig.MaxHealth);
        if (RecoveredHealth < InjuredRestHealth) throw new ArgumentException("RecoveredHealth must be at least InjuredRestHealth.");
        ArgumentOutOfRangeException.ThrowIfLessThan(InjuredRestScore, 1);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        w.Write(Enabled);
        foreach (int v in new[] { DiurnalStart, DiurnalEnd, NocturnalStart, NocturnalEnd, SleepScore, FatigueRecoveryMultiplier,
                     RestHealPercent, SleepHealPercent, AwareDistance, NoiseRadius })
            w.Write(v);
        w.Write(GuardsKeepWatch);
        w.Write(InjuredRestHealth);
        w.Write(RecoveredHealth);
        w.Write(InjuredRestScore);
    }

    /// <summary>FIX-R3 #13: with Body on, a positive base rate must not scale down to zero for any species or rest state.</summary>
    internal void ValidateRates(VitalsConfig v, IEnumerable<SpeciesDef> species)
    {
        if (!Enabled) return;
        static void Positive(long baseRate, int pct, string what)
        {
            if (baseRate > 0 && baseRate * pct / 100 == 0)
                throw new ArgumentException($"{what}: base rate {baseRate} at {pct}% rounds to zero per tick; raise the base rate or the percent.");
        }
        foreach (var d in species)
        {
            Positive(v.HungerPerTick, d.HungerPercent, $"{d.Name} hunger");
            Positive(v.ThirstPerTick, d.ThirstPercent, $"{d.Name} thirst");
        }
        Positive(v.RegenPerTick, RestHealPercent, "rest healing");
        Positive(v.RegenPerTick, SleepHealPercent, "sleep healing");
    }
}

/// <summary>
/// R1 bodies and sleep. Each species has its own hunger and thirst rates (species percent of the global VitalsConfig rates).
/// Diurnal creatures sleep at night, nocturnal ones by day: townsfolk walk home to their town centre first, animals sleep where
/// they are. Sleepers notice threats only when very close, wake when a fight happens nearby or the window ends, and heal and
/// recover faster. All rates are applied through the vitals anchor, so every existing vitals read stays correct.
/// </summary>
public sealed partial class SimulationWorld
{
    private BodyConfig BodyCfg => options.Body;

    /// <summary>Asleep (not merely walking home to sleep).</summary>
    private static bool IsAsleep(EntityState e) => e.Mind.Kind == ActionKind.Sleep && e.Mind.Resting;

    // ---- Body rates (species + rest state) through the vitals anchor --------------------------

    private void ApplyBodyRates(EntityState e)
    {
        var d = BodyCfg.Enabled ? SpeciesOf(e) : null;
        e.HungerPct = d?.HungerPercent ?? 100;
        e.ThirstPct = d?.ThirstPercent ?? 100;
        e.RegenPct = !BodyCfg.Enabled || !e.Mind.Resting ? 100 : e.Mind.Kind == ActionKind.Sleep ? BodyCfg.SleepHealPercent : BodyCfg.RestHealPercent;
        e.Vitals = e.Vitals; // re-stamp the anchor with the rates (no time change)
    }

    /// <summary>Called when the rest state changes: closes the vitals segment at `now` under the old rates, then applies the new ones.</summary>
    private void ReanchorBody(EntityState e, long now)
    {
        if (!BodyCfg.Enabled || e.LifeState != LifeStates.Alive) return; // FIX-R3 #5: Body off keeps pre-R1 anchors
        var v = e.Vitals.At(now, options.Vitals);
        e.Vitals = new VitalsAnchor(now, v.Hunger, v.Thirst, v.Health);
        ApplyBodyRates(e);
        ScheduleDeath(e);
    }

    // ---- Windows -------------------------------------------------------------------------------

    /// <summary>The sleep window in ticks of the day, or null (no sleep, Body off, or a window that collapses to nothing
    /// after rounding per mille to whole ticks; FIX-R3 #8).</summary>
    private (long Start, long End)? SleepWindow(EntityState e)
    {
        if (!BodyCfg.Enabled) return null;
        (int s, int end) = SpeciesOf(e)?.Sleep switch
        {
            SleepPattern.Diurnal => (BodyCfg.DiurnalStart, BodyCfg.DiurnalEnd),
            SleepPattern.Nocturnal => (BodyCfg.NocturnalStart, BodyCfg.NocturnalEnd),
            _ => (-1, -1),
        };
        if (s < 0) return null;
        long a = WindowTick(s), b = WindowTick(end);
        return a == b ? null : (a, b);
    }

    private long TickOfDay(long now) => now % options.Calendar.TicksPerDay;

    private long WindowTick(int perMille) => (long)options.Calendar.TicksPerDay * perMille / 1000;

    private bool InSleepWindow(EntityState e, long now)
    {
        if (SleepWindow(e) is not { } w) return false;
        long tod = TickOfDay(now);
        return w.Start < w.End ? tod >= w.Start && tod < w.End : tod >= w.Start || tod < w.End;
    }

    /// <summary>The next tick at which this creature's sleep window ends (always after `now`; long arithmetic).</summary>
    private long WakeTick(EntityState e, long now)
    {
        long day = options.Calendar.TicksPerDay;
        long end = SleepWindow(e)!.Value.End;
        long delta = ((end - TickOfDay(now)) % day + day) % day;
        return now + (delta == 0 ? day : delta);
    }

    private bool WantsToSleep(EntityState e, VitalsView v, long now) =>
        InSleepWindow(e, now) && v.Hunger < MindCfg.EatStartNeed && v.Thirst < MindCfg.DrinkStartNeed
        && !(BodyCfg.GuardsKeepWatch && e.Job == Job.Guard);

    /// <summary>Townsfolk at home in town (not on a trade trip) walk to their centre before sleeping.</summary>
    private bool SleepsAtHome(EntityState e) => SettlementOf(e) is { } s && !e.OnTrip && (e.X != s.X || e.Y != s.Y);

    // ---- Falling asleep and waking -------------------------------------------------------------

    /// <summary>FIX-R3 #3: at the actual bed-down tick (arrival, failed path) sleep only if still eligible; otherwise replan.</summary>
    private void SleepOrReplan(EntityState e, long now)
    {
        if (WantsToSleep(e, e.Vitals.At(now, options.Vitals), now)) BeginSleep(e, now);
        else FinishAction(e, now);
    }

    /// <summary>FIX-R3 #11: ticks of rest (at the rest healing rate) until a wounded creature reaches RecoveredHealth; 0 if not injured.</summary>
    private long InjuredRestTicks(EntityState e, long now)
    {
        if (!BodyCfg.Enabled) return 0;
        long health = e.Vitals.At(now, options.Vitals).Health;
        long rate = options.Vitals.RegenPerTick * BodyCfg.RestHealPercent / 100;
        if (health >= BodyCfg.InjuredRestHealth || rate <= 0) return 0;
        return Math.Min(options.Calendar.TicksPerDay, (BodyCfg.RecoveredHealth - health + rate - 1) / rate);
    }

    private bool NeedsUrgent(EntityState e, long now)
    {
        var v = e.Vitals.At(now, options.Vitals);
        return v.Hunger >= MindCfg.EatStartNeed || v.Thirst >= MindCfg.DrinkStartNeed;
    }

    private void BeginSleep(EntityState e, long now)
    {
        e.Action = (int)ActionKind.Sleep; // FIX-R3 #17: the published action is Sleep only once actually asleep
        e.Mind.Path = null;
        e.Mind.AwaitingPath = false;
        SetResting(e, true, now);               // fatigue and vitals switch to sleep rates from now on
        long wake = WakeTick(e, now);
        e.Mind.CommitUntil = wake;              // stays asleep unless a threat comes very close or a fight is heard
        ScheduleStep(e, wake);
    }

    /// <summary>Leaving sleep (any switch): closes the sleep-rate segments before the action changes.</summary>
    private void EndSleep(EntityState e, long now)
    {
        if (IsAsleep(e)) SetResting(e, false, now);
    }

    private long noiseTick = -1;
    private readonly HashSet<int> noiseCells = new();

    /// <summary>
    /// A real hit at (x, y) wakes sleepers within NoiseRadius; they decide again on the next tick. Called from Hurt for every
    /// applied hit, whether or not its Attacked event fits the event log (FIX-R3 #2). Several hits on one cell in one tick
    /// are one noise (FIX-R3 #4); the coalescing set is transient (rebuilt per tick, never decides outcomes across saves).
    /// </summary>
    private void OnNoise(int x, int y, long now)
    {
        if (!BodyCfg.Enabled || BodyCfg.NoiseRadius <= 0) return;
        if (noiseTick != now) { noiseTick = now; noiseCells.Clear(); }
        if (!noiseCells.Add(Cell(x, y))) return;
        int r = BodyCfg.NoiseRadius;
        grid.Query(x, y, r, id =>
        {
            if (FindLive(id) is not (int i and >= 0)) return;
            var o = entities[i];
            if (!IsAsleep(o) || Math.Max(Math.Abs(o.X - x), Math.Abs(o.Y - y)) > r) return;
            FinishAction(o, now);
            Emit(new WorldEvent(0, now, EventKinds.WokeUp, 0, o.Id.Value, 0, o.X, o.Y, 1, 0));
        });
    }

    /// <summary>FIX-R3 #12: after an entity is restored, its sleep/rest state and death booking must be reachable.</summary>
    private string? BodyStateError(EntityState e)
    {
        if (e.LifeState != LifeStates.Alive) return null;
        if (e.Mind.Kind == ActionKind.Sleep && SleepWindow(e) is null) return "Sleep without a sleep window (Body off or no pattern).";
        if (e.Mind.Resting && e.Mind.Kind is not (ActionKind.Rest or ActionKind.Sleep)) return "Resting without a Rest or Sleep action.";
        if (e.ScheduledDeathTick != e.Vitals.DeathTick(options.Vitals).Tick) return "Death booking does not match the restored vitals.";
        return null;
    }

    internal bool IsAsleepForTest(long id) => FindLive(id) is int i and >= 0 && IsAsleep(entities[i]);

    internal bool ForceStartForTest(long id, ActionKind kind) =>
        TryStart(entities[FindLive(id)], kind, null, null, null, null, tick, out _);

    internal void SleepOrReplanForTest(long id) => SleepOrReplan(entities[FindLive(id)], tick);

    internal long? WakeTickForTest(long id) => SleepWindow(entities[FindLive(id)]) is null ? null : WakeTick(entities[FindLive(id)], tick);

    internal long VitalsAnchorTickForTest(long id) => entities[FindLive(id)].Vitals.Tick;

    internal void SetMindForTest(long id, ActionKind kind, bool resting)
    {
        var e = entities[FindLive(id)];
        e.Mind.Kind = kind;
        e.Action = (int)kind;
        e.Mind.Resting = resting;
    }
}
