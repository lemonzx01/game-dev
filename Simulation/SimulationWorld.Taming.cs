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

    private bool IsEligibleFeeder(EntityState feeder, long now, out Settlement? town)
    {
        town = null;
        if (feeder.LifeState != LifeStates.Alive || !IsSapient(SpeciesOf(feeder)) || feeder.Job == Job.Guard
            || feeder.OnTrip || StageOf(feeder, now) == LifeStage.Juvenile) return false;
        town = SettlementOf(feeder);
        return town is not null && town.Food > TameCfg.FoodReserve;
    }

    private bool HasActiveAttemptClaim(EntityState animal, EntityState self, long now)
    {
        if (animal.TamerId == 0 || animal.TamerId == self.Id.Value || now >= animal.TameAttemptUntil) return false;
        if (FindLive(animal.TamerId) is not (int ti and >= 0)) return false;
        var current = entities[ti];
        return current.LifeState == LifeStates.Alive && current.Job != Job.Guard && !current.OnTrip;
    }

    private bool CanFeedAnimal(EntityState feeder, EntityState animal, long now) =>
        IsEligibleFeeder(feeder, now, out _) && animal.LifeState == LifeStates.Alive && animal.TamedTo == 0
        && animal.SettlementId == 0 && SpeciesOf(animal) is { TameTrust: > 0 } && !HasActiveAttemptClaim(animal, feeder, now);

    private bool WantsToTame(EntityState e, VitalsView v, long now) =>
        IsEligibleFeeder(e, now, out _) && v.Hunger < MindCfg.EatStartNeed && v.Thirst < MindCfg.DrinkStartNeed
        && TameCandidate(e, now) is not null;

    /// <summary>Nearest untamed animal of a tameable species within perception without an active competing claim (Manhattan, then id).</summary>
    private EntityState? TameCandidate(EntityState e, long now)
    {
        EntityState? best = null;
        int bestDistance = int.MaxValue, r = MindCfg.PerceptionRadius;
        grid.Query(e.X, e.Y, r, id =>
        {
            var o = entities[FindLive(id)];
            if (o.LifeState != LifeStates.Alive || o.TamedTo != 0 || SpeciesOf(o) is not { TameTrust: > 0 } || o.SettlementId != 0) return;
            if (HasActiveAttemptClaim(o, e, now)) return;
            if (Math.Max(Math.Abs(o.X - e.X), Math.Abs(o.Y - e.Y)) > r) return;
            int d = Math.Abs(o.X - e.X) + Math.Abs(o.Y - e.Y);
            if (d < bestDistance || (d == bestDistance && o.Id.Value < best!.Id.Value)) { best = o; bestDistance = d; }
        });
        return best;
    }

    private void TameStep(EntityState e, long now)
    {
        var animal = FindLive(e.Mind.TargetEntity) is int ai and >= 0 ? entities[ai] : null;
        if (animal is null || now >= e.Mind.HuntUntil || !CanFeedAnimal(e, animal, now))
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

    private bool Feed(EntityState feeder, EntityState animal, long now)
    {
        if (!CanFeedAnimal(feeder, animal, now) || SettlementOf(feeder) is not { } town) return false;
        var before = BuyerSignature(town);
        town.Food--;
        InvalidateSettlementEconomy(town, before);
        var sd = SpeciesOf(animal)!;
        if (animal.TamerId != feeder.Id.Value) { animal.TamerId = feeder.Id.Value; animal.Trust = 0; } // trust is personal
        animal.TameAttemptUntil = now + TameCfg.CooldownTicks + TameCfg.GiveUpTicks;
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
        if (animal.Trust < sd.TameTrust) return true;
        animal.TamedTo = town.Id;
        animal.TameAttemptUntil = 0; // completed bond outlives the feeder without holding an attempt lock
        animal.HomeX = town.X;
        animal.HomeY = town.Y;
        long sequence = Emit(new WorldEvent(0, now, EventKinds.AnimalTamed, 0, feeder.Id.Value, animal.Id.Value, animal.X, animal.Y, town.Id, animal.Species), critical: true);
        milestones.Add(new WorldEvent(sequence, now, EventKinds.AnimalTamed, 0, feeder.Id.Value, animal.Id.Value, animal.X, animal.Y, town.Id, animal.Species));
        return true;
    }

    private Settlement? TamedHome(EntityState e) => e.TamedTo > 0 && e.TamedTo <= settlements.Count ? settlements[e.TamedTo - 1] : null;

    internal void SetTrustForTest(long id, long tamer, int trust)
    {
        var e = entities[FindLive(id)];
        e.TamerId = tamer;
        e.Trust = trust;
    }

    internal ulong PsycheRngStateForTest => streams[PsycheStream].State;
}
