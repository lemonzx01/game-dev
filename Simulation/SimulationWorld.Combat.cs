namespace FantasyWorld.Simulation;

/// <summary>Combat tuning (T019). Damage depends on skill, body condition, numbers and the target's toughness, not on level alone.</summary>
public sealed record CombatConfig(long UnarmedDamage = 150_000, int AllyBonusPercent = 15, int MaxAllies = 3, int AllyRadius = 2,
    long FatiguePerAttack = 20_000, int FightBackBravery = 600, int FightBackAllies = 2, int FightBackDistance = 2)
{
    public static CombatConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (long v in new[] { UnarmedDamage, AllyBonusPercent, MaxAllies, FatiguePerAttack, FightBackAllies })
            ArgumentOutOfRangeException.ThrowIfNegative(v);
        foreach (int v in new[] { AllyRadius, FightBackDistance }) ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FatiguePerAttack, MindConfig.MaxFatigue);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (long v in new[] { UnarmedDamage, AllyBonusPercent, MaxAllies, AllyRadius, FatiguePerAttack, FightBackBravery, FightBackAllies, FightBackDistance })
            w.Write(v);
    }
}

/// <summary>Spells known to sapient casters. Values are part of events (Reason) and the hash schema.</summary>
public enum Spell { Firebolt = 1, Heal = 2 }

/// <summary>Magic tuning (T019): mana is an elapsed-time anchor; casting needs study (Scholarship) and costs mana.</summary>
public sealed record MagicConfig(int MinScholarshipLevel = 1, long BaseMana = 100, int ManaRegenTicks = 20,
    long FireboltCost = 40, int FireboltRange = 4, long FireboltDamage = 300_000, int CastIntervalTicks = 8,
    long HealCost = 30, int HealRange = 3, long HealAmount = 250_000, long HealBelowHealth = 700_000)
{
    public static MagicConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (long v in new[] { MinScholarshipLevel, BaseMana, ManaRegenTicks, FireboltCost, FireboltRange, FireboltDamage, CastIntervalTicks, HealCost, HealRange, HealAmount })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BaseMana, long.MaxValue - 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(HealBelowHealth, VitalsConfig.MaxHealth);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (long v in new[] { MinScholarshipLevel, BaseMana, ManaRegenTicks, FireboltCost, FireboltRange, FireboltDamage, CastIntervalTicks, HealCost, HealRange, HealAmount, HealBelowHealth })
            w.Write(v);
    }
}

/// <summary>Read model for T019 per-entity state.</summary>
public sealed record AdventureView(EntityId Id, long Mana, long MaxMana, int Aptitude, bool KnowsMagic, int TamedTo, long TamerId, int Trust);

/// <summary>
/// T019 combat and magic. Every attack is validated (alive, adjacent, not an ally or the town's own tamed beast) and costs
/// fatigue; damage scales with Combat skill, the attacker's health, allies fighting the same foe, and the target's toughness.
/// Fierce species (dragons) retaliate against whoever hurts them. Cornered or brave villagers with friends nearby fight back
/// instead of fleeing. Casters who have studied (Scholarship) spend mana on Firebolt (ranged) or Heal (wounded ally).
/// </summary>
public sealed partial class SimulationWorld
{
    private CombatConfig CombatCfg => options.Combat;
    private MagicConfig MagicCfg => options.Magic;

    private static bool IsSapient(SpeciesDef? d) => d is not null && d.Faction != Faction.None;

    // ---- Validity ------------------------------------------------------------------------------

    /// <summary>True if <paramref name="a"/> may harm <paramref name="b"/>: both alive, different, not townmates, and not the town's own beast.</summary>
    private static bool CanHarm(EntityState a, EntityState b)
    {
        if (a.Id == b.Id || a.LifeState != LifeStates.Alive || b.LifeState != LifeStates.Alive) return false;
        int townA = a.SettlementId != 0 ? a.SettlementId : a.TamedTo, townB = b.SettlementId != 0 ? b.SettlementId : b.TamedTo;
        return townA == 0 || townA != townB;
    }

    /// <summary>A living creature is a threat to e when e's species fears it, unless it is a beast tamed by e's town.</summary>
    private bool ThreatTo(EntityState e, EntityState other) =>
        Fears(e, other.Species) && !(other.TamedTo != 0 && other.TamedTo == (e.SettlementId != 0 ? e.SettlementId : e.TamedTo));

    // ---- Damage --------------------------------------------------------------------------------

    /// <summary>Base damage: guards fight armed, other sapients unarmed, animals with their species' attack.</summary>
    private long AttackPower(EntityState attacker)
    {
        if (attacker.Job == Job.Guard) return TradeCfg.GuardDamage;
        var d = SpeciesOf(attacker);
        if (d is { AttackDamage: > 0 }) return d.AttackDamage;
        return IsSapient(d) ? CombatCfg.UnarmedDamage : 0;
    }

    private static Int128 ScalePercent(Int128 value, Int128 percent)
    {
        if (value <= 0 || percent <= 0) return 0;
        if (value <= Int128.MaxValue / percent) return value * percent / 100;
        var exact = (System.Numerics.BigInteger)value * (System.Numerics.BigInteger)percent / 100;
        var cap = (System.Numerics.BigInteger)long.MaxValue * 1000;
        return exact >= cap ? (Int128)long.MaxValue * 1000 : (Int128)exact;
    }

    private long Damage(EntityState attacker, EntityState target, long basePower, long now)
    {
        Int128 dmg = basePower;
        dmg = ScalePercent(dmg, 100 + (Int128)10 * Level(attacker, Skill.Combat));                       // experience
        dmg = ScalePercent(dmg, 50 + (Int128)50 * attacker.Vitals.At(now, options.Vitals).Health / VitalsConfig.MaxHealth); // wounded fight weaker
        dmg = ScalePercent(dmg, 100 + (Int128)CombatCfg.AllyBonusPercent * AlliesFighting(attacker, target)); // numbers
        dmg = ScalePercent(dmg, SpeciesOf(target)?.DamageTakenPercent ?? 100);                           // toughness (dragon hide)
        return dmg >= long.MaxValue ? long.MaxValue : (long)dmg;
    }

    private struct AlliesVisitor : IGridVisitor
    {
        public SimulationWorld World;
        public EntityState Attacker, Target;
        public int Count;

        public bool Visit(long id)
        {
            World.candidateVisitsThisTick++;
            if (id == Attacker.Id.Value || id == Target.Id.Value) return true;
            var o = World.entities[World.FindLive(id)];
            if (o.LifeState != LifeStates.Alive || o.Mind.TargetEntity != Target.Id.Value || o.Mind.Kind is not (ActionKind.Hunt or ActionKind.Defend)) return true;
            bool ally = Attacker.SettlementId != 0 ? o.SettlementId == Attacker.SettlementId : o.SettlementId == 0 && o.Species == Attacker.Species;
            if (ally && Math.Max(Math.Abs(o.X - Attacker.X), Math.Abs(o.Y - Attacker.Y)) <= World.CombatCfg.AllyRadius)
            {
                if (++Count >= World.CombatCfg.MaxAllies) return false;
            }
            return true;
        }
    }

    // Allies within AllyRadius engaged against the same target (townmates, or the same wild species), capped & short-circuited.
    private int AlliesFighting(EntityState attacker, EntityState target)
    {
        if (CombatCfg.MaxAllies <= 0) return 0;
        int maxVisits = QuerySliceBudgetNow();
        if (maxVisits <= 0) return 0;
        int dummyBucket = 0;
        long dummyId = 0;
        var visitor = new AlliesVisitor { World = this, Attacker = attacker, Target = target };
        grid.QueryRange(attacker.X, attacker.Y, CombatCfg.AllyRadius, 0, 0, maxVisits, ref dummyBucket, ref dummyId, ref visitor, out _);
        return Math.Min(visitor.Count, CombatCfg.MaxAllies);
    }

    private void AddFatigue(EntityState e, long amount, long now)
    {
        e.Mind.Fatigue = Math.Min(MindConfig.MaxFatigue, FatigueAt(e.Mind, now) + amount);
        e.Mind.FatigueTick = now;
    }

    private void Attack(EntityState attacker, EntityState prey, long now)
    {
        if (!CanHarm(attacker, prey) || Math.Abs(prey.X - attacker.X) + Math.Abs(prey.Y - attacker.Y) > 1) return; // invalid target: no cost
        long power = AttackPower(attacker);
        if (power <= 0) return;
        AddFatigue(attacker, CombatCfg.FatiguePerAttack, now);
        GainXp(attacker, Skill.Combat, SkillCfg.XpPerHit, now);
        Hurt(attacker, prey, Damage(attacker, prey, power, now), now);
    }

    // Applies damage; kills are Combat when the attacker fights (Defend) or is a person, Predation when an animal hunts.
    private void Hurt(EntityState attacker, EntityState prey, long damage, long now)
    {
        var v = prey.Vitals.At(now, options.Vitals);
        long health = Math.Max(0, v.Health - Math.Max(1, damage));
        Emit(new WorldEvent(0, now, EventKinds.Attacked, 0, attacker.Id.Value, prey.Id.Value, prey.X, prey.Y, 0, v.Health - health));
        if (health == 0)
        {
            bool fight = attacker.Mind.Kind == ActionKind.Defend || IsSapient(SpeciesOf(attacker));
            Die(prey, now, fight ? DeathCause.Combat : DeathCause.Predation, attacker.Id.Value);
            attacker.Mind.NoFoodUntil = 0; // the killer knows where fresh meat is
            OnNoise(prey.X, prey.Y, now); // FIX-R3 #2: the fight is heard whether or not its event fits the log
            return;
        }
        prey.Vitals = new VitalsAnchor(now, v.Hunger, v.Thirst, health);
        ScheduleDeath(prey); // the old booking becomes stale
        OnNoise(prey.X, prey.Y, now); // FIX-R3 #2 (also wakes the victim if it was asleep)
        perceptionVersion++;
        OnAttacked(prey, attacker, now);
        if (SpeciesOf(prey) is { Retaliates: true } && prey.Mind.Kind != ActionKind.Defend && CanHarm(prey, attacker))
            StartFight(prey, attacker, now);
    }

    /// <summary>Starts a Defend action against a specific foe (retaliation); the usual think cadence resumes afterwards.</summary>
    private void StartFight(EntityState e, EntityState foe, long now)
    {
        FinishReading(e, now);
        SetResting(e, false, now);
        e.Mind.Kind = ActionKind.Defend;
        e.Action = (int)ActionKind.Defend;
        e.Mind.Path = null;
        e.Mind.PathIndex = 0;
        e.Mind.TargetEntity = foe.Id.Value;
        e.Mind.HuntUntil = now + options.Ecology.HuntGiveUpTicks;
        e.Mind.AttackReadyTick = now + 1;
        e.Mind.FleeStepsDone = 0;
        e.Mind.Replans = 0;
        e.Mind.AwaitingPath = false;
        e.Mind.CommitUntil = now + MindCfg.CommitmentTicks;
        Emit(new WorldEvent(0, now, EventKinds.ActionStarted, 0, e.Id.Value, foe.Id.Value, e.X, e.Y, (int)ActionKind.Defend));
        ScheduleStep(e, now + 1);
    }

    // ---- Fighting back -------------------------------------------------------------------------

    private struct FriendsVisitor : IGridVisitor
    {
        public SimulationWorld World;
        public EntityState Actor;
        public int Friends;

        public bool Visit(long id)
        {
            World.candidateVisitsThisTick++;
            if (id == Actor.Id.Value) return true;
            var o = World.entities[World.FindLive(id)];
            if (o.LifeState == LifeStates.Alive && Actor.SettlementId != 0 && o.SettlementId == Actor.SettlementId
                && Math.Max(Math.Abs(o.X - Actor.X), Math.Abs(o.Y - Actor.Y)) <= World.CombatCfg.AllyRadius)
            {
                if (++Friends >= World.CombatCfg.FightBackAllies) return false;
            }
            return true;
        }
    }

    /// <summary>A non-guard adult person fights a close threat when cornered, or when brave with enough friends nearby.</summary>
    private bool FightsBack(EntityState e, EntityState? threat, int distance, long now)
    {
        if (threat is null || e.Job == Job.Guard || !IsSapient(SpeciesOf(e)) || StageOf(e, now) == LifeStage.Juvenile) return false;
        if (distance > CombatCfg.FightBackDistance || !CanHarm(e, threat)) return false;
        if (FleeStep(e, threat) < 0) return true; // cornered
        if (e.Psyche.Bravery < CombatCfg.FightBackBravery) return false;
        if (CombatCfg.FightBackAllies <= 0) return true;
        int maxVisits = QuerySliceBudgetNow();
        if (maxVisits <= 0) return false;
        int dummyBucket = 0;
        long dummyId = 0;
        var visitor = new FriendsVisitor { World = this, Actor = e };
        grid.QueryRange(e.X, e.Y, CombatCfg.AllyRadius, 0, 0, maxVisits, ref dummyBucket, ref dummyId, ref visitor, out _);
        return visitor.Friends >= CombatCfg.FightBackAllies;
    }

    // ---- Magic ---------------------------------------------------------------------------------

    /// <summary>Stable 0–100 talent per entity, derived from the world seed and id (not stored).</summary>
    private int Aptitude(EntityState e)
    {
        unchecked
        {
            ulong z = (ulong)options.Seed * 0x9E3779B97F4A7C15UL ^ (ulong)e.Id.Value * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 31)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 29)) % 101);
        }
    }

    private long MaxMana(EntityState e) => MagicCfg.BaseMana + Aptitude(e);

    private bool KnowsMagic(EntityState e) =>
        IsSapient(SpeciesOf(e)) && e.LifeState == LifeStates.Alive && Level(e, Skill.Scholarship) >= MagicCfg.MinScholarshipLevel;

    private long ManaAt(EntityState e, long now)
    {
        long max = MaxMana(e);
        long gain = (now - e.ManaTick) / MagicCfg.ManaRegenTicks;
        long room = max - e.Mana;
        return room <= 0 ? max : e.Mana + Math.Min(room, gain);
    }

    private bool Spend(EntityState e, long cost, long now)
    {
        long mana = ManaAt(e, now);
        if (mana < cost) return false;
        e.Mana = mana - cost;
        e.ManaTick = now;
        return true;
    }

    private bool CanCastFirebolt(EntityState caster, EntityState target, long now) =>
        KnowsMagic(caster) && CanHarm(caster, target)
        && ManaAt(caster, now) >= MagicCfg.FireboltCost
        && Math.Max(Math.Abs(target.X - caster.X), Math.Abs(target.Y - caster.Y)) <= MagicCfg.FireboltRange;

    /// <summary>Firebolt: ranged damage to a valid foe within range. Costs mana only when cast.</summary>
    private bool CastFirebolt(EntityState caster, EntityState target, long now)
    {
        if (!CanCastFirebolt(caster, target, now) || !Spend(caster, MagicCfg.FireboltCost, now)) return false;
        AddFatigue(caster, CombatCfg.FatiguePerAttack, now);
        GainXp(caster, Skill.Combat, SkillCfg.XpPerHit, now);
        long damage = Damage(caster, target, MagicCfg.FireboltDamage, now);
        Emit(new WorldEvent(0, now, EventKinds.SpellCast, 0, caster.Id.Value, target.Id.Value, target.X, target.Y, (int)Spell.Firebolt, MagicCfg.FireboltCost));
        Hurt(caster, target, damage, now);
        return true;
    }

    private bool CanHeal(EntityState caster, EntityState target, long now) =>
        caster.LifeState == LifeStates.Alive && target.LifeState == LifeStates.Alive
        && caster.SettlementId != 0 && target.SettlementId == caster.SettlementId
        && KnowsMagic(caster)
        && Math.Max(Math.Abs(target.X - caster.X), Math.Abs(target.Y - caster.Y)) <= MagicCfg.HealRange
        && target.Vitals.At(now, options.Vitals).Health < MagicCfg.HealBelowHealth;

    /// <summary>Heal a specific eligible townmate. Shared validation, mana, and fatigue costs (reused by T020).</summary>
    private bool CastHeal(EntityState caster, EntityState target, long now)
    {
        if (!CanHeal(caster, target, now) || !Spend(caster, MagicCfg.HealCost, now)) return false;
        AddFatigue(caster, CombatCfg.FatiguePerAttack, now);
        var v = target.Vitals.At(now, options.Vitals);
        long healed = Math.Min(MagicCfg.HealAmount, VitalsConfig.MaxHealth - v.Health);
        target.Vitals = new VitalsAnchor(now, v.Hunger, v.Thirst, v.Health + healed);
        ScheduleDeath(target);
        perceptionVersion++;
        Relate(target, caster.Id.Value, 300, now);
        Emit(new WorldEvent(0, now, EventKinds.SpellCast, 0, caster.Id.Value, target.Id.Value, target.X, target.Y, (int)Spell.Heal, healed));
        return true;
    }

    /// <summary>Metered scan for the most wounded eligible townmate in range (lowest HP, then ascending ID).</summary>
    private bool TryScanHealTarget(EntityState caster, long now, out EntityState? target)
    {
        target = null;
        if (caster.SettlementId == 0 || !KnowsMagic(caster) || ManaAt(caster, now) < MagicCfg.HealCost) return true;
        return RunCachedBestScan(
            caster, ScanPhase.Heal, null, ScanCacheKind.Heal, caster.SettlementId, 0,
            caster.X, caster.Y, caster.X, caster.Y, MagicCfg.HealRange, MagicCfg.HealBelowHealth, now, shareInCache: true,
            out target, out _, out _);
    }

    // ---- State, reads, test hooks --------------------------------------------------------------

    private static void WriteAdventure(BinaryWriter w, EntityState e)
    {
        w.Write(e.Mana);
        w.Write(e.ManaTick);
        w.Write(e.TamedTo);
        w.Write(e.TamerId);
        w.Write(e.Trust);
        w.Write(e.TameAttemptUntil);
    }

    private void ReadAdventureFrom(BinaryReader r, EntityState e)
    {
        e.Mana = r.ReadInt64();
        e.ManaTick = r.ReadInt64();
        e.TamedTo = r.ReadInt32();
        e.TamerId = r.ReadInt64();
        e.Trust = r.ReadInt32();
        e.TameAttemptUntil = r.ReadInt64();
        if (e.Mana < 0 || e.ManaTick < 0 || e.ManaTick > tick || e.Mana > MaxMana(e)
            || e.TamedTo < 0 || e.TamerId < 0 || e.Trust < 0 || e.TameAttemptUntil < 0)
            throw new InvalidDataException("Mana or taming state invalid.");
    }

    public AdventureView? ReadAdventure(WorldId worldId, EntityId entityId)
    {
        CheckAccess();
        if (worldId != this.worldId || FindLive(entityId.Value) is not (int i and >= 0)) return null;
        var e = entities[i];
        return new AdventureView(entityId, ManaAt(e, tick), MaxMana(e), Aptitude(e), KnowsMagic(e), e.TamedTo, e.TamerId, e.Trust);
    }

    internal void SetHealthForTest(long id, long health)
    {
        var e = entities[FindLive(id)];
        var v = e.Vitals.At(tick, options.Vitals);
        e.Vitals = new VitalsAnchor(tick, v.Hunger, v.Thirst, health);
        ScheduleDeath(e);
        perceptionVersion++;
    }

    internal void SetBraveryForTest(long id, int bravery) => entities[FindLive(id)].Psyche.Bravery = bravery;

    internal void SetManaForTest(long id, long mana)
    {
        var e = entities[FindLive(id)];
        e.Mana = mana;
        e.ManaTick = tick;
    }

    internal void SetTamedForTest(long id, int settlementId)
    {
        var e = entities[FindLive(id)];
        e.TamedTo = settlementId;
        e.HomeX = settlements[settlementId - 1].X;
        e.HomeY = settlements[settlementId - 1].Y;
        if (SpeciesOf(e) is { TameTrust: > 0 } sd) e.Trust = sd.TameTrust;
        if (e.TamerId == 0)
            e.TamerId = entities.FirstOrDefault(m => m.LifeState == LifeStates.Alive && m.SettlementId == settlementId)?.Id.Value ?? 0;
        perceptionVersion++;
    }

    internal long ComputeDamageForTest(long attackerId, long targetId, long basePower) =>
        Damage(entities[FindLive(attackerId)], entities[FindLive(targetId)], basePower, tick);

    internal (long Mana, long ManaTick) ManaAnchorForTest(long id)
    {
        var e = entities[FindLive(id)];
        return (e.Mana, e.ManaTick);
    }

    internal bool TryCastFireboltForTest(long casterId, long targetId)
    {
        int ci = FindLive(casterId), ti = FindLive(targetId);
        return ci >= 0 && ti >= 0 && CastFirebolt(entities[ci], entities[ti], tick);
    }

    internal bool TryCastHealForTest(long casterId, long targetId)
    {
        int ci = FindLive(casterId), ti = FindLive(targetId);
        return ci >= 0 && ti >= 0 && CastHeal(entities[ci], entities[ti], tick);
    }
}
