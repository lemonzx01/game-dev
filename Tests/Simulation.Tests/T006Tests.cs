using FantasyWorld.Simulation;
using System.Collections.Immutable;

/// <summary>T006 acceptance: nearby results correct, path request budget enforced, unreachable bounded, blocked path handled.</summary>
internal static class T006Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T006 nearby queries match brute force while agents move and die", Nearby),
        ("T006 path request node budget enforced per tick, FIFO drains", PathBudget),
        ("T006 unreachable goal retried only after cooldown", Unreachable),
        ("T006 blocked path re-plans around, full block gives up", BlockedPath),
        ("T006 God SetTerrain validation and map update", SetTerrain),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static readonly VitalsConfig Calm = new(0, 0, 0, 0, 0, 250_000, 100_000, 4);
    private static readonly MindConfig QuietMind = MindConfig.Default with { FatiguePerTick = 0 };

    private static SimulationWorld World(string[] rows, VitalsConfig vitals, MindConfig mind, params ResourcePlacement[] resources)
    {
        var map = MapOverride.FromRows(rows, resources);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 42, MapWidth = map.Width, MapHeight = map.Height, Map = map, Vitals = vitals, Mind = mind,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static CommandReceipt God_(SimulationWorld world, GameCommand c) => world.SubmitCommand(c);

    private static long Spawn(SimulationWorld world, int x, int y)
    {
        var receipt = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, 1));
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        world.Step();
        var outcome = world.ReadEvents(cursor, 256).Records.Single(e => e.CommandSequence == receipt.CommandSequence);
        Check(outcome.Kind == EventKinds.CommandApplied, $"spawn rejected {(CommandReason)outcome.Reason}");
        return outcome.Target;
    }

    private static List<WorldEvent> Run(SimulationWorld world, int ticks, ref long cursor)
    {
        var all = new List<WorldEvent>();
        for (int i = 0; i < ticks; i++)
        {
            world.Step();
            while (true)
            {
                var page = world.ReadEvents(cursor, 256);
                Check(page.Status == PageStatus.Ok, "gap in test");
                all.AddRange(page.Records);
                if (page.NextCursor == cursor) break;
                cursor = page.NextCursor;
            }
        }
        return all;
    }

    private static EntityRow Row(SimulationWorld world, long id) => world.ReadSnapshot().Chunks.SelectMany(c => c).Single(r => r.Id.Value == id);

    private static void Nearby()
    {
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 5, MapWidth = 96, MapHeight = 96,
            Vitals = VitalsConfig.Default with { ThirstPerTick = 20_000, DehydrationDamagePerTick = 50_000 }, // some die early
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        var map = world.ReadMap();
        var land = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Where((_, k) => k % 7 == 0).Take(500).ToList();
        foreach (int c in land) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, c % 96, c / 96, 1));
        int checks = 0;
        for (int round = 0; round < 8; round++)
        {
            for (int t = 0; t < 15; t++) world.Step();
            var live = world.LivePositionsForTest().ToList();
            for (int q = 0; q < 40; q++)
            {
                int x = q * 37 % 96, y = q * 53 % 96, r = q % 13;
                var expected = live.Where(p => Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y)) <= r).Select(p => p.Id).OrderBy(i => i).ToList();
                Check(world.NearbyIds(x, y, r).SequenceEqual(expected), $"round {round} query ({x},{y},r={r})");
                checks++;
            }
        }
        Check(world.ReadHistory(0, 256).Records.Length > 0, "deaths occurred during the check");
        Check(checks == 320, "all queries checked");
    }

    private static void PathBudget()
    {
        // 120 hungry agents on an open field all want food at once.
        var rows = Enumerable.Range(0, 30).Select(y => y is 0 or 29 ? new string('^', 40) : "^" + new string('.', 38) + "^").ToArray();
        var food = Enumerable.Range(0, 10).Select(k => new ResourcePlacement(2 + k * 4, 15, ResourceKind.Food, 20)).ToArray();
        var mind = QuietMind with { SearchNodeBudget = 400, PathNodeBudgetPerTick = 1200 };
        var world = World(rows, Calm with { HungerPerTick = 400_000 }, mind, food);
        for (int k = 0; k < 120; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 1 + k % 38, 1 + k / 38 * 9, 1));
        long maxExpanded = 0, served = 0;
        int maxQueue = 0, ticks = 0;
        for (; ticks < 300 && (ticks < 20 || world.PendingPathRequests > 0); ticks++)
        {
            world.Step();
            maxExpanded = Math.Max(maxExpanded, world.LastPathExpansions);
            served += world.LastPathsServed;
            maxQueue = Math.Max(maxQueue, world.PendingPathRequests);
            Check(world.LastPathExpansions <= 1200, $"tick expanded {world.LastPathExpansions} nodes > budget 1200");
        }
        Check(maxExpanded <= 1200, $"expanded {maxExpanded} nodes in one tick > budget 1200");
        Check(maxQueue > 3, "requests actually queued beyond one tick's budget");
        Check(world.PendingPathRequests == 0 && served >= 120, $"queue drained ({served} served in {ticks} ticks)");
    }

    private static readonly string[] Lake =
    [
        "~~~~~~~~~~~~~~~~~~~~",
        "~..................~",
        "~......~~~~~.......~",
        "~......~...~.......~",
        "~......~~~~~.......~",
        "~..................~",
        "~~~~~~~~~~~~~~~~~~~~",
    ];

    private static void Unreachable()
    {
        var world = World(Lake, Calm with { HungerPerTick = 100_000 }, QuietMind, new ResourcePlacement(9, 3, ResourceKind.Food, 20));
        long id = Spawn(world, 3, 3);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 500, ref cursor).Where(e => e.Actor == id).ToList();
        var fails = events.Where(e => e.Kind == EventKinds.PathFailed).Select(e => e.Tick).ToList();
        Check(fails.Count >= 2, "searched and failed");
        Check(fails.Zip(fails.Skip(1), (a, b) => b - a).All(gap => gap >= QuietMind.UnreachableCooldownTicks), $"retries respect cooldown: {string.Join(",", fails)}");
        Check(!events.Any(e => e.Kind == EventKinds.EntityAte), "island food never eaten");
    }

    private static readonly string[] Ring =
    [
        "^^^^^^^^^^^",
        "^.........^",
        "^.^^^^^^^.^",
        "^.........^",
        "^^^^^^^^^^^",
    ];

    private static void BlockedPath()
    {
        foreach (bool fullBlock in new[] { false, true })
        {
            var world = World(Ring, Calm with { HungerPerTick = 100_000 }, QuietMind, new ResourcePlacement(9, 3, ResourceKind.Food, 20));
            long id = Spawn(world, 1, 3);
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            var events = new List<WorldEvent>();
            for (int t = 0; t < 60 && !(world.ActionOf(id) == ActionKind.Eat && Row(world, id).X >= 2); t++) events.AddRange(Run(world, 1, ref cursor));
            Check(world.ActionOf(id) == ActionKind.Eat && Row(world, id).Y == 3, "walking the short bottom route");
            Check(God_(world, new SetTerrainCommand(W, God, CommandModes.God, 6, 3, Terrain.Mountain)).Status == "Queued", "block bottom");
            if (fullBlock) God_(world, new SetTerrainCommand(W, God, CommandModes.God, 6, 1, Terrain.Mountain));
            events.AddRange(Run(world, 80, ref cursor));
            var mine = events.Where(e => e.Actor == id).ToList();
            var ate = mine.FirstOrDefault(e => e.Kind == EventKinds.EntityAte);
            if (!fullBlock)
            {
                Check(ate.Kind == EventKinds.EntityAte && (ate.X, ate.Y) == (9, 3), "re-planned via the top route and ate");
                Check(!mine.Any(e => e.Kind == EventKinds.PathFailed), "no failure when an alternative exists");
            }
            else
            {
                Check(ate.Kind != EventKinds.EntityAte, "fully blocked food not reached");
                Check(mine.Any(e => e.Kind == EventKinds.PathFailed && e.Reason == (int)ActionKind.Eat), "gave up with PathFailed");
            }
            Check(world.ReadMap().At(6, 3) == Terrain.Mountain, "map view updated");
        }
    }

    private static void SetTerrain()
    {
        var world = World(Lake, Calm, MindConfig.Disabled, new ResourcePlacement(2, 1, ResourceKind.Food, 20));
        long id = Spawn(world, 5, 5);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 2, 1, Terrain.Mountain)); // resource tile
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 5, 5, Terrain.Water));    // occupied tile
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 5, 5, Terrain.Forest));   // occupied, still passable
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 8, 3, Terrain.Grass));    // flood island interior? no: dry a water tile
        Check(world.SubmitCommand(new SetTerrainCommand(W, 99, CommandModes.God, 1, 1, Terrain.Water)).Reason == "Unauthorized", "needs God grant");
        Check(world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 1, 1, (Terrain)42)).Reason == "InvalidCommand", "unknown terrain");
        var outcomes = Run(world, 1, ref cursor).Where(e => e.Kind is EventKinds.CommandApplied or EventKinds.CommandRejected).ToList();
        Check(outcomes.Select(e => (CommandReason)e.Reason).SequenceEqual(
            [CommandReason.HasResource, CommandReason.Occupied, CommandReason.None, CommandReason.None]), "outcome reasons");
        var map = world.ReadMap();
        Check(map.At(2, 1) == Terrain.Grass && map.At(5, 5) == Terrain.Forest && map.At(8, 3) == Terrain.Grass, "applied tiles only");
        Check(world.ReadEntityDetails(W, new EntityId(id))!.LifeState == LifeStates.Alive, "entity unaffected");
    }
}
