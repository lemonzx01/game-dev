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
    int AwareDistance = 2, int NoiseRadius = 6, bool GuardsKeepWatch = true)
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
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        w.Write(Enabled);
        foreach (int v in new[] { DiurnalStart, DiurnalEnd, NocturnalStart, NocturnalEnd, SleepScore, FatigueRecoveryMultiplier,
                     RestHealPercent, SleepHealPercent, AwareDistance, NoiseRadius })
            w.Write(v);
        w.Write(GuardsKeepWatch);
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
        if (e.LifeState != LifeStates.Alive) return;
        var v = e.Vitals.At(now, options.Vitals);
        e.Vitals = new VitalsAnchor(now, v.Hunger, v.Thirst, v.Health);
        ApplyBodyRates(e);
        ScheduleDeath(e);
    }

    // ---- Windows -------------------------------------------------------------------------------

    private (int Start, int End)? SleepWindow(EntityState e) => !BodyCfg.Enabled ? null : SpeciesOf(e)?.Sleep switch
    {
        SleepPattern.Diurnal => (BodyCfg.DiurnalStart, BodyCfg.DiurnalEnd),
        SleepPattern.Nocturnal => (BodyCfg.NocturnalStart, BodyCfg.NocturnalEnd),
        _ => null,
    };

    private int TickOfDay(long now) => (int)(now % options.Calendar.TicksPerDay);

    private int WindowTick(int perMille) => (int)((long)options.Calendar.TicksPerDay * perMille / 1000);

    private bool InSleepWindow(EntityState e, long now)
    {
        if (SleepWindow(e) is not { } w) return false;
        int tod = TickOfDay(now), s = WindowTick(w.Start), end = WindowTick(w.End);
        return s < end ? tod >= s && tod < end : tod >= s || tod < end;
    }

    /// <summary>The next tick at which this creature's sleep window ends (always after `now`).</summary>
    private long WakeTick(EntityState e, long now)
    {
        int day = options.Calendar.TicksPerDay;
        int end = WindowTick(SleepWindow(e)!.Value.End);
        long delta = ((end - TickOfDay(now)) % day + day) % day;
        return now + (delta == 0 ? day : delta);
    }

    private bool WantsToSleep(EntityState e, VitalsView v, long now) =>
        InSleepWindow(e, now) && v.Hunger < MindCfg.EatStartNeed && v.Thirst < MindCfg.DrinkStartNeed
        && !(BodyCfg.GuardsKeepWatch && e.Job == Job.Guard);

    /// <summary>Townsfolk at home in town (not on a trade trip) walk to their centre before sleeping.</summary>
    private bool SleepsAtHome(EntityState e) => SettlementOf(e) is { } s && !e.OnTrip && (e.X != s.X || e.Y != s.Y);

    // ---- Falling asleep and waking -------------------------------------------------------------

    private void BeginSleep(EntityState e, long now)
    {
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

    /// <summary>A fight at (x, y) wakes sleepers within NoiseRadius; they decide again on the next tick.</summary>
    private void OnNoise(int x, int y, long now)
    {
        if (!BodyCfg.Enabled || BodyCfg.NoiseRadius <= 0) return;
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

    internal bool IsAsleepForTest(long id) => FindLive(id) is int i and >= 0 && IsAsleep(entities[i]);
}
