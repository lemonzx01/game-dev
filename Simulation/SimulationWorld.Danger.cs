using System.Text;

namespace FantasyWorld.Simulation;

/// <summary>
/// R2 danger response tuning: threat strength assessment, town alarms, group defence rallies,
/// and avoidance of remembered dangerous areas.
/// </summary>
public sealed record DangerConfig(
    int OutmatchMultiplier = 3,
    int DefenseRadius = 10,
    int SupportRadius = 3,
    int AlarmDurationTicks = 160,
    int AlarmClearGraceTicks = 20,
    int AlarmLeaveMargin = 2,
    int GatherScore = 900,
    int GuardRetreatScore = 4100,
    int GuardSafeDistance = 6,
    int VillagerFleeDistance = 4)
{
    public static DangerConfig Default { get; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(OutmatchMultiplier, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DefenseRadius, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(SupportRadius, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(AlarmDurationTicks, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(AlarmClearGraceTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(AlarmLeaveMargin);
        ArgumentOutOfRangeException.ThrowIfLessThan(GatherScore, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(GuardRetreatScore, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(GuardSafeDistance, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(VillagerFleeDistance);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (int v in new[] { OutmatchMultiplier, DefenseRadius, SupportRadius, AlarmDurationTicks, AlarmClearGraceTicks,
                     AlarmLeaveMargin, GatherScore, GuardRetreatScore, GuardSafeDistance, VillagerFleeDistance })
            w.Write(v);
    }
}

/// <summary>
/// R2 danger responses: deterministic strength assessment, outmatched guard retreat and alarm,
/// town gather/flee behaviour, group defence rallies, and avoidance of remembered threat areas.
/// </summary>
public sealed partial class SimulationWorld
{
    private DangerConfig DangerCfg => options.Danger;

    private static bool IsTownAlarmActive(Settlement? town, long now) => town is not null && town.AlarmUntil > now;

    private bool IsTownAlarmActiveForMember(EntityState e, Settlement? town, long now) =>
        IsTownAlarmActive(town, now) && !e.OnTrip
        && Math.Max(Math.Abs(e.X - town!.X), Math.Abs(e.Y - town.Y)) <= TownCfg.Radius;

    private long EstimateFighterStrength(EntityState fighter, long now)
    {
        if (fighter.LifeState != LifeStates.Alive) return 0;
        long power = AttackPower(fighter);
        if (power <= 0) return 0;
        var v = fighter.Vitals.At(now, options.Vitals);
        if (v.Health <= 0) return 0;
        int combatBonusPct = 100 + 10 * Level(fighter, Skill.Combat);
        int atkConditionPct = (int)(50 + 50 * v.Health / VitalsConfig.MaxHealth);
        int durabilityPct = (int)(30 + 70 * v.Health / VitalsConfig.MaxHealth);
        int toughnessPct = Math.Max(1, SpeciesOf(fighter)?.DamageTakenPercent ?? 100);
        Int128 s = power;
        s = ScalePercent(s, combatBonusPct);
        s = ScalePercent(s, atkConditionPct);
        s = ScalePercent(s, durabilityPct);
        if (toughnessPct != 100)
            s = s <= Int128.MaxValue / 100 ? s * 100 / toughnessPct : (Int128)long.MaxValue;
        return s >= long.MaxValue ? long.MaxValue : (long)s;
    }

    private bool IsStrongThreat(EntityState foe, long now) =>
        EstimateFighterStrength(foe, now) > (Int128)TradeCfg.GuardDamage * DangerCfg.OutmatchMultiplier;

    private bool IsWillingTownDefender(EntityState actor, EntityState o, EntityState foe, Settlement? town, long now)
    {
        if (o.LifeState != LifeStates.Alive || IsAsleep(o) || !CanHarm(o, foe) || AttackPower(o) <= 0) return false;
        if (town is not null)
        {
            if (o.SettlementId != town.Id || o.OnTrip) return false;
            bool fightingSameFoe = o.Mind.Kind == ActionKind.Defend && o.Mind.TargetEntity == foe.Id.Value;
            bool nearbyActor = Math.Max(Math.Abs(o.X - actor.X), Math.Abs(o.Y - actor.Y)) <= DangerCfg.SupportRadius;
            bool ralliedAtCentre = town.AlarmUntil > now
                                   && Math.Max(Math.Abs(actor.X - town.X), Math.Abs(actor.Y - town.Y)) <= 1
                                   && Math.Max(Math.Abs(o.X - town.X), Math.Abs(o.Y - town.Y)) <= 2;
            if (!fightingSameFoe && !nearbyActor && !ralliedAtCentre) return false;
            if (o.Job == Job.Guard || fightingSameFoe) return true;
            return IsSapient(SpeciesOf(o)) && StageOf(o, now) != LifeStage.Juvenile
                && o.Psyche.Bravery >= CombatCfg.FightBackBravery;
        }
        return o.SettlementId == 0 && o.Species == foe.Species;
    }

    private struct AlliedDefenseVisitor : IGridVisitor
    {
        public SimulationWorld World;
        public EntityState Actor, Foe;
        public Settlement? Town;
        public int CenterX, CenterY, Radius;
        public long Now;
        public long FoeStrength;
        public Int128 PowerSum;
        public int Defenders;

        public bool Visit(long id)
        {
            World.candidateVisitsThisTick++;
            if (id == Actor.Id.Value) return true;
            int idx = World.FindLive(id);
            if (idx < 0) return true;
            var o = World.entities[idx];
            if (Math.Max(Math.Abs(o.X - CenterX), Math.Abs(o.Y - CenterY)) > Radius) return true;
            if (!World.IsWillingTownDefender(Actor, o, Foe, Town, Now)) return true;

            PowerSum += World.EstimateFighterStrength(o, Now);
            Defenders++;
            Int128 ourWithBonus = World.AlliedStrengthWithBonus(PowerSum, Defenders);
            if (ourWithBonus * World.DangerCfg.OutmatchMultiplier >= FoeStrength) return false; // short-circuit once threshold met
            return true;
        }
    }

    private Int128 AlliedStrengthWithBonus(Int128 rawPowerSum, int defenderCount)
    {
        if (defenderCount <= 0 || rawPowerSum <= 0) return 0;
        int allies = Math.Min(CombatCfg.MaxAllies, defenderCount - 1);
        return ScalePercent(rawPowerSum, 100 + CombatCfg.AllyBonusPercent * allies);
    }

    private long EstimateAlliedStrength(EntityState self, EntityState foe, Settlement? town, long now, long foeStrength, out int defenderCount)
    {
        int centerX = town?.X ?? self.X;
        int centerY = town?.Y ?? self.Y;
        int radius = town is not null ? Math.Max(TownCfg.Radius, DangerCfg.DefenseRadius) : DangerCfg.DefenseRadius;
        Int128 selfPower = IsWillingTownDefender(self, self, foe, town, now) ? EstimateFighterStrength(self, now) : 0;
        int startDefenders = selfPower > 0 ? 1 : 0;

        Int128 immediate = AlliedStrengthWithBonus(selfPower, startDefenders);
        if (foeStrength > 0 && immediate * DangerCfg.OutmatchMultiplier >= foeStrength)
        {
            defenderCount = startDefenders;
            return immediate >= long.MaxValue ? long.MaxValue : (long)immediate;
        }

        int maxVisits = QuerySliceBudgetNow();
        var visitor = new AlliedDefenseVisitor
        {
            World = this,
            Actor = self,
            Foe = foe,
            Town = town,
            CenterX = centerX,
            CenterY = centerY,
            Radius = radius,
            Now = now,
            FoeStrength = foeStrength,
            PowerSum = selfPower,
            Defenders = startDefenders,
        };
        if (maxVisits > 0)
        {
            int dummyBucket = 0;
            long dummyId = 0;
            grid.QueryRange(centerX, centerY, radius, 0, 0, maxVisits, ref dummyBucket, ref dummyId, ref visitor, out _);
        }
        defenderCount = visitor.Defenders;
        Int128 withBonus = AlliedStrengthWithBonus(visitor.PowerSum, visitor.Defenders);
        return withBonus >= long.MaxValue ? long.MaxValue : (long)withBonus;
    }

    private bool IsOutmatchedAgainst(EntityState actor, EntityState foe, Settlement? town, long now)
    {
        long foeStrength = EstimateFighterStrength(foe, now);
        if (foeStrength <= 0) return false;
        long ourStrength = EstimateAlliedStrength(actor, foe, town, now, foeStrength, out _);
        return (Int128)foeStrength > (Int128)ourStrength * DangerCfg.OutmatchMultiplier;
    }

    private struct AlarmInterruptVisitor : IGridVisitor
    {
        public SimulationWorld World;
        public Settlement Town;
        public EntityState Threat;
        public long Now;

        public bool Visit(long id)
        {
            World.candidateVisitsThisTick++;
            int idx = World.FindLive(id);
            if (idx < 0) return true;
            var m = World.entities[idx];
            if (m.LifeState != LifeStates.Alive || m.SettlementId != Town.Id || m.OnTrip) return true;
            if (Math.Max(Math.Abs(m.X - Town.X), Math.Abs(m.Y - Town.Y)) > World.TownCfg.Radius) return true;

            World.Remember(m, MemoryKind.SawThreat, Threat.Id.Value, Threat.X, Threat.Y, Now);
            if (m.Job != Job.Guard && m.Mind.Kind is ActionKind.Work or ActionKind.Read or ActionKind.Tame or ActionKind.Sleep or ActionKind.Rest)
                World.FinishAction(m, Now);
            return true;
        }
    }

    private void RaiseTownAlarm(Settlement town, EntityState guard, EntityState threat, long now)
    {
        Remember(guard, MemoryKind.SawThreat, threat.Id.Value, threat.X, threat.Y, now);
        bool newlyRaised = town.AlarmUntil <= now || town.AlarmThreatId != threat.Id.Value;
        town.AlarmUntil = Math.Max(town.AlarmUntil, now + DangerCfg.AlarmDurationTicks);
        town.AlarmThreatId = threat.Id.Value;
        town.AlarmThreatX = threat.X;
        town.AlarmThreatY = threat.Y;
        town.AlarmLastSeenTick = now;
        if (!newlyRaised) return;

        Emit(new WorldEvent(0, now, EventKinds.TownAlarm, 0, guard.Id.Value, threat.Id.Value, threat.X, threat.Y, town.Id, 1));
        int budget = Math.Max(64, QuerySliceBudgetNow());
        int dummyBucket = 0;
        long dummyId = 0;
        var visitor = new AlarmInterruptVisitor { World = this, Town = town, Threat = threat, Now = now };
        grid.QueryRange(town.X, town.Y, TownCfg.Radius, 0, 0, budget, ref dummyBucket, ref dummyId, ref visitor, out _);
    }

    private void TickDangerAlarms(long now)
    {
        foreach (var town in settlements)
        {
            if (town.AlarmUntil <= 0) continue;
            int ti = town.AlarmThreatId > 0 ? FindLive(town.AlarmThreatId) : -1;
            var foe = ti >= 0 ? entities[ti] : null;
            bool stillInTown = foe is { LifeState: LifeStates.Alive }
                && Math.Max(Math.Abs(foe.X - town.X), Math.Abs(foe.Y - town.Y)) <= TownCfg.Radius + DangerCfg.AlarmLeaveMargin;
            if (stillInTown)
            {
                town.AlarmThreatX = foe!.X;
                town.AlarmThreatY = foe.Y;
                town.AlarmLastSeenTick = now;
                town.AlarmUntil = Math.Max(town.AlarmUntil, now + DangerCfg.AlarmDurationTicks);
                continue;
            }

            long clearAt = Math.Min(town.AlarmUntil, town.AlarmLastSeenTick + DangerCfg.AlarmClearGraceTicks);
            town.AlarmUntil = clearAt;
            if (now >= clearAt)
            {
                town.AlarmUntil = 0;
                town.AlarmThreatId = 0;
                town.AlarmThreatX = 0;
                town.AlarmThreatY = 0;
                town.AlarmLastSeenTick = 0;
                Emit(new WorldEvent(0, now, EventKinds.TownAlarmEnded, 0, 0, town.Id, town.X, town.Y, town.Id));
            }
        }
    }

    private EntityState? ActiveTownStrongThreat(EntityState e, EntityState? localThreat, Settlement town, long now)
    {
        if (localThreat is not null && IsStrongThreat(localThreat, now) && CanHarm(e, localThreat))
            return localThreat;
        if (town.AlarmUntil > now && town.AlarmThreatId > 0 && FindLive(town.AlarmThreatId) is int ai and >= 0)
        {
            var alarmFoe = entities[ai];
            if (alarmFoe.LifeState == LifeStates.Alive && CanHarm(e, alarmFoe)
                && Math.Max(Math.Abs(alarmFoe.X - town.X), Math.Abs(alarmFoe.Y - town.Y)) <= TownCfg.Radius)
                return alarmFoe;
        }
        return null;
    }

    private void ResolveCombatOrAlarmCandidates(
        EntityState e, Settlement? town, EntityState? threat, int distance, EntityState? townThreat, long now,
        out EntityState? defendTarget, out bool guardRetreat, out bool gatherAtCentre, out bool inTownAlarm)
    {
        defendTarget = null;
        guardRetreat = false;
        gatherAtCentre = false;
        inTownAlarm = IsTownAlarmActiveForMember(e, town, now);

        if (e.Job == Job.Guard && townThreat is not null && town is not null)
        {
            if (IsOutmatchedAgainst(e, townThreat, town, now))
            {
                RaiseTownAlarm(town, e, townThreat, now);
                inTownAlarm = IsTownAlarmActiveForMember(e, town, now);
                guardRetreat = true;
            }
            else
            {
                defendTarget = townThreat;
            }
        }
        else
        {
            if (FightsBack(e, threat, distance, now))
            {
                bool cornered = threat is not null && FleeStep(e, threat) < 0;
                if (cornered || threat is null || !IsStrongThreat(threat, now) || !IsOutmatchedAgainst(e, threat, town, now))
                    defendTarget = threat;
            }

            if (defendTarget is null && town is not null && !e.OnTrip
                && IsSapient(SpeciesOf(e)) && StageOf(e, now) != LifeStage.Juvenile
                && e.Psyche.Bravery >= CombatCfg.FightBackBravery
                && Math.Max(Math.Abs(e.X - town.X), Math.Abs(e.Y - town.Y)) <= TownCfg.Radius)
            {
                var strongFoe = ActiveTownStrongThreat(e, threat, town, now);
                if (strongFoe is not null && !IsOutmatchedAgainst(e, strongFoe, town, now))
                    defendTarget = strongFoe;
            }

            inTownAlarm = IsTownAlarmActiveForMember(e, town, now);
        }

        if (guardRetreat || (inTownAlarm && defendTarget is null))
            gatherAtCentre = true;
    }

    private bool PreferFleeOverGather(EntityState e, Settlement? town, EntityState? threat, int distance, bool guardRetreat)
    {
        if (threat is null) return false;
        if (guardRetreat)
            return distance <= DangerCfg.VillagerFleeDistance && FleeStep(e, threat) >= 0;
        return distance <= DangerCfg.VillagerFleeDistance;
    }

    private void GatherStep(EntityState e, long now)
    {
        var town = SettlementOf(e);
        if (town is null || !IsTownAlarmActiveForMember(e, town, now))
        {
            FinishAction(e, now);
            return;
        }

        long foeId = e.Mind.TargetEntity > 0 ? e.Mind.TargetEntity : town.AlarmThreatId;
        var foe = foeId > 0 && FindLive(foeId) is int fi and >= 0 && entities[fi].LifeState == LifeStates.Alive ? entities[fi] : null;
        if (e.Job == Job.Guard)
        {
            if (foe is null || Math.Max(Math.Abs(foe.X - town.X), Math.Abs(foe.Y - town.Y)) > TownCfg.Radius
                || !IsOutmatchedAgainst(e, foe, town, now))
            {
                FinishAction(e, now); // threat gone/left or reinforcements arrived: re-evaluate immediately
                return;
            }
        }

        int nextCell = -1;
        if (foe is not null && Math.Abs(foe.X - e.X) + Math.Abs(foe.Y - e.Y) <= DangerCfg.GuardSafeDistance)
        {
            nextCell = GuardRetreatStep(e, town, foe, now);
        }
        else if (e.X != town.X || e.Y != town.Y)
        {
            nextCell = StepTowardTownCentre(e, town, foe, now);
        }

        if (nextCell >= 0)
        {
            MoveTo(e, nextCell);
            ScheduleStep(e, now + MoveTicks(e));
            return;
        }
        ScheduleStep(e, now + MindCfg.DangerThinkIntervalTicks);
    }

    private int GuardRetreatStep(EntityState e, Settlement town, EntityState foe, long now)
    {
        int curFoeDist = Math.Abs(e.X - foe.X) + Math.Abs(e.Y - foe.Y);
        for (int pass = 0; pass < 2; pass++)
        {
            int best = -1, bestFoeDist = curFoeDist, bestHomeDist = int.MaxValue;
            foreach (var (dx, dy) in Neighbours)
            {
                int x = e.X + dx, y = e.Y + dy;
                if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)])) continue;
                if (Math.Max(Math.Abs(x - town.X), Math.Abs(y - town.Y)) > TownCfg.Radius) continue;
                if (pass == 0 && RemembersDangerNear(e, x, y, now)) continue;
                int fd = Math.Abs(x - foe.X) + Math.Abs(y - foe.Y);
                int hd = Math.Abs(x - town.X) + Math.Abs(y - town.Y);
                if (fd > bestFoeDist || (fd == bestFoeDist && fd > curFoeDist && hd < bestHomeDist))
                {
                    best = Cell(x, y);
                    bestFoeDist = fd;
                    bestHomeDist = hd;
                }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    private int StepTowardTownCentre(EntityState e, Settlement town, EntityState? foe, long now)
    {
        int curDist = Math.Abs(e.X - town.X) + Math.Abs(e.Y - town.Y);
        for (int pass = 0; pass < 2; pass++)
        {
            int best = -1, bestDist = curDist;
            foreach (var (dx, dy) in Neighbours)
            {
                int x = e.X + dx, y = e.Y + dy;
                if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)])) continue;
                if (pass == 0 && RemembersDangerNear(e, x, y, now)) continue;
                if (pass == 0 && foe is not null && Math.Abs(x - foe.X) + Math.Abs(y - foe.Y) <= DangerCfg.VillagerFleeDistance) continue;
                int d = Math.Abs(x - town.X) + Math.Abs(y - town.Y);
                if (d < bestDist) { best = Cell(x, y); bestDist = d; }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    private void WriteDanger(BinaryWriter w)
    {
        w.Write((uint)settlements.Count);
        foreach (var s in settlements)
        {
            w.Write(s.AlarmUntil);
            w.Write(s.AlarmThreatId);
            w.Write(s.AlarmThreatX);
            w.Write(s.AlarmThreatY);
            w.Write(s.AlarmLastSeenTick);
        }
    }

    private void ReadDangerFrom(BinaryReader r)
    {
        int n = Count(r, settlements.Count);
        Require(n == settlements.Count, "Danger settlement count differs.");
        for (int i = 0; i < n; i++)
        {
            var s = settlements[i];
            s.AlarmUntil = r.ReadInt64();
            s.AlarmThreatId = r.ReadInt64();
            s.AlarmThreatX = r.ReadInt32();
            s.AlarmThreatY = r.ReadInt32();
            s.AlarmLastSeenTick = r.ReadInt64();
            Require(s.AlarmUntil >= 0 && s.AlarmThreatId >= 0 && s.AlarmThreatId < ids.Next
                    && s.AlarmLastSeenTick >= 0 && s.AlarmLastSeenTick <= tick,
                "Settlement danger alarm state invalid.");
            if (s.AlarmUntil == 0)
            {
                Require(s.AlarmThreatId == 0 && s.AlarmThreatX == 0 && s.AlarmThreatY == 0 && s.AlarmLastSeenTick == 0,
                    "Cleared settlement alarm must zero trailing fields.");
            }
            else
            {
                Require(s.AlarmThreatId > 0 && InBounds(s.AlarmThreatX, s.AlarmThreatY),
                    "Active settlement alarm missing valid threat.");
            }
        }
    }

    internal bool IsTownAlarmActiveForTest(int settlementId) =>
        settlementId >= 1 && settlementId <= settlements.Count && settlements[settlementId - 1].AlarmUntil > tick;
}
