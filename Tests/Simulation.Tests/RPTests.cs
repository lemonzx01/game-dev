using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>
/// RP predator viability (Body on): wide search when needs go unmet, and wild predators keeping out of town grounds.
/// Balance (wolves alive after 60 days on every seed) is measured with Tests/WorldReport; see docs/reports/RP-predators.md.
/// </summary>
internal static class RPTests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("RP hungry wild wolf roams far when no prey is in sight", HungryWolfRoams),
        ("RP thirsty wild deer roams when no water is in sight", ThirstyDeerRoams),
        ("RP wild wolf does not hunt prey inside town grounds", NoHuntInTown),
        ("RP wild wolf leaves town grounds before resting", LeavesTownToRest),
        ("RP roaming wolf: live equals saved and loaded", RoamLiveEqualsLoaded),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    // Open 80x80 grass, mountain border, nothing to eat or drink; sleep windows closed (sleep is not under test).
    private static SimulationWorld World(VitalsConfig vitals, BodyConfig? body = null, long fatiguePerTick = 0,
        ImmutableSortedDictionary<int, SpeciesDef>? species = null)
    {
        var rows = Enumerable.Range(0, 80).Select(y => y is 0 or 79 ? new string('^', 80) : "^" + new string('.', 78) + "^").ToList();
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 11, MapWidth = 80, MapHeight = 80, Map = MapOverride.FromRows(rows, []), Species = species ?? Default,
            Vitals = vitals,
            Mind = MindConfig.Default with { FatiguePerTick = fatiguePerTick },
            Settlements = SettlementConfig.Default with { FarmPlots = 0 },
            Body = body ?? BodyConfig.Realistic with { DiurnalStart = 999, DiurnalEnd = 0, NocturnalStart = 999, NocturnalEnd = 0 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static readonly VitalsConfig Hungry = new(5_000, 0, 0, 0, 0, 250_000, 100_000, 4);
    private static readonly VitalsConfig Thirsty = new(0, 5_000, 0, 0, 0, 250_000, 100_000, 4);
    private static BodyConfig Awake => BodyConfig.Realistic with { DiurnalStart = 999, DiurnalEnd = 0, NocturnalStart = 999, NocturnalEnd = 0 };

    private static long Spawn(SimulationWorld world, int x, int y, int species, Sex sex = Sex.Unspecified)
    {
        long before = world.LivePositionsForTest().Select(p => p.Id).DefaultIfEmpty(0).Max();
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species, sex));
        world.Step();
        return world.LivePositionsForTest().Select(p => p.Id).Where(id => id > before).Single();
    }

    private static (int X, int Y) Pos(SimulationWorld world, long id) => world.LivePositionsForTest().Single(p => p.Id == id) is var p ? (p.X, p.Y) : default;

    /// <summary>Farthest Chebyshev distance from the start reached within the given ticks.</summary>
    private static int Reach(SimulationWorld world, long id, int ticks)
    {
        var (sx, sy) = Pos(world, id);
        int best = 0;
        for (int t = 0; t < ticks; t++)
        {
            world.Step();
            var (x, y) = Pos(world, id);
            best = Math.Max(best, Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)));
        }
        return best;
    }

    private static void HungryWolfRoams()
    {
        int Measure(BodyConfig body)
        {
            var world = World(Hungry, body);
            long wolf = Spawn(world, 40, 40, Wolf);
            return Reach(world, wolf, 1500);
        }
        int roam = Measure(Awake), control = Measure(Awake with { RoamSteps = 0 });
        Check(roam >= 20, $"hungry wolf searches widely (reached {roam} tiles)");
        Check(roam > control + 8, $"roaming reaches farther than one-tile wandering ({roam} vs {control})");
    }

    private static void ThirstyDeerRoams()
    {
        int Measure(BodyConfig body)
        {
            var world = World(Thirsty, body);
            long deer = Spawn(world, 40, 40, Deer);
            return Reach(world, deer, 1500);
        }
        int roam = Measure(Awake), control = Measure(Awake with { RoamSteps = 0 });
        Check(roam >= 20, $"thirsty deer searches widely for water (reached {roam} tiles)");
        Check(roam > control + 8, $"roaming reaches farther than one-tile wandering ({roam} vs {control})");
    }

    private static int HuntsOn(bool avoid)
    {
        // A calm rabbit (fears nothing) so the test is about the wolf's choice, not about the rabbit running off first.
        var calm = Default.SetItem(Rabbit, Default[Rabbit] with { Fears = [] });
        var world = World(Hungry with { HungerPerTick = 50_000 }, Awake with { PredatorsAvoidTowns = avoid, RoamSteps = 0 }, species: calm);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 40, 40, Faction.Human, "Testford"));
        world.Step();
        long rabbit = Spawn(world, 48, 40, Rabbit); // inside the grounds (radius 10)
        long wolf = Spawn(world, 52, 40, Wolf);     // just outside, prey well within perception
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        int hunts = 0;
        for (int t = 0; t < 400; t++)
        {
            world.Step();
            var page = world.ReadEvents(cursor, 256);
            cursor = page.NextCursor;
            hunts += page.Records.Count(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Hunt && e.Actor == wolf && e.Target == rabbit);
        }
        return hunts;
    }

    private static void NoHuntInTown()
    {
        Check(HuntsOn(avoid: true) == 0, "wolf leaves prey inside town grounds alone");
        Check(HuntsOn(avoid: false) > 0, "control: without town avoidance the same wolf hunts it");
    }

    private static (int RestsInside, bool EndedOutside) RestNearTown(bool avoid)
    {
        var world = World(new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4), Awake with { PredatorsAvoidTowns = avoid }, fatiguePerTick: 200_000); // tired at once
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 40, 40, Faction.Human, "Testford"));
        world.Step();
        long wolf = Spawn(world, 43, 41, Wolf);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        int inside = 0;
        int radius = SettlementConfig.Default.Radius;
        for (int t = 0; t < 600; t++)
        {
            world.Step();
            var page = world.ReadEvents(cursor, 256);
            cursor = page.NextCursor;
            inside += page.Records.Count(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf
                && e.Reason is (int)ActionKind.Rest or (int)ActionKind.Sleep && Math.Max(Math.Abs(e.X - 40), Math.Abs(e.Y - 40)) <= radius);
        }
        var (x, y) = Pos(world, wolf);
        return (inside, Math.Max(Math.Abs(x - 40), Math.Abs(y - 40)) > radius);
    }

    private static void LeavesTownToRest()
    {
        var (inside, outside) = RestNearTown(avoid: true);
        Check(inside == 0 && outside, $"tired wolf walks out of the grounds first ({inside} rests inside, outside at end: {outside})");
        var (controlInside, _) = RestNearTown(avoid: false);
        Check(controlInside > 0, "control: without town avoidance it rests where it stands");
    }

    private static void RoamLiveEqualsLoaded()
    {
        var world = World(Hungry with { ThirstPerTick = 1_000 });
        Spawn(world, 40, 40, Wolf, Sex.Female);
        Spawn(world, 20, 60, Deer);
        for (int t = 0; t < 700; t++) world.Step();   // hungry and roaming by now
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        Check(world.StateHash() == loaded.StateHash(), "identical hash after load");
        for (int t = 1; t <= 900; t++)
        {
            world.Step();
            loaded.Step();
            if (t % 100 == 0) Check(world.StateHash() == loaded.StateHash(), $"tick +{t}: live and loaded diverge");
        }
    }
}
