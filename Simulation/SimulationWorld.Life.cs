namespace FantasyWorld.Simulation;

public enum Sex { Unspecified = 0, Female = 1, Male = 2 }
public enum LifeStage { None = 0, Juvenile = 1, Adult = 2, Elder = 3 }

/// <summary>
/// T011 life cycle: sex, age and life stage derived from BirthTick, old-age death booked once, and reproduction:
/// an eligible adult female approaches an adult male of her species, conceives, and gives birth after gestation.
/// Births are capped (local same-species density, global MaxEntities) and paid for by the mother's food reserves
/// (hunger rises by BirthHungerCost per child), so no offspring appears for free.
/// </summary>
public sealed partial class SimulationWorld
{
    public const uint EcologyStream = 3;
    private readonly List<WorldEvent> milestones = new(); // durable births and deaths, ascending EventSequence

    private static bool Breeds(SpeciesDef? sd) => sd is { LitterSize: > 0 };

    private LifeStage StageOf(EntityState e, long now)
    {
        var sd = SpeciesOf(e);
        if (sd is null || sd.LifespanTicks == 0) return LifeStage.None;
        long age = now - e.BirthTick;
        if (age < sd.MaturityTicks) return LifeStage.Juvenile;
        return age >= e.LifespanTicks * 4 / 5 ? LifeStage.Elder : LifeStage.Adult;
    }

    private void InitLife(EntityState e, long now, Sex sex)
    {
        var rng = streams[EcologyStream];
        e.Sex = sex != Sex.Unspecified ? sex : (rng.Draw() & 1) == 0 ? Sex.Female : Sex.Male;
        var sd = SpeciesOf(e);
        if (sd is null || sd.LifespanTicks == 0) return;
        int jitter = options.Ecology.LifespanJitterPercent;
        long percent = 100 + (long)(rng.Draw() % (ulong)(2 * jitter + 1)) - jitter;
        e.LifespanTicks = Math.Max(sd.MaturityTicks + 1, sd.LifespanTicks * percent / 100);
        long oldAge = e.BirthTick + e.LifespanTicks;
        scheduler.Schedule(Math.Max(oldAge, now + 1), 0, e.Id.Value, new WorkItem(WorkKind.OldAge, e.Id.Value), isRequired: true);
    }

    private void RunOldAge(WorkItem item, long now)
    {
        int i = FindLive(item.Subject);
        if (i < 0 || entities[i].LifeState != LifeStates.Alive) return;
        Die(entities[i], now, DeathCause.OldAge, 0);
    }

    // ---- Mating --------------------------------------------------------------------------------

    /// <summary>The male to approach if this entity may conceive now, else null. All checks are bounded queries.</summary>
    private EntityState? MateFor(EntityState e, long now)
    {
        var sd = SpeciesOf(e);
        var eco = options.Ecology;
        if (!Breeds(sd) || e.Sex != Sex.Female || e.PregnantUntil != 0 || now < e.ReproReadyTick) return null;
        if (StageOf(e, now) != LifeStage.Adult) return null;
        var v = e.Vitals.At(now, options.Vitals);
        if (v.Hunger > eco.ReproMaxNeed || v.Thirst > eco.ReproMaxNeed || v.Health < eco.ReproMinHealth) return null;
        if (entities.Count >= eco.MaxEntities) return null;
        EntityState? best = null;
        int bestDistance = int.MaxValue, sameSpecies = 0, r = MindCfg.PerceptionRadius;
        grid.Query(e.X, e.Y, r, id =>
        {
            var other = entities[FindLive(id)];
            if (other.LifeState != LifeStates.Alive || other.Species != e.Species) return;
            if (Math.Max(Math.Abs(other.X - e.X), Math.Abs(other.Y - e.Y)) > r) return;
            sameSpecies++;
            if (other.Sex != Sex.Male || StageOf(other, now) == LifeStage.Juvenile) return;
            int d = Math.Abs(other.X - e.X) + Math.Abs(other.Y - e.Y);
            if (d < bestDistance || (d == bestDistance && other.Id.Value < best!.Id.Value)) { best = other; bestDistance = d; }
        });
        return sameSpecies > sd!.LocalDensityCap ? null : best; // crowded: no conception
    }

    private void MateStep(EntityState e, long now)
    {
        var male = FindLive(e.Mind.TargetEntity) is int mi and >= 0 ? entities[mi] : null;
        if (male is null || male.LifeState != LifeStates.Alive || now >= e.Mind.HuntUntil)
        {
            FinishAction(e, now);
            return;
        }
        if (Math.Abs(male.X - e.X) + Math.Abs(male.Y - e.Y) <= 1)
        {
            Conceive(e, male, now);
            FinishAction(e, now);
            return;
        }
        int step = ChaseStep(e, male);
        if (step >= 0) MoveTo(e, step);
        ScheduleStep(e, now + MoveTicks(e));
    }

    private void Conceive(EntityState mother, EntityState father, long now)
    {
        var sd = SpeciesOf(mother)!;
        mother.PregnantUntil = now + sd.GestationTicks;
        mother.PregnancyFather = father.Id.Value;
        mother.ReproReadyTick = mother.PregnantUntil + sd.ReproCooldownTicks;
        scheduler.Schedule(mother.PregnantUntil, 0, mother.Id.Value, new WorkItem(WorkKind.Birth, mother.Id.Value, mother.PregnantUntil), isRequired: true);
        Emit(new WorldEvent(0, now, EventKinds.Conceived, 0, mother.Id.Value, father.Id.Value, mother.X, mother.Y));
    }

    // ---- Birth ---------------------------------------------------------------------------------

    private void RunBirth(WorkItem item, long now)
    {
        int i = FindLive(item.Subject);
        if (i < 0) return;
        var mother = entities[i];
        if (mother.LifeState != LifeStates.Alive || mother.PregnantUntil != item.Token) return; // mother died: pregnancy lost
        mother.PregnantUntil = 0;
        var sd = SpeciesOf(mother)!;
        var c = options.Vitals;
        var v = mother.Vitals.At(now, c);
        // Resource check: each child costs the mother BirthHungerCost; she bears only what she can afford,
        // and never more than the global entity cap allows.
        long affordable = sd.BirthHungerCost == 0 ? sd.LitterSize : (VitalsConfig.MaxNeed - v.Hunger) / sd.BirthHungerCost;
        int children = (int)Math.Min(Math.Min(sd.LitterSize, affordable), Math.Max(0, options.Ecology.MaxEntities - entities.Count));
        if (children <= 0)
        {
            Emit(new WorldEvent(0, now, EventKinds.BirthFailed, 0, mother.Id.Value, mother.PregnancyFather, mother.X, mother.Y));
            return;
        }
        mother.Vitals = new VitalsAnchor(now, v.Hunger + sd.BirthHungerCost * children, v.Thirst, v.Health);
        ScheduleDeath(mother);
        for (int k = 0; k < children; k++)
        {
            long child = SpawnEntity(mother.X, mother.Y, mother.Species, now, Sex.Unspecified, mother.Id.Value, mother.PregnancyFather, born: true);
            long sequence = Emit(new WorldEvent(0, now, EventKinds.EntityBorn, 0, mother.Id.Value, child, mother.X, mother.Y, 0, mother.PregnancyFather), critical: true);
            milestones.Add(new WorldEvent(sequence, now, EventKinds.EntityBorn, 0, mother.Id.Value, child, mother.X, mother.Y, 0, mother.PregnancyFather));
        }
    }

    private void WriteLife(BinaryWriter w)
    {
        w.Write((uint)milestones.Count);
        foreach (var m in milestones)
        {
            w.Write(m.EventSequence);
            w.Write(m.Tick);
            w.Write(m.Kind);
            w.Write(m.Actor);
            w.Write(m.Target);
            w.Write(m.X);
            w.Write(m.Y);
            w.Write(m.Reason);
            w.Write(m.Amount);
        }
    }

    // ---- Test hooks ----------------------------------------------------------------------------
    internal long PregnantUntilOf(long id) => entities[FindLive(id)].PregnantUntil;
    internal LifeStage StageOfForTest(long id) => StageOf(entities[FindLive(id)], tick);
}
