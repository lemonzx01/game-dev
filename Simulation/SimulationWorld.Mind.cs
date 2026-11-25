namespace FantasyWorld.Simulation;

internal enum ScanPhase : byte { None = 0, NearestThreat = 1, Heal = 2, TownThreat = 3, Mate = 4, Tame = 5, Hunt = 6 }

internal enum ScanCacheKind : byte { NearestThreat = 1, TownThreat = 2, Heal = 3, TameUnclaimed = 4 }

/// <summary>Per-entity mind state (part of EntityState; hashed).</summary>
internal struct MindState
{
    public ActionKind Kind;
    public int[]? Path;          // remaining cells for movement actions
    public int PathIndex;
    public long TargetEntity;    // Flee: threat id
    public int FleeStepsDone;
    public long CommitUntil;     // no voluntary switch before this tick (danger still interrupts)
    public long ThinkToken;      // only the scheduled Think with this token runs
    public long StepToken;       // only the scheduled ActionStep with this token runs
    public long FatigueTick;     // fatigue anchor
    public long Fatigue;
    public bool Resting;
    public bool AwaitingPath;     // Eat/Drink waiting in the path queue
    public long NoFoodUntil;      // cooldown after an unreachable food search
    public long NoWaterUntil;
    public int Replans;           // re-plans after a blocked path in the current action
    public long HuntUntil;        // give up the current hunt at this tick
    public long NoHuntUntil;      // cooldown after a failed hunt
    public long AttackReadyTick;  // next tick an attack may land
    public long NoWorkUntil;      // cooldown after an unreachable work target
    public ScanPhase ScanPhase;
    public int ScanBucketOffset;
    public long ScanLastId;
    public long ScanBestId;
    public long ScanBestMetric;
    public int ScanExtraCount;
    public long PendingThreatId;
    public int PendingThreatDist;
    public long PendingTownThreatId;
    public long PendingMateId;
    public long PendingTameId;
}

public sealed partial class SimulationWorld
{
    public const uint AiStream = 2;
    private const int ThinkPriority = 20, StepPriority = 5;
    private static readonly (int Dx, int Dy)[] Neighbours = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    private MindConfig MindCfg => options.Mind;

    // ---- Fatigue (elapsed-time anchor, like vitals) ---------------------------------------------

    private long FatigueAt(in MindState m, long now)
    {
        long elapsed = now - m.FatigueTick;
        long recovery = MindCfg.RestRecoveryPerTick * (m.Kind == ActionKind.Sleep && BodyCfg.Enabled ? BodyCfg.FatigueRecoveryMultiplier : 1); // R1
        return m.Resting
            ? Math.Max(0, m.Fatigue - recovery * elapsed)
            : Math.Min(MindConfig.MaxFatigue, m.Fatigue + MindCfg.FatiguePerTick * elapsed);
    }

    private void SetResting(EntityState e, bool resting, long now)
    {
        if (e.Mind.Resting == resting) return;
        e.Mind.Fatigue = FatigueAt(e.Mind, now);
        e.Mind.FatigueTick = now;
        e.Mind.Resting = resting;
        ReanchorBody(e, now); // R1: healing is faster while resting or asleep
    }

    // ---- Scheduling helpers --------------------------------------------------------------------

    private void InitMind(EntityState e, long now)
    {
        e.Mind.FatigueTick = now;
        if (!MindCfg.Enabled) return;
        // Stagger first decisions by stable ID so spawns in one tick do not all think together.
        ScheduleThink(e, now + 1 + e.Id.Value % MindCfg.ThinkIntervalTicks);
    }

    private void ScheduleThink(EntityState e, long due)
    {
        e.Mind.ThinkToken++;
        scheduler.Schedule(due, ThinkPriority, e.Id.Value, new WorkItem(WorkKind.Think, e.Id.Value, e.Mind.ThinkToken), isRequired: false);
    }

    private void ScheduleStep(EntityState e, long due)
    {
        e.Mind.StepToken++;
        scheduler.Schedule(due, StepPriority, e.Id.Value, new WorkItem(WorkKind.ActionStep, e.Id.Value, e.Mind.StepToken), isRequired: true);
    }

    private EntityState? LiveFor(WorkItem item, bool think)
    {
        int i = FindLive(item.Subject);
        if (i < 0) return null;
        var e = entities[i];
        if (e.LifeState != LifeStates.Alive) return null;
        return item.Token == (think ? e.Mind.ThinkToken : e.Mind.StepToken) ? e : null;
    }

    // ---- Perception & metered scans ------------------------------------------------------------

    private long perceptionVersion;
    private long scanCacheTick = -1;
    private int scanCacheGridVersion = -1;
    private long scanCachePerceptionVersion;
    private readonly Dictionary<(ScanCacheKind Kind, int Param1, int Param2, int X, int Y), (long Id, long Metric)> scanResultCache = new();

    private int RemainingCandidateBudget() => (int)Math.Max(0, MindCfg.CandidateVisitBudgetPerTick - candidateVisitsThisTick);

    private int QuerySliceBudgetNow() => Math.Min(MindCfg.QuerySliceBudget, RemainingCandidateBudget());

    private void EnsureScanCacheValid(long now)
    {
        if (scanCacheTick == now && scanCacheGridVersion == grid.Version && scanCachePerceptionVersion == perceptionVersion) return;
        scanResultCache.Clear();
        scanCacheTick = now;
        scanCacheGridVersion = grid.Version;
        scanCachePerceptionVersion = perceptionVersion;
    }

    private static void SaveScanCursor(EntityState e, ScanPhase phase, int offset, long lastId, long bestId, long bestMetric, int extra, long now)
    {
        e.Mind.ScanPhase = phase;
        e.Mind.ScanBucketOffset = offset;
        e.Mind.ScanLastId = lastId;
        e.Mind.ScanBestId = bestId;
        e.Mind.ScanBestMetric = bestMetric;
        e.Mind.ScanExtraCount = extra;
    }

    private static void ClearScanCursor(ref MindState m)
    {
        m.ScanPhase = ScanPhase.None;
        m.ScanBucketOffset = 0;
        m.ScanLastId = 0;
        m.ScanBestId = 0;
        m.ScanBestMetric = 0;
        m.ScanExtraCount = 0;
    }

    private static void ClearScanState(ref MindState m)
    {
        ClearScanCursor(ref m);
        m.PendingThreatId = 0;
        m.PendingThreatDist = 0;
        m.PendingTownThreatId = 0;
        m.PendingMateId = 0;
        m.PendingTameId = 0;
    }

    private struct BestScanVisitor : IGridVisitor
    {
        public SimulationWorld World;
        public EntityState Actor;
        public Settlement? Town;
        public ScanPhase Phase;
        public long Now;
        public int Radius;
        public long BestId;
        public long BestMetric;
        public bool SawActiveClaim;

        public bool Visit(long id)
        {
            World.candidateVisitsThisTick++;
            long m = -1L;
            var entities = World.entities;
            switch (Phase)
            {
                case ScanPhase.NearestThreat:
                {
                    if (id == Actor.Id.Value) return true;
                    var other = entities[World.FindLive(id)];
                    if (other.LifeState == LifeStates.Alive && World.ThreatTo(Actor, other)
                        && Math.Max(Math.Abs(other.X - Actor.X), Math.Abs(other.Y - Actor.Y)) <= Radius)
                        m = Math.Abs(other.X - Actor.X) + Math.Abs(other.Y - Actor.Y);
                    break;
                }
                case ScanPhase.Heal:
                {
                    World.healVisitsThisTick++;
                    var o = entities[World.FindLive(id)];
                    if (o.LifeState == LifeStates.Alive && o.SettlementId == Actor.SettlementId
                        && Math.Max(Math.Abs(o.X - Actor.X), Math.Abs(o.Y - Actor.Y)) <= Radius)
                    {
                        long h = o.Vitals.At(Now, World.options.Vitals).Health;
                        if (h < World.MagicCfg.HealBelowHealth) m = h;
                    }
                    break;
                }
                case ScanPhase.TownThreat:
                {
                    World.guardVisitsThisTick++;
                    var other = entities[World.FindLive(id)];
                    if (other.LifeState == LifeStates.Alive && World.ThreatTo(Actor, other)
                        && Math.Max(Math.Abs(other.X - Town!.X), Math.Abs(other.Y - Town.Y)) <= Radius)
                        m = Math.Abs(other.X - Actor.X) + Math.Abs(other.Y - Actor.Y);
                    break;
                }
                case ScanPhase.Tame:
                {
                    World.tameVisitsThisTick++;
                    var o = entities[World.FindLive(id)];
                    if (o.LifeState == LifeStates.Alive && o.TamedTo == 0 && World.SpeciesOf(o) is { TameTrust: > 0 } && o.SettlementId == 0)
                    {
                        if (o.TamerId != 0 && Now < o.TameAttemptUntil) SawActiveClaim = true;
                        if (!World.HasActiveAttemptClaim(o, Actor, Now)
                            && Math.Max(Math.Abs(o.X - Actor.X), Math.Abs(o.Y - Actor.Y)) <= Radius)
                            m = Math.Abs(o.X - Actor.X) + Math.Abs(o.Y - Actor.Y);
                    }
                    break;
                }
                case ScanPhase.Hunt:
                {
                    if (id == Actor.Id.Value) return true;
                    var other = entities[World.FindLive(id)];
                    if (World.CanHunt(Actor, other)
                        && Math.Max(Math.Abs(other.X - Actor.X), Math.Abs(other.Y - Actor.Y)) <= Radius)
                        m = Math.Abs(other.X - Actor.X) + Math.Abs(other.Y - Actor.Y);
                    break;
                }
            }
            if (m >= 0 && (m < BestMetric || (m == BestMetric && BestId > 0 && id < BestId)))
            {
                BestId = id;
                BestMetric = m;
            }
            return true;
        }
    }

    private bool ValidateScanWinner(EntityState e, ScanPhase phase, Settlement? town, int radius, long now, EntityState best) => phase switch
    {
        ScanPhase.NearestThreat => best.Id.Value != e.Id.Value && best.LifeState == LifeStates.Alive && ThreatTo(e, best)
                                   && Math.Max(Math.Abs(best.X - e.X), Math.Abs(best.Y - e.Y)) <= radius,
        ScanPhase.Heal => CanHeal(e, best, now),
        ScanPhase.TownThreat => best.LifeState == LifeStates.Alive && ThreatTo(e, best)
                                && Math.Max(Math.Abs(best.X - town!.X), Math.Abs(best.Y - town.Y)) <= radius,
        ScanPhase.Tame => best.LifeState == LifeStates.Alive && best.TamedTo == 0 && SpeciesOf(best) is { TameTrust: > 0 }
                          && best.SettlementId == 0 && !HasActiveAttemptClaim(best, e, now)
                          && Math.Max(Math.Abs(best.X - e.X), Math.Abs(best.Y - e.Y)) <= radius,
        ScanPhase.Hunt => CanHunt(e, best) && Math.Max(Math.Abs(best.X - e.X), Math.Abs(best.Y - e.Y)) <= radius,
        _ => false,
    };

    private bool RunMeteredBestScan(
        EntityState e, ScanPhase phase, Settlement? town, int qx, int qy, int radius, long initialMetric, long now,
        out EntityState? result, out long resultMetric, out bool sawActiveClaim)
    {
        result = null;
        resultMetric = 0;
        sawActiveClaim = false;
        int startBucket = e.Mind.ScanPhase == phase ? e.Mind.ScanBucketOffset : 0;
        long lastId = e.Mind.ScanPhase == phase ? e.Mind.ScanLastId : 0;
        long bestId = e.Mind.ScanPhase == phase ? e.Mind.ScanBestId : 0;
        long bestMetric = e.Mind.ScanPhase == phase ? e.Mind.ScanBestMetric : initialMetric;
        int slice = QuerySliceBudgetNow();
        if (slice <= 0)
        {
            SaveScanCursor(e, phase, startBucket, lastId, bestId, bestMetric, 0, now);
            ScheduleThink(e, now + 1);
            return false;
        }
        int nextBucket = 0;
        long nextId = 0;
        var visitor = new BestScanVisitor
        {
            World = this, Actor = e, Town = town, Phase = phase, Now = now, Radius = radius,
            BestId = bestId, BestMetric = bestMetric,
        };
        bool done = grid.QueryRange(qx, qy, radius, startBucket, lastId, slice, ref nextBucket, ref nextId, ref visitor, out _);
        sawActiveClaim = visitor.SawActiveClaim;
        if (!done)
        {
            SaveScanCursor(e, phase, nextBucket, nextId, visitor.BestId, visitor.BestMetric, 0, now);
            ScheduleThink(e, now + 1);
            return false;
        }
        ClearScanCursor(ref e.Mind);
        if (visitor.BestId == 0) return true;
        if (FindLive(visitor.BestId) is int bi and >= 0 && ValidateScanWinner(e, phase, town, radius, now, entities[bi]))
        {
            result = entities[bi];
            resultMetric = visitor.BestMetric;
            return true;
        }
        // Winner died or became invalid during a multi-tick scan: restart cleanly next tick.
        SaveScanCursor(e, phase, 0, 0, 0, initialMetric, 0, now);
        ScheduleThink(e, now + 1);
        return false;
    }

    private bool RunCachedBestScan(
        EntityState e, ScanPhase phase, Settlement? town, ScanCacheKind cacheKind, int p1, int p2, int keyX, int keyY,
        int qx, int qy, int radius, long initialMetric, long now, bool shareInCache,
        out EntityState? result, out long resultMetric, out bool sawActiveClaim)
    {
        EnsureScanCacheValid(now);
        sawActiveClaim = false;
        var cacheKey = (cacheKind, p1, p2, keyX, keyY);
        if (shareInCache && scanResultCache.TryGetValue(cacheKey, out var cached))
        {
            ClearScanCursor(ref e.Mind);
            if (cached.Id == 0)
            {
                result = null;
                resultMetric = 0;
                return true;
            }
            if (FindLive(cached.Id) is int ci and >= 0 && ValidateScanWinner(e, phase, town, radius, now, entities[ci]))
            {
                result = entities[ci];
                resultMetric = cached.Metric;
                return true;
            }
            scanResultCache.Remove(cacheKey);
        }
        if (!RunMeteredBestScan(e, phase, town, qx, qy, radius, initialMetric, now, out result, out resultMetric, out sawActiveClaim))
            return false;
        if (shareInCache)
            scanResultCache[cacheKey] = (result?.Id.Value ?? 0, resultMetric);
        return true;
    }

    /// <summary>Metered scan for nearest living feared entity within the perception radius (Manhattan distance, then lower ID).</summary>
    private bool TryScanNearestThreat(EntityState e, long now, out EntityState? threat, out int distance)
    {
        threat = null;
        distance = 0;
        if (!HasFears(e)) return true;
        int r = MindCfg.PerceptionRadius;
        int townKey = e.SettlementId != 0 ? e.SettlementId : e.TamedTo;
        bool share = !(SpeciesOf(e)?.Fears.Contains(e.Species) ?? false);
        bool done = RunCachedBestScan(
            e, ScanPhase.NearestThreat, null, ScanCacheKind.NearestThreat, e.Species, townKey,
            e.X, e.Y, e.X, e.Y, r, long.MaxValue, now, share,
            out threat, out long distMetric, out _);
        if (done) distance = threat is null ? 0 : (int)distMetric;
        return done;
    }

    // Goal test for an Eat path: plants for herbivores, carcass meat for carnivores, either for omnivores.
    // Settlement members may also eat at their town centre while its storage holds food (T015).
    private bool CanEatAt(EntityState e, int cell)
    {
        if (SettlementOf(e) is { Food: > 0 } town && cell == Cell(town.X, town.Y)) return true;
        return DietOf(e) switch
        {
            Diet.Herbivore => CanConsumeAt(cell, ResourceKind.Food),
            Diet.Carnivore => CanConsumeAt(cell, ResourceKind.Meat),
            _ => CanConsumeAt(cell, ResourceKind.Food) || CanConsumeAt(cell, ResourceKind.Meat),
        };
    }

    // Food: a stocked node on the tile. Water: a stocked water node on a 4-neighbour (water tiles are impassable).
    // Meat: a carcass on the tile or a 4-neighbour.
    private bool CanConsumeAt(int cell, ResourceKind kind)
    {
        if (kind == ResourceKind.Meat)
        {
            int mx = cell % options.MapWidth, my = cell / options.MapWidth;
            if (MeatAt(cell) > 0) return true;
            foreach (var (dx, dy) in Neighbours)
                if (InBounds(mx + dx, my + dy) && MeatAt(Cell(mx + dx, my + dy)) > 0) return true;
            return false;
        }
        if (kind == ResourceKind.Food)
        {
            int n = resources.Find(cell, ResourceKind.Food);
            return n >= 0 && resources[n].Amount > 0;
        }
        int x = cell % options.MapWidth, y = cell / options.MapWidth;
        foreach (var (dx, dy) in Neighbours)
        {
            if (!InBounds(x + dx, y + dy)) continue;
            int n = resources.Find(Cell(x + dx, y + dy), ResourceKind.Water);
            if (n >= 0 && resources[n].Amount > 0) return true;
        }
        return false;
    }

    // ---- Decision ------------------------------------------------------------------------------

    /// <summary>
    /// Utility decision: perceive → score → pick the best feasible candidate (score desc, then ActionKind asc).
    /// Commitment keeps a non-idle action until CommitUntil unless danger appears; the current action also
    /// gets an inertia bonus. Both prevent rapid oscillation between close scores.
    /// </summary>
    private void Think(EntityState e, long now)
    {
        var cfg = MindCfg;
        var resumePhase = e.Mind.ScanPhase;
        bool resuming = resumePhase != ScanPhase.None;

        EntityState? threat;
        int distance;
        if (!resuming || resumePhase == ScanPhase.NearestThreat)
        {
            if (!TryScanNearestThreat(e, now, out threat, out distance)) return;
            if (threat is not null && IsAsleep(e) && distance > BodyCfg.AwareDistance) { threat = null; distance = 0; } // R1: sleepers notice only close danger
            e.Mind.PendingThreatId = threat?.Id.Value ?? 0;
            e.Mind.PendingThreatDist = distance;
            if (threat is not null) OnThreatSeen(e, threat, distance, now); // danger raises fear and is remembered
            if (!resuming)
                ScheduleThink(e, now + (threat is null ? cfg.ThinkIntervalTicks : cfg.DangerThinkIntervalTicks));
        }
        else
        {
            threat = e.Mind.PendingThreatId > 0 && FindLive(e.Mind.PendingThreatId) is int ti and >= 0
                     && entities[ti].LifeState == LifeStates.Alive && ThreatTo(e, entities[ti])
                ? entities[ti]
                : null;
            distance = threat is null ? 0 : Math.Abs(threat.X - e.X) + Math.Abs(threat.Y - e.Y);
        }

        if (!IsAsleep(e) && (!resuming || resumePhase <= ScanPhase.Heal)) // FIX-R3 #6: no casting in one's sleep
        {
            if (!TryScanHealTarget(e, now, out var healTarget)) return;
            if (healTarget is not null) CastHeal(e, healTarget, now);
        }

        var current = e.Mind.Kind;
        var town = SettlementOf(e);
        bool inTownAlarm = IsTownAlarmActiveForMember(e, town, now);
        if (threat is null && !inTownAlarm && current is not (ActionKind.Idle or ActionKind.Wander) && now < e.Mind.CommitUntil
            && !(IsAsleep(e) && NeedsUrgent(e, now))) // FIX-R3 #9: hunger or thirst wakes a sleeper
        {
            ClearScanState(ref e.Mind);
            if (resuming) ScheduleThink(e, now + cfg.ThinkIntervalTicks);
            return; // committed
        }
        if (threat is not null && current == ActionKind.Flee && e.Mind.TargetEntity == threat.Id.Value && e.Job != Job.Guard)
        {
            ClearScanState(ref e.Mind);
            if (resuming) ScheduleThink(e, now + cfg.DangerThinkIntervalTicks);
            return; // already fleeing this threat
        }

        if (!resuming || resumePhase < ScanPhase.TownThreat) MaybeBecomeGuard(e, now);

        EntityState? townThreat;
        if (!resuming || resumePhase <= ScanPhase.TownThreat)
        {
            if (!TryScanTownThreat(e, now, out townThreat)) return;
            e.Mind.PendingTownThreatId = townThreat?.Id.Value ?? 0;
        }
        else
        {
            townThreat = e.Mind.PendingTownThreatId > 0 && FindLive(e.Mind.PendingTownThreatId) is int gti and >= 0
                         && entities[gti].LifeState == LifeStates.Alive && ThreatTo(e, entities[gti])
                ? entities[gti]
                : null;
        }

        EntityState? mateTarget = null;
        if (threat is null && current != ActionKind.Mate && CanAttemptMate(e, now))
        {
            if (!resuming || resumePhase <= ScanPhase.Mate)
            {
                if (!TryScanMate(e, now, out mateTarget)) { ScheduleThink(e, now + 1); return; }
                e.Mind.PendingMateId = mateTarget?.Id.Value ?? 0;
            }
            else if (e.Mind.PendingMateId > 0 && FindLive(e.Mind.PendingMateId) is int mi and >= 0
                     && entities[mi].LifeState == LifeStates.Alive)
            {
                mateTarget = entities[mi];
            }
        }

        var v = e.Vitals.At(now, options.Vitals);
        EntityState? tameTarget = null;
        if (threat is null && current != ActionKind.Tame && !OnCooldown(e.Mind, ActionKind.Tame, now)
            && IsEligibleFeeder(e, now, out _) && v.Hunger < cfg.EatStartNeed && v.Thirst < cfg.DrinkStartNeed)
        {
            if (!resuming || resumePhase <= ScanPhase.Tame)
            {
                if (!TryScanTameCandidate(e, now, out tameTarget)) return;
                e.Mind.PendingTameId = tameTarget?.Id.Value ?? 0;
            }
            else if (e.Mind.PendingTameId > 0 && FindLive(e.Mind.PendingTameId) is int tai and >= 0
                     && entities[tai].LifeState == LifeStates.Alive)
            {
                tameTarget = entities[tai];
            }
        }

        if (resuming)
            ScheduleThink(e, now + (threat is null && !inTownAlarm ? cfg.ThinkIntervalTicks : cfg.DangerThinkIntervalTicks));

        long fatigue = FatigueAt(e.Mind, now);
        Span<(int Score, ActionKind Kind)> candidates = stackalloc (int, ActionKind)[15];
        int n = 0;
        // Guards fight threats inside the town (T016); when outmatched they raise the alarm and retreat (R2).
        ResolveCombatOrAlarmCandidates(e, town, threat, distance, townThreat, now,
            out var defendTarget, out bool guardRetreat, out bool gatherAtCentre, out inTownAlarm);
        if (inTownAlarm && threat is null && !resuming)
            ScheduleThink(e, now + cfg.DangerThinkIntervalTicks);
        if (defendTarget is not null)
            candidates[n++] = (TradeCfg.DefendScore, ActionKind.Defend);
        if (threat is not null && (!guardRetreat || PreferFleeOverGather(e, town, threat, distance, guardRetreat: true)))
            candidates[n++] = (FleeScore(e, distance, now) + (inTownAlarm && distance <= DangerCfg.VillagerFleeDistance ? DangerCfg.GatherScore : 0), ActionKind.Flee); // personality and fear decide how close is too close
        if (gatherAtCentre)
            candidates[n++] = (guardRetreat ? DangerCfg.GuardRetreatScore : DangerCfg.GatherScore, ActionKind.Gather);
        if (v.Thirst >= cfg.DrinkStartNeed || current == ActionKind.Drink)
            candidates[n++] = ((int)(v.Thirst * 12 / 10_000), ActionKind.Drink);
        if (v.Hunger >= cfg.EatStartNeed || current == ActionKind.Eat)
            candidates[n++] = ((int)(v.Hunger / 1_000), ActionKind.Eat);
        // Predators hunt only when hungry; a reachable carcass (Eat) is preferred by one point.
        if ((SpeciesOf(e)?.Hunts.Length ?? 0) > 0 && (v.Hunger >= cfg.EatStartNeed || current == ActionKind.Hunt))
            candidates[n++] = ((int)(v.Hunger / 1_000) - 1, ActionKind.Hunt);
        // Reproduction only when safe and needs are low (TryScanMate checks vitals, stage, caps and a mate in sight).
        if (threat is null && !inTownAlarm && (current == ActionKind.Mate || mateTarget is not null))
            candidates[n++] = (250 + e.Psyche.Sociability / 4, ActionKind.Mate);
        if (threat is null && !inTownAlarm && (fatigue >= cfg.RestStartFatigue || current == ActionKind.Rest))
            candidates[n++] = ((int)(fatigue * 9 / 10_000), ActionKind.Rest);
        // FIX-R3 #11: with Body on, a badly wounded creature rests to recover before working (realistic healing at rest).
        else if (BodyCfg.Enabled && threat is null && !inTownAlarm && v.Health < BodyCfg.InjuredRestHealth)
            candidates[n++] = (BodyCfg.InjuredRestScore, ActionKind.Rest);
        // Sleep in the species' window when fed and safe (R1); guards keep watch at night.
        if (threat is null && !inTownAlarm && (current == ActionKind.Sleep || WantsToSleep(e, v, now)))
            candidates[n++] = (BodyCfg.SleepScore, ActionKind.Sleep);
        // Settlement work when nothing urgent: what the town needs (deliver, harvest, gather food or wood).
        if (threat is null && !inTownAlarm && e.SettlementId != 0 && (current == ActionKind.Work || PlanWork(e) != WorkGoal.None))
            candidates[n++] = (TownCfg.WorkScore, ActionKind.Work);
        // Taming a nearby wild animal when safe, fed and the town can spare food (T019).
        if (threat is null && !inTownAlarm && (current == ActionKind.Tame || tameTarget is not null))
            candidates[n++] = (TameCfg.TameScore, ActionKind.Tame);
        // Reading when fed, safe and the town has no work for this member (T017).
        if (threat is null && !inTownAlarm && (current == ActionKind.Read || WantsToRead(e, v)))
            candidates[n++] = (SkillCfg.ReadScore, ActionKind.Read);
        candidates[n++] = (50, ActionKind.Wander);
        for (int i = 0; i < n; i++)
            if (candidates[i].Kind == current) candidates[i].Score += cfg.InertiaBonus;
        // Insertion sort by (score desc, kind asc): at most 7 candidates, no allocation (T014).
        for (int i = 1; i < n; i++)
            for (int j = i; j > 0 && (candidates[j].Score > candidates[j - 1].Score
                     || (candidates[j].Score == candidates[j - 1].Score && candidates[j].Kind < candidates[j - 1].Kind)); j--)
                (candidates[j], candidates[j - 1]) = (candidates[j - 1], candidates[j]);
        var ordered = candidates[..n];

        foreach (var (_, kind) in ordered)
        {
            if (kind == current && current != ActionKind.Wander) { ClearScanState(ref e.Mind); return; } // keep the running action, no restart
            if (TryStart(e, kind, threat, defendTarget, mateTarget, tameTarget, now, out bool yielded))
            {
                ClearScanState(ref e.Mind);
                return;
            }
            if (yielded) return;
        }
        ClearScanState(ref e.Mind);
        StartIdle(e, now);
    }

    private bool TryStart(EntityState e, ActionKind kind, EntityState? threat, EntityState? defendTarget,
        EntityState? mateTarget, EntityState? tameTarget, long now, out bool yielded)
    {
        yielded = false;
        if (kind == ActionKind.Read && (OnCooldown(e.Mind, kind, now) || !WantsToRead(e, e.Vitals.At(now, options.Vitals)))) return false;
        int[]? path = null;
        int wanderCell = -1;
        long huntTarget = 0;
        switch (kind)
        {
            case ActionKind.Eat:
            case ActionKind.Drink:
                if (OnCooldown(e.Mind, kind, now)) return false; // recently unreachable: infeasible for now
                break;
            case ActionKind.Flee:
                if (threat is null || FleeStep(e, threat) < 0) return false;
                if (e.Job == Job.Guard && SettlementOf(e) is { } fleeTown && IsTownAlarmActive(fleeTown, now)
                    && (e.Mind.Kind is not (ActionKind.Gather or ActionKind.Flee) || e.Mind.TargetEntity != fleeTown.AlarmThreatId))
                    Emit(new WorldEvent(0, now, EventKinds.GuardRetreated, 0, e.Id.Value, fleeTown.AlarmThreatId, e.X, e.Y, fleeTown.Id));
                break;
            case ActionKind.Hunt:
                if (e.Mind.NoHuntUntil > now || StageOf(e, now) == LifeStage.Juvenile) return false;
                if (!TryScanNearestPrey(e, now, out var prey)) { yielded = true; return false; }
                if (prey is null) return false;
                huntTarget = prey.Id.Value;
                break;
            case ActionKind.Mate:
                if (mateTarget is null) return false;
                huntTarget = mateTarget.Id.Value;
                break;
            case ActionKind.Defend:
                if (defendTarget is null) return false;
                huntTarget = defendTarget.Id.Value;
                if (e.Job == Job.Guard) Emit(new WorldEvent(0, now, EventKinds.GuardAlert, 0, e.Id.Value, defendTarget.Id.Value, defendTarget.X, defendTarget.Y, e.SettlementId));
                break;
            case ActionKind.Gather:
                if (SettlementOf(e) is not { } gTown || !IsTownAlarmActiveForMember(e, gTown, now)) return false;
                huntTarget = gTown.AlarmThreatId;
                if (e.Job == Job.Guard && (e.Mind.Kind is not (ActionKind.Gather or ActionKind.Flee) || e.Mind.TargetEntity != huntTarget))
                    Emit(new WorldEvent(0, now, EventKinds.GuardRetreated, 0, e.Id.Value, huntTarget, e.X, e.Y, gTown.Id));
                break;
            case ActionKind.Tame:
                if (tameTarget is null) return false;
                huntTarget = tameTarget.Id.Value;
                break;
            case ActionKind.Work:
                if (OnCooldown(e.Mind, kind, now) || PlanWork(e) is not (var goal and not WorkGoal.None)) return false;
                e.WorkGoal = goal;
                e.Job = JobFor(goal, e.Job);
                break;
            case ActionKind.Sleep:
                if (!WantsToSleep(e, e.Vitals.At(now, options.Vitals), now)) return false;
                break;
            case ActionKind.Wander:
                wanderCell = WanderStep(e);
                if (wanderCell < 0) return false;
                break;
        }

        FinishReading(e, now); // switching away mid-session credits exactly the pages read so far
        EndSleep(e, now);      // switching away from sleep closes the sleep-rate segments first (R1)
        e.Mind.Kind = kind;    // FIX-R3 #1: the incoming kind first, so the rest/heal rates derive from it (as after load)
        e.Action = kind == ActionKind.Sleep ? (int)ActionKind.Wander : (int)kind; // #17: Sleep is published once asleep
        SetResting(e, kind == ActionKind.Rest, now);
        e.Mind.Path = kind == ActionKind.Wander ? [wanderCell] : path;
        e.Mind.PathIndex = 0;
        e.Mind.TargetEntity = kind is ActionKind.Hunt or ActionKind.Mate or ActionKind.Defend or ActionKind.Tame or ActionKind.Gather ? huntTarget : threat?.Id.Value ?? 0;
        e.Mind.HuntUntil = now + (kind switch { ActionKind.Mate => options.Ecology.MateGiveUpTicks, ActionKind.Tame => TameCfg.GiveUpTicks, _ => options.Ecology.HuntGiveUpTicks });
        e.Mind.AttackReadyTick = now;
        e.Mind.FleeStepsDone = 0;
        e.Mind.Replans = 0;
        e.Mind.AwaitingPath = false;
        e.Mind.CommitUntil = now + MindCfg.CommitmentTicks;
        Emit(new WorldEvent(0, now, EventKinds.ActionStarted, 0, e.Id.Value, e.Mind.TargetEntity, e.X, e.Y, (int)kind));

        if (kind == ActionKind.Rest)
        {
            long fatigue = FatigueAt(e.Mind, now);
            long ticks = Math.Max(1, (fatigue - MindCfg.RestEndFatigue + MindCfg.RestRecoveryPerTick - 1) / MindCfg.RestRecoveryPerTick);
            ticks = Math.Max(ticks, InjuredRestTicks(e, now));
            ScheduleStep(e, now + ticks);
        }
        else if (kind is ActionKind.Eat or ActionKind.Drink or ActionKind.Work or ActionKind.Read)
        {
            RequestPath(e, kind); // searched later within the per-tick path budget
        }
        else if (kind == ActionKind.Sleep)
        {
            if (SleepsAtHome(e)) RequestPath(e, kind); // townsfolk walk home first
            else BeginSleep(e, now);   // eligibility was checked above, this tick
        }
        else
        {
            ScheduleStep(e, now + MoveTicks(e));
        }
        return true;
    }

    private void StartIdle(EntityState e, long now)
    {
        FinishReading(e, now);
        EndSleep(e, now);
        e.Mind.Kind = ActionKind.Idle; // FIX-R3 #1: incoming state first
        e.Action = (int)ActionKind.Idle;
        SetResting(e, false, now);
        ClearScanState(ref e.Mind);
        e.Mind.Path = null;
        e.Mind.AwaitingPath = false;
        e.Mind.StepToken++; // cancel any pending step or path request
    }

    private void FinishAction(EntityState e, long now)
    {
        StartIdle(e, now);
        e.Mind.CommitUntil = now;
        ScheduleThink(e, now + 1); // decide again promptly
    }

    // ---- Execution -----------------------------------------------------------------------------

    private void Step(EntityState e, long now)
    {
        switch (e.Mind.Kind)
        {
            case ActionKind.Rest:
                FinishAction(e, now); // recovery reached the target
                return;
            case ActionKind.Hunt:
            case ActionKind.Defend:
                HuntStep(e, now);
                return;
            case ActionKind.Mate:
                MateStep(e, now);
                return;
            case ActionKind.Tame:
                TameStep(e, now);
                return;
            case ActionKind.Gather:
                GatherStep(e, now);
                return;
            case ActionKind.Flee:
                var threat = FindLive(e.Mind.TargetEntity) is int ti and >= 0 ? entities[ti] : null;
                int dest = threat is { LifeState: LifeStates.Alive } ? FleeStep(e, threat) : -1;
                if (dest >= 0) MoveTo(e, dest);
                e.Mind.FleeStepsDone++;
                bool safe = threat is null || threat.LifeState != LifeStates.Alive
                    || Math.Max(Math.Abs(threat.X - e.X), Math.Abs(threat.Y - e.Y)) > MindCfg.PerceptionRadius;
                if (dest < 0 || safe || e.Mind.FleeStepsDone >= MindCfg.FleeSteps) FinishAction(e, now);
                else ScheduleStep(e, now + MoveTicks(e));
                return;
            case ActionKind.Eat:
            case ActionKind.Drink:
            case ActionKind.Work:
            case ActionKind.Read:
            case ActionKind.Sleep:
            case ActionKind.Wander:
                if (e.Mind.Kind == ActionKind.Read && e.ReadingSince > 0) { FinishAction(e, now); return; } // session over
                if (e.Mind.Kind == ActionKind.Sleep && e.Mind.Resting) { FinishAction(e, now); return; } // sleep window over
                var path = e.Mind.Path!;
                if (e.Mind.PathIndex < path.Length)
                {
                    int next = path[e.Mind.PathIndex++];
                    int nx = next % options.MapWidth, ny = next / options.MapWidth;
                    if (!TerrainRules.IsPassable(terrain[next]) || (HasAnimalLeash(e) && !WithinAnimalLeash(e, nx, ny)))
                    {
                        OnPathBlocked(e, now);
                        return;
                    }
                    MoveTo(e, next);
                    if (e.Mind.PathIndex < path.Length) { ScheduleStep(e, now + MoveTicks(e)); return; }
                }
                if (e.Mind.Kind == ActionKind.Wander)
                {
                    StartIdle(e, now); // the regular think cadence continues; no early re-think
                    return;
                }
                if (e.Mind.Kind == ActionKind.Read) { StartReading(e, now); return; }
                if (e.Mind.Kind == ActionKind.Sleep) { SleepOrReplan(e, now); return; } // FIX-R3 #3
                if (e.Mind.Kind == ActionKind.Work) { DoWork(e, now); FinishAction(e, now); return; }
                if (e.Mind.Kind == ActionKind.Eat && EatFromStorage(e, now)) { FinishAction(e, now); return; }
                if (e.Mind.Kind == ActionKind.Drink) TryConsume(e.Id, ResourceKind.Water, now);
                else if (DietOf(e) == Diet.Carnivore || TryConsume(e.Id, ResourceKind.Food, now) != CommandReason.None)
                    TryConsume(e.Id, ResourceKind.Meat, now); // omnivores fall back to meat
                FinishAction(e, now); // consumed, or the stock vanished meanwhile: re-plan
                return;
        }
    }

    private void MoveTo(EntityState e, int cell)
    {
        int x = cell % options.MapWidth, y = cell / options.MapWidth;
        grid.Move(e.Id.Value, e.X, e.Y, x, y);
        e.X = x;
        e.Y = y;
    }

    // Neighbour that maximises Manhattan distance from the threat (ties: N, W, E, S order); -1 if none improves.
    private int FleeStep(EntityState e, EntityState threat)
    {
        int best = -1, bestDistance = Math.Abs(e.X - threat.X) + Math.Abs(e.Y - threat.Y);
        foreach (var (dx, dy) in Neighbours)
        {
            int x = e.X + dx, y = e.Y + dy;
            if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)]) || !WithinAnimalLeash(e, x, y)) continue;
            int d = Math.Abs(x - threat.X) + Math.Abs(y - threat.Y);
            if (d > bestDistance) { best = Cell(x, y); bestDistance = d; }
        }
        return best;
    }

    // Deterministic step strictly closer to the leash center when a tamed/domestic animal starts outside its radius.
    private int ReturnHomeStep(EntityState e, int hx, int hy, long now)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            int best = -1, bestCheb = int.MaxValue, bestMan = int.MaxValue;
            foreach (var (dx, dy) in Neighbours)
            {
                int x = e.X + dx, y = e.Y + dy;
                if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)]) || !WithinAnimalLeash(e, x, y)) continue;
                if (pass == 0 && RemembersDangerNear(e, x, y, now)) continue;
                int cdx = Math.Abs(x - hx), cdy = Math.Abs(y - hy);
                int cheb = Math.Max(cdx, cdy), man = cdx + cdy;
                if (cheb < bestCheb || (cheb == bestCheb && man < bestMan))
                {
                    best = Cell(x, y);
                    bestCheb = cheb;
                    bestMan = man;
                }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    // Random passable neighbour from the AI stream (draws happen in scheduler order, so replay is stable).
    // Leashed animals outside their radius always take a deterministic step home; inside, pass 0 avoids remembered danger.
    private int WanderStep(EntityState e)
    {
        long now = tick + 1; // called while the next tick is being processed
        if (AnimalLeashTarget(e, out int hx, out int hy, out int radius)
            && Math.Max(Math.Abs(e.X - hx), Math.Abs(e.Y - hy)) > radius)
            return ReturnHomeStep(e, hx, hy, now);
        int start = (int)(streams[AiStream].Draw() % 4);
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < 4; k++)
            {
                var (dx, dy) = Neighbours[(start + k) % 4];
                int x = e.X + dx, y = e.Y + dy;
                if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)]) || !WithinLeash(e, x, y)) continue;
                if (pass == 0 && RemembersDangerNear(e, x, y, now)) continue;
                return Cell(x, y);
            }
        return -1;
    }

    private bool RunMindWork(WorkItem item, long now)
    {
        bool think = item.Kind == WorkKind.Think;
        if (LiveFor(item, think) is not { } e) return false; // stale, dead or superseded: no budget used
        if (think) Think(e, now); else Step(e, now);
        return true;
    }

    private void WriteMind(BinaryWriter w, in MindState m)
    {
        w.Write((int)m.Kind);
        w.Write((uint)(m.Path?.Length ?? 0));
        if (m.Path is not null) foreach (int c in m.Path) w.Write(c);
        w.Write(m.PathIndex);
        w.Write(m.TargetEntity);
        w.Write(m.FleeStepsDone);
        w.Write(m.CommitUntil);
        w.Write(m.ThinkToken);
        w.Write(m.StepToken);
        w.Write(m.FatigueTick);
        w.Write(m.Fatigue);
        w.Write(m.Resting);
        w.Write(m.AwaitingPath);
        w.Write(m.NoFoodUntil);
        w.Write(m.NoWaterUntil);
        w.Write(m.Replans);
        w.Write(m.HuntUntil);
        w.Write(m.NoHuntUntil);
        w.Write(m.AttackReadyTick);
        w.Write(m.NoWorkUntil);
        WriteMindScan(w, m);
    }

    private static void WriteMindScan(BinaryWriter w, in MindState m)
    {
        w.Write((byte)m.ScanPhase);
        if (m.ScanPhase == ScanPhase.None) return;
        w.Write(m.ScanBucketOffset);
        w.Write(m.ScanLastId);
        w.Write(m.ScanBestId);
        w.Write(m.ScanBestMetric);
        w.Write(m.ScanExtraCount);
        w.Write(m.PendingThreatId);
        w.Write(m.PendingThreatDist);
        w.Write(m.PendingTownThreatId);
        w.Write(m.PendingMateId);
        w.Write(m.PendingTameId);
    }

    private static void ReadMindScanFrom(BinaryReader r, ref MindState m, long nextId)
    {
        m.ScanPhase = (ScanPhase)r.ReadByte();
        Require(Enum.IsDefined(m.ScanPhase), "Mind scan phase invalid.");
        if (m.ScanPhase == ScanPhase.None) return;
        m.ScanBucketOffset = r.ReadInt32();
        m.ScanLastId = r.ReadInt64();
        m.ScanBestId = r.ReadInt64();
        m.ScanBestMetric = r.ReadInt64();
        m.ScanExtraCount = r.ReadInt32();
        m.PendingThreatId = r.ReadInt64();
        m.PendingThreatDist = r.ReadInt32();
        m.PendingTownThreatId = r.ReadInt64();
        m.PendingMateId = r.ReadInt64();
        m.PendingTameId = r.ReadInt64();
        Require(m.ScanBucketOffset >= 0 && m.ScanLastId >= 0
                && m.ScanBestId >= 0 && m.ScanBestId < nextId && m.ScanExtraCount >= 0
                && m.PendingThreatId >= 0 && m.PendingThreatId < nextId && m.PendingThreatDist >= 0
                && m.PendingTownThreatId >= 0 && m.PendingTownThreatId < nextId
                && m.PendingMateId >= 0 && m.PendingMateId < nextId
                && m.PendingTameId >= 0 && m.PendingTameId < nextId,
            "Mind scan state invalid.");
    }

    private long candidateVisitsThisTick, guardVisitsThisTick, healVisitsThisTick, tameVisitsThisTick;
    internal long LastCandidateVisits { get; private set; }
    internal long LastGuardCandidateVisits { get; private set; }
    internal long LastHealCandidateVisits { get; private set; }
    internal long LastTameCandidateVisits { get; private set; }
    internal int LastDiscretionaryRun { get; private set; }
    internal int PendingDiscretionary => scheduler.DiscretionaryCount;

    // ---- Test hooks ----------------------------------------------------------------------------
    internal ActionKind ActionOf(long id) => FindLive(id) is int i and >= 0 ? entities[i].Mind.Kind : ActionKind.Idle;
    internal long FatigueOf(long id) => FatigueAt(entities[FindLive(id)].Mind, tick);
}
