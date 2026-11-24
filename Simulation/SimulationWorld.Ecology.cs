namespace FantasyWorld.Simulation;

/// <summary>Meat left by a dead body. One per tile (a second body on the tile adds its meat). Token guards the decay booking.</summary>
internal sealed class Carcass
{
    public int Cell;
    public int Meat;
    public long DecayTick;
    public long Token;
}

/// <summary>
/// T009 ecology: species lookup, carcasses as a real meat resource, attacks/kills, hunting and farm-animal leashing.
/// Meat is conserved: every unit comes from a body (MeatYield) and leaves only by being eaten or by explicit decay.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly SortedDictionary<int, Carcass> carcasses = new(); // by cell; sorted iteration only for hashing
    private long nextCarcassToken = 1;

    private SpeciesDef? SpeciesOf(EntityState e) => options.Species.TryGetValue(e.Species, out var d) ? d : null;

    private Diet DietOf(EntityState e) => SpeciesOf(e)?.Diet ?? Diet.Herbivore;

    private int MoveTicks(EntityState e) => SpeciesOf(e)?.MoveTicksPerTile ?? MindCfg.MoveTicksPerTile;

    private bool Fears(EntityState e, int otherSpecies) =>
        (SpeciesOf(e)?.Fears.Contains(otherSpecies) ?? false)
        || (MindCfg.FearOf.TryGetValue(e.Species, out var extra) && extra.Contains(otherSpecies));

    private bool HasFears(EntityState e) =>
        (SpeciesOf(e)?.Fears.Length ?? 0) > 0 || (MindCfg.FearOf.TryGetValue(e.Species, out var extra) && !extra.IsEmpty);

    // ---- Carcasses -----------------------------------------------------------------------------

    private void AddCarcass(int cell, int meat, long now)
    {
        if (meat <= 0) return;
        if (!carcasses.TryGetValue(cell, out var c))
            carcasses.Add(cell, c = new Carcass { Cell = cell });
        c.Meat = checked(c.Meat + meat);
        c.DecayTick = now + options.Ecology.CarcassDecayTicks;
        c.Token = nextCarcassToken++;
        scheduler.Schedule(c.DecayTick, 10, 0, new WorkItem(WorkKind.CarcassDecay, cell, c.Token), isRequired: true);
    }

    private void RunDecay(WorkItem item, long now)
    {
        if (!carcasses.TryGetValue((int)item.Subject, out var c) || c.Token != item.Token) return; // eaten or refreshed
        carcasses.Remove(c.Cell);
        // Explicit sink: the remaining meat leaves the world here.
        Emit(new WorldEvent(0, now, EventKinds.CarcassDecayed, 0, 0, 0, c.Cell % options.MapWidth, c.Cell / options.MapWidth, 0, c.Meat));
    }

    private int MeatAt(int cell) => carcasses.TryGetValue(cell, out var c) ? c.Meat : 0;

    /// <summary>Takes up to <paramref name="units"/> meat from the carcass on <paramref name="cell"/>; returns units taken.</summary>
    private int TakeMeat(int cell, int units)
    {
        if (!carcasses.TryGetValue(cell, out var c)) return 0;
        int taken = Math.Min(units, c.Meat);
        c.Meat -= taken;
        if (c.Meat == 0) carcasses.Remove(cell); // the pending decay booking becomes stale
        return taken;
    }

    private CommandReason TryEatMeat(EntityState e, long now)
    {
        if (DietOf(e) == Diet.Herbivore) return CommandReason.WrongDiet;
        int cell = -1;
        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (0, 0), (1, 0), (0, 1) }) // ascending cell index
        {
            int x = e.X + dx, y = e.Y + dy;
            if (InBounds(x, y) && MeatAt(Cell(x, y)) > 0) { cell = Cell(x, y); break; }
        }
        if (cell < 0) return CommandReason.NoResource;
        var c = options.Vitals;
        var v = e.Vitals.At(now, c);
        if (v.Hunger == 0) return CommandReason.NotNeeded;
        int want = (int)Math.Min((v.Hunger + c.FoodPerUnit - 1) / c.FoodPerUnit, c.MaxUnitsPerAct);
        int units = TakeMeat(cell, want);
        e.Vitals = new VitalsAnchor(now, Math.Max(0, v.Hunger - units * c.FoodPerUnit), v.Thirst, v.Health);
        ScheduleDeath(e);
        Emit(new WorldEvent(0, now, EventKinds.EntityAte, 0, e.Id.Value, 0, cell % options.MapWidth, cell / options.MapWidth, (int)ResourceKind.Meat, units));
        return CommandReason.None;
    }

    // ---- Attacks and kills ---------------------------------------------------------------------

    private void Attack(EntityState attacker, EntityState prey, long now)
    {
        long damage = SpeciesOf(attacker)?.AttackDamage ?? 0;
        if (damage <= 0 || prey.LifeState != LifeStates.Alive) return;
        var v = prey.Vitals.At(now, options.Vitals);
        long health = Math.Max(0, v.Health - damage);
        Emit(new WorldEvent(0, now, EventKinds.Attacked, 0, attacker.Id.Value, prey.Id.Value, prey.X, prey.Y, 0, v.Health - health));
        if (health == 0)
        {
            Die(prey, now, DeathCause.Predation, attacker.Id.Value);
            attacker.Mind.NoFoodUntil = 0; // the killer knows where fresh meat is
            return;
        }
        prey.Vitals = new VitalsAnchor(now, v.Hunger, v.Thirst, health);
        ScheduleDeath(prey); // the old booking becomes stale
    }

    // ---- Hunting -------------------------------------------------------------------------------

    /// <summary>Nearest living huntable prey within perception (Manhattan, then lower ID).</summary>
    private EntityState? NearestPrey(EntityState e)
    {
        var hunts = SpeciesOf(e)?.Hunts ?? [];
        if (hunts.IsEmpty) return null;
        EntityState? best = null;
        int bestDistance = int.MaxValue, r = MindCfg.PerceptionRadius;
        grid.Query(e.X, e.Y, r, id =>
        {
            if (id == e.Id.Value) return;
            var other = entities[FindLive(id)];
            if (other.LifeState != LifeStates.Alive || !hunts.Contains(other.Species)) return;
            if (Math.Max(Math.Abs(other.X - e.X), Math.Abs(other.Y - e.Y)) > r) return;
            int d = Math.Abs(other.X - e.X) + Math.Abs(other.Y - e.Y);
            if (d < bestDistance || (d == bestDistance && other.Id.Value < best!.Id.Value)) { best = other; bestDistance = d; }
        });
        return best;
    }

    private void HuntStep(EntityState e, long now)
    {
        var prey = FindLive(e.Mind.TargetEntity) is int pi and >= 0 ? entities[pi] : null;
        if (prey is null || prey.LifeState != LifeStates.Alive)
        {
            FinishAction(e, now); // prey killed or lost; the next think looks for the carcass
            return;
        }
        if (now >= e.Mind.HuntUntil)
        {
            e.Mind.NoHuntUntil = now + options.Ecology.HuntCooldownTicks;
            FinishAction(e, now);
            return;
        }
        int distance = Math.Abs(prey.X - e.X) + Math.Abs(prey.Y - e.Y);
        if (distance <= 1)
        {
            if (now >= e.Mind.AttackReadyTick)
            {
                Attack(e, prey, now);
                e.Mind.AttackReadyTick = now + options.Ecology.AttackIntervalTicks;
            }
            ScheduleStep(e, Math.Max(now + 1, e.Mind.AttackReadyTick));
            return;
        }
        int step = ChaseStep(e, prey);
        if (step >= 0) MoveTo(e, step);
        ScheduleStep(e, now + MoveTicks(e));
    }

    // Neighbour that minimises Manhattan distance to the prey (ties: N, W, E, S); -1 if none improves.
    private int ChaseStep(EntityState e, EntityState prey)
    {
        int best = -1, bestDistance = Math.Abs(e.X - prey.X) + Math.Abs(e.Y - prey.Y);
        foreach (var (dx, dy) in Neighbours)
        {
            int x = e.X + dx, y = e.Y + dy;
            if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)])) continue;
            int d = Math.Abs(x - prey.X) + Math.Abs(y - prey.Y);
            if (d < bestDistance) { best = Cell(x, y); bestDistance = d; }
        }
        return best;
    }

    // ---- Farm animals --------------------------------------------------------------------------

    /// <summary>True if moving to (x, y) keeps a domestic animal inside its leash, or brings it closer to home.</summary>
    private bool WithinLeash(EntityState e, int x, int y)
    {
        var sd = SpeciesOf(e);
        if (sd is null || !sd.Domestic) return true;
        int now = Math.Max(Math.Abs(e.X - e.HomeX), Math.Abs(e.Y - e.HomeY));
        int next = Math.Max(Math.Abs(x - e.HomeX), Math.Abs(y - e.HomeY));
        return next <= sd.LeashRadius || next < now;
    }

    private void WriteEcology(BinaryWriter w)
    {
        w.Write(nextCarcassToken);
        w.Write((uint)carcasses.Count);
        foreach (var (_, c) in carcasses)
        {
            w.Write(c.Cell);
            w.Write(c.Meat);
            w.Write(c.DecayTick);
            w.Write(c.Token);
        }
    }

    // ---- Reads and test hooks ------------------------------------------------------------------

    /// <summary>Meat units in the carcass on a tile (0 if none).</summary>
    public int ReadCarcassMeat(int x, int y)
    {
        CheckAccess();
        return InBounds(x, y) ? MeatAt(Cell(x, y)) : 0;
    }

    internal long TotalCarcassMeat => carcasses.Values.Sum(c => (long)c.Meat);
    internal (int X, int Y) PositionOf(long id) => entities[FindLive(id)] is var e ? (e.X, e.Y) : (-1, -1);
    internal (int X, int Y) HomeOf(long id) => entities[FindLive(id)] is var e ? (e.HomeX, e.HomeY) : (-1, -1);
}
