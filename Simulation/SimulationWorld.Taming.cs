namespace FantasyWorld.Simulation;

/// <summary>Taming tuning (T019). Feeding costs town food; the animal may refuse; trust builds per accepted feed.</summary>
public sealed record TamingConfig(int TameScore = 120, long FoodReserve = 40, int TrustPerFeed = 20, int TrustLostOnRefusal = 10,
    int BaseAcceptPercent = 65, int CooldownTicks = 60, int GiveUpTicks = 120)
{
    public static TamingConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (long v in new[] { TameScore, TrustPerFeed, CooldownTicks, GiveUpTicks }) ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        foreach (long v in new[] { FoodReserve, TrustLostOnRefusal }) ArgumentOutOfRangeException.ThrowIfNegative(v);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BaseAcceptPercent, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(BaseAcceptPercent);
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (long v in new[] { TameScore, FoodReserve, TrustPerFeed, TrustLostOnRefusal, BaseAcceptPercent, CooldownTicks, GiveUpTicks }) w.Write(v);
    }
}

/// <summary>
/// T019 taming. An idle, safe adult townsperson walks to a nearby untamed animal of a tameable species and feeds it one unit
/// of town food (only while the town keeps its reserve). The animal accepts or refuses (a deterministic roll that its fear
/// lowers and the feeder's sociability raises): acceptance adds trust toward that feeder, refusal costs trust and scares it.
/// At the species' TameTrust the animal is tamed by the feeder's town: it stays near the town, is not feared by its people,
/// and a tamed hunter no longer takes domestic or tamed animals. The bond is a durable milestone and outlives the feeder.
/// </summary>
public sealed partial class SimulationWorld
{
    private TamingConfig TameCfg => options.Taming;

    private bool WantsToTame(EntityState e, VitalsView v, long now) =>
        e.SettlementId != 0 && e.Job != Job.Guard && !e.OnTrip && v.Hunger < MindCfg.EatStartNeed && v.Thirst < MindCfg.DrinkStartNeed
        && StageOf(e, now) != LifeStage.Juvenile && SettlementOf(e)!.Food > TameCfg.FoodReserve && TameCandidate(e) is not null;

    /// <summary>Nearest untamed animal of a tameable species within perception that nobody else is taming (Manhattan, then id).</summary>
    private EntityState? TameCandidate(EntityState e)
    {
        EntityState? best = null;
        int bestDistance = int.MaxValue, r = MindCfg.PerceptionRadius;
        grid.Query(e.X, e.Y, r, id =>
        {
            var o = entities[FindLive(id)];
            if (o.LifeState != LifeStates.Alive || o.TamedTo != 0 || SpeciesOf(o) is not { TameTrust: > 0 } || o.SettlementId != 0) return;
            if (o.TamerId != 0 && o.TamerId != e.Id.Value && FindLive(o.TamerId) is >= 0 and var t && entities[t].LifeState == LifeStates.Alive) return;
            if (Math.Max(Math.Abs(o.X - e.X), Math.Abs(o.Y - e.Y)) > r) return;
            int d = Math.Abs(o.X - e.X) + Math.Abs(o.Y - e.Y);
            if (d < bestDistance || (d == bestDistance && o.Id.Value < best!.Id.Value)) { best = o; bestDistance = d; }
        });
        return best;
    }

    private void TameStep(EntityState e, long now)
    {
        var animal = FindLive(e.Mind.TargetEntity) is int ai and >= 0 ? entities[ai] : null;
        if (animal is null || animal.LifeState != LifeStates.Alive || animal.TamedTo != 0 || now >= e.Mind.HuntUntil)
        {
            e.Mind.NoWorkUntil = now + TameCfg.CooldownTicks;
            FinishAction(e, now);
            return;
        }
        if (Math.Abs(animal.X - e.X) + Math.Abs(animal.Y - e.Y) > 1)
        {
            int step = ChaseStep(e, animal);
            if (step >= 0) MoveTo(e, step);
            ScheduleStep(e, now + MoveTicks(e));
            return;
        }
        Feed(e, animal, now);
        e.Mind.NoWorkUntil = now + TameCfg.CooldownTicks;
        FinishAction(e, now);
    }

    private void Feed(EntityState feeder, EntityState animal, long now)
    {
        if (SettlementOf(feeder) is not { } town || town.Food <= TameCfg.FoodReserve) return; // nothing to spare: no cost, no feed
        var before = BuyerSignature(town);
        town.Food--;
        InvalidateSettlementEconomy(town, before);
        var sd = SpeciesOf(animal)!;
        if (animal.TamerId != feeder.Id.Value) { animal.TamerId = feeder.Id.Value; animal.Trust = 0; } // trust is personal
        int chance = Math.Clamp(TameCfg.BaseAcceptPercent - FearAt(animal, now) / 20 + feeder.Psyche.Sociability / 50, 5, 95);
        bool accepted = (int)(streams[PsycheStream].Draw() % 100) < chance;
        if (accepted)
        {
            var v = animal.Vitals.At(now, options.Vitals);
            animal.Vitals = new VitalsAnchor(now, Math.Max(0, v.Hunger - options.Vitals.FoodPerUnit), v.Thirst, v.Health);
            ScheduleDeath(animal);
            animal.Trust = Math.Min(sd.TameTrust, animal.Trust + TameCfg.TrustPerFeed);
            Relate(animal, feeder.Id.Value, 200, now);
        }
        else
        {
            animal.Trust = Math.Max(0, animal.Trust - TameCfg.TrustLostOnRefusal);
            RaiseFear(animal, 500, now);
        }
        Emit(new WorldEvent(0, now, EventKinds.TameFed, 0, feeder.Id.Value, animal.Id.Value, animal.X, animal.Y, accepted ? 1 : 0, animal.Trust));
        if (animal.Trust < sd.TameTrust) return;
        animal.TamedTo = town.Id;
        animal.HomeX = town.X;
        animal.HomeY = town.Y;
        long sequence = Emit(new WorldEvent(0, now, EventKinds.AnimalTamed, 0, feeder.Id.Value, animal.Id.Value, animal.X, animal.Y, town.Id, animal.Species), critical: true);
        milestones.Add(new WorldEvent(sequence, now, EventKinds.AnimalTamed, 0, feeder.Id.Value, animal.Id.Value, animal.X, animal.Y, town.Id, animal.Species));
    }

    private Settlement? TamedHome(EntityState e) => e.TamedTo > 0 && e.TamedTo <= settlements.Count ? settlements[e.TamedTo - 1] : null;

    internal void SetTrustForTest(long id, long tamer, int trust)
    {
        var e = entities[FindLive(id)];
        e.TamerId = tamer;
        e.Trust = trust;
    }
}
