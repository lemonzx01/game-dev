namespace FantasyWorld.Simulation;

/// <summary>
/// RP predator viability (Body on). Diagnosis with WorldReport `--trace-wolves`: hunts that start usually succeed, but
/// hungry wolves often start none: with an 8-tile perception and one-tile random wandering they rarely meet prey on a
/// large map, and those that stray into towns are killed by guards. So, with Body on:
/// - a hungry wild predator that sees no prey roams: it travels up to RoamSteps tiles in a steady heading (derived from its
///   id and the time window, no stored state) and looks again at its next decision; any wild animal whose water or food
///   search found nothing within perception roams the same way (wolves were dying of thirst one wander tile at a time);
/// - wild predators do not pick prey inside a town's grounds, and roaming never enters them (wolves avoid people);
/// - a wild predator found inside town grounds does not rest or sleep there: it walks straight out, away from the nearest
///   town centre (resting wolves at the town edge were the main source of wolf combat deaths: guards defend the grounds).
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly (int Dx, int Dy)[] Headings = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    private bool WildPredator(EntityState e) =>
        e.TamedTo == 0 && e.SettlementId == 0 && SpeciesOf(e) is { Hunts.Length: > 0 } d && !IsSapient(d);

    /// <summary>
    /// Wild animal that should search widely (Body on): a hungry predator looking for prey, or any wild animal whose water
    /// or food search just failed (nothing within perception) and that still needs it.
    /// </summary>
    private bool RoamsForNeeds(EntityState e, long now)
    {
        if (!BodyCfg.Enabled || BodyCfg.RoamSteps <= 0 || e.TamedTo != 0 || e.SettlementId != 0 || SpeciesOf(e) is not { } d || IsSapient(d))
            return false;
        var v = e.Vitals.At(now, options.Vitals);
        if (v.Thirst >= MindCfg.DrinkStartNeed && e.Mind.NoWaterUntil > now) return true;
        if (v.Hunger < MindCfg.EatStartNeed) return false;
        return d.Hunts.Length > 0 || e.Mind.NoFoodUntil > now;
    }

    /// <summary>Inside any town's grounds (Chebyshev distance within the settlement radius).</summary>
    private bool InTownGrounds(int x, int y)
    {
        foreach (var s in settlements)
            if (Math.Max(Math.Abs(x - s.X), Math.Abs(y - s.Y)) <= TownCfg.Radius) return true;
        return false;
    }

    private bool AvoidsTowns(EntityState hunter) => BodyCfg.Enabled && BodyCfg.PredatorsAvoidTowns && WildPredator(hunter);

    private int RoamHeading(EntityState e, long now)
    {
        unchecked
        {
            ulong z = (ulong)e.Id.Value * 0x9E3779B97F4A7C15UL ^ (ulong)(now / BodyCfg.RoamHeadingTicks + 1) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 31)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 29)) % (ulong)Headings.Length);
        }
    }

    /// <summary>
    /// Up to RoamSteps 4-neighbour steps along a steady heading (diagonals alternate x and y), skipping impassable tiles and,
    /// for town-shy predators, town grounds. Stops early when both axis moves are blocked. Empty if no first step exists.
    /// </summary>
    private int[] RoamPath(EntityState e, long now)
    {
        var (hx, hy) = Headings[RoamHeading(e, now)];
        return HeadingPath(e, hx, hy, BodyCfg.RoamSteps, AvoidsTowns(e), stopOutsideTowns: false);
    }

    /// <summary>Path straight away from the nearest town centre until just outside its grounds (empty if boxed in).</summary>
    private int[] LeaveTownPath(EntityState e, long now)
    {
        Settlement? nearest = null;
        int best = int.MaxValue;
        foreach (var s in settlements)
        {
            int d = Math.Max(Math.Abs(e.X - s.X), Math.Abs(e.Y - s.Y));
            if (d < best) { best = d; nearest = s; }
        }
        if (nearest is null) return [];
        int hx = Math.Sign(e.X - nearest.X), hy = Math.Sign(e.Y - nearest.Y);
        if (hx == 0 && hy == 0) (hx, hy) = Headings[RoamHeading(e, now)];
        return HeadingPath(e, hx, hy, TownCfg.Radius * 2 + 2, avoidTowns: false, stopOutsideTowns: true);
    }

    private int[] HeadingPath(EntityState e, int hx, int hy, int steps, bool avoidTowns, bool stopOutsideTowns)
    {
        bool avoid = avoidTowns;
        var path = new List<int>(steps);
        int x = e.X, y = e.Y;
        bool Ok(int nx, int ny) => InBounds(nx, ny) && TerrainRules.IsPassable(terrain[Cell(nx, ny)]) && WithinLeash(e, nx, ny)
                                   && !(avoid && InTownGrounds(nx, ny));
        for (int i = 0; i < steps; i++)
        {
            (int, int) first = hx != 0 && (hy == 0 || i % 2 == 0) ? (x + hx, y) : (x, y + hy);
            (int, int) second = first.Item1 != x ? (x, y + hy) : (x + hx, y);
            if (Ok(first.Item1, first.Item2) && (first.Item1 != x || first.Item2 != y)) (x, y) = first;
            else if ((second.Item1 != x || second.Item2 != y) && Ok(second.Item1, second.Item2)) (x, y) = second;
            else break;
            path.Add(Cell(x, y));
            if (stopOutsideTowns && !InTownGrounds(x, y)) break;
        }
        return path.ToArray();
    }
}
