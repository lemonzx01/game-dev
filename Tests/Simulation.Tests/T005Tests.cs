using FantasyWorld.Simulation;
using System.Collections.Immutable;

/// <summary>T005 acceptance: hunger selects reachable food, drink, rest, flee + interrupt, no rapid oscillation, decision budget.</summary>
internal static class T005Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);
    private const int Prey = 1, Predator = 2;

    public static (string, Action)[] Cases =>
    [
        ("T005 hunger selects reachable food, not the unreachable nearer one", ReachableFood),
        ("T005 thirst walks to shore and drinks", Drink),
        ("T005 fatigue leads to rest and recovery", Rest),
        ("T005 danger interrupts and flee increases distance", FleeInterrupt),
        ("T005 committed actions complete without oscillation", NoOscillation),
        ("T005 decisions staggered and within per-tick budget", Budget),
        ("T005 agent survives days on real food and water", Survival),
        ("T005 deterministic AI replay", Determinism),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    // No starvation damage unless asked; mind with no fatigue unless asked.
    private static readonly VitalsConfig Calm = new(0, 0, 0, 0, 0, 250_000, 100_000, 4);
    private static readonly MindConfig QuietMind = MindConfig.Default with { FatiguePerTick = 0 };

    private static SimulationWorld World(string[] rows, VitalsConfig vitals, MindConfig mind, params ResourcePlacement[] resources)
    {
        var map = MapOverride.FromRows(rows, resources);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 42, MapWidth = map.Width, MapHeight = map.Height, Map = map,
            Vitals = vitals, Mind = mind,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static long Spawn(SimulationWorld world, int x, int y, int species = Prey)
    {
        var receipt = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species));
        Check(receipt.Status == "Queued", "spawn " + receipt.Reason);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        world.Step();
        var outcome = world.ReadEvents(cursor, 256).Records.Single(e => e.CommandSequence == receipt.CommandSequence);
        Check(outcome.Kind == EventKinds.CommandApplied, $"spawn at {x},{y} rejected: {(CommandReason)outcome.Reason}");
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
                Check(page.Status == PageStatus.Ok, "event gap in test");
                all.AddRange(page.Records);
                if (page.NextCursor == cursor) break;
                cursor = page.NextCursor;
            }
        }
        return all;
    }

    private static EntityRow Row(SimulationWorld world, long id) => world.ReadSnapshot().Chunks.SelectMany(c => c).Single(r => r.Id.Value == id);

    private static readonly string[] Lake =
    [
        "~~~~~~~~~~~~~~~~~~~~",
        "~..................~",
        "~..................~",
        "~......~~~~~.......~",
        "~......~...~.......~",
        "~......~~~~~.......~",
        "~..................~",
        "~..................~",
        "~~~~~~~~~~~~~~~~~~~~",
    ];

    private static void ReachableFood()
    {
        // Island food at (9,4) is 4 steps away but walled by water; mainland food at (2,7) is 6 steps away.
        var vitals = Calm with { HungerPerTick = 50_000 };
        var world = World(Lake, vitals, QuietMind,
            new ResourcePlacement(9, 4, ResourceKind.Food, 20), new ResourcePlacement(2, 7, ResourceKind.Food, 20));
        long id = Spawn(world, 5, 4);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 60, ref cursor);
        var started = events.Where(e => e.Kind == EventKinds.ActionStarted && e.Actor == id && e.Reason == (int)ActionKind.Eat).ToList();
        Check(started.Count >= 1, "hunger chose Eat");
        var ate = events.FirstOrDefault(e => e.Kind == EventKinds.EntityAte && e.Actor == id);
        Check(ate.Kind == EventKinds.EntityAte && (ate.X, ate.Y) == (2, 7), $"ate at reachable food, got {ate}");
        Check(world.ReadResource(9, 4, ResourceKind.Food)!.Value.Amount == 20, "island food untouched");
        Check(ate.Amount > 0 && world.ReadResource(2, 7, ResourceKind.Food)!.Value.Amount < 20, "mainland stock consumed");
    }

    private static void Drink()
    {
        var vitals = Calm with { ThirstPerTick = 50_000 };
        var world = World(Lake, vitals, QuietMind, new ResourcePlacement(0, 4, ResourceKind.Water, 100));
        long id = Spawn(world, 5, 4);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 60, ref cursor);
        var drank = events.FirstOrDefault(e => e.Kind == EventKinds.EntityDrank && e.Actor == id);
        Check(drank.Kind == EventKinds.EntityDrank && (drank.X, drank.Y) == (0, 4), "drank from the shore node");
        Check(world.ReadResource(0, 4, ResourceKind.Water)!.Value.Amount < 100, "water stock consumed");
    }

    private static void Rest()
    {
        var mind = MindConfig.Default with { FatiguePerTick = 100_000, RestRecoveryPerTick = 50_000 };
        var world = World(Lake, Calm, mind);
        long id = Spawn(world, 5, 4);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        bool rested = false;
        long peak = 0;
        for (int t = 0; t < 40 && !rested; t++)
        {
            Run(world, 1, ref cursor);
            peak = Math.Max(peak, world.FatigueOf(id));
            rested = world.ActionOf(id) == ActionKind.Rest;
        }
        Check(rested && peak >= mind.RestStartFatigue, "tired entity chose Rest");
        var restRow = Row(world, id);
        Check(restRow.Action == (int)ActionKind.Rest, "snapshot shows Rest action");
        long before = world.FatigueOf(id);
        Run(world, 5, ref cursor);
        Check(world.FatigueOf(id) < before && Row(world, id) is var r && (r.X, r.Y) == (restRow.X, restRow.Y), "recovers in place");
        Run(world, 20, ref cursor);
        Check(world.ActionOf(id) != ActionKind.Rest, "rest ends after recovery");
    }

    private static readonly string[] Field =
    [
        "^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^",
        "^............................^",
        "^............................^",
        "^............^^^.............^",
        "^............^.^.............^",
        "^............^^^.............^",
        "^............................^",
        "^............................^",
        "^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^",
    ];

    private static void FleeInterrupt()
    {
        // Prey walks east toward food at (10,4) (within perception); a predator penned at (14,4) appears ahead.
        var mind = QuietMind with
        {
            FearOf = ImmutableSortedDictionary<int, ImmutableArray<int>>.Empty.Add(Prey, [Predator]),
            CommitmentTicks = 100, // even a long commitment must yield to danger
        };
        var vitals = Calm with { HungerPerTick = 100_000 };
        var world = World(Field, vitals, mind, new ResourcePlacement(10, 4, ResourceKind.Food, 20));
        long prey = Spawn(world, 3, 4);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        for (int t = 0; t < 40 && world.ActionOf(prey) != ActionKind.Eat; t++) Run(world, 1, ref cursor);
        Check(world.ActionOf(prey) == ActionKind.Eat, "prey committed to Eat");
        Run(world, 2, ref cursor);
        long predator = Spawn(world, 14, 4, Predator);
        var p = Row(world, prey);
        int before = Math.Abs(p.X - 14) + Math.Abs(p.Y - 4);
        Check(before <= mind.PerceptionRadius * 2, "predator within perception");
        long spawnTick = world.LastCompletedTick;
        long fleeTick = -1;
        for (int t = 0; t < mind.ThinkIntervalTicks + 2 && fleeTick < 0; t++)
        {
            Run(world, 1, ref cursor);
            if (world.ActionOf(prey) == ActionKind.Flee) fleeTick = world.LastCompletedTick;
        }
        p = Row(world, prey);
        int atFlee = Math.Abs(p.X - 14) + Math.Abs(p.Y - 4);
        Check(fleeTick > 0 && fleeTick - spawnTick <= mind.ThinkIntervalTicks + 1, $"danger interrupted Eat within one think interval ({fleeTick - spawnTick})");
        Run(world, mind.MoveTicksPerTile * 4, ref cursor);
        p = Row(world, prey);
        Check(Math.Abs(p.X - 14) + Math.Abs(p.Y - 4) > atFlee, $"fleeing increased distance from {atFlee}");
        Check(world.ActionOf(predator) == ActionKind.Idle, "penned predator cannot move");
    }

    private static void NoOscillation()
    {
        // Equal hunger and thirst; food 7 tiles west, water 7 tiles east. Without commitment the agent would
        // flip between the two as the scores cross; every started Eat/Drink must finish before the next starts.
        var vitals = Calm with { HungerPerTick = 30_000, ThirstPerTick = 30_000 };
        string[] rows =
        [
            "^^^^^^^^^^^^^^^^^",
            "^...............~",
            "^...............~",
            "^^^^^^^^^^^^^^^^^",
        ];
        var world = World(rows, vitals, QuietMind,
            new ResourcePlacement(1, 1, ResourceKind.Food, 20), new ResourcePlacement(16, 1, ResourceKind.Water, 1000));
        long id = Spawn(world, 8, 1);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 400, ref cursor).Where(e => e.Actor == id).ToList();
        ActionKind? open = null;
        int completed = 0;
        foreach (var e in events)
        {
            if (e.Kind == EventKinds.ActionStarted && e.Reason is (int)ActionKind.Eat or (int)ActionKind.Drink)
            {
                Check(open is null, $"switched to {(ActionKind)e.Reason} before finishing {open} at tick {e.Tick}");
                open = (ActionKind)e.Reason;
            }
            if ((e.Kind == EventKinds.EntityAte && open == ActionKind.Eat) || (e.Kind == EventKinds.EntityDrank && open == ActionKind.Drink))
            {
                open = null;
                completed++;
            }
        }
        Check(completed >= 4, $"several needs satisfied ({completed})");
    }

    private static void Budget()
    {
        const int budget = 64;
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 9, MapWidth = 128, MapHeight = 128, DiscretionaryWorkBudget = budget,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        var map = world.ReadMap();
        var land = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Take(1000).ToList();
        for (int k = 0; k < land.Count; k++)
        {
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land[k] % 128, land[k] / 128, 1));
            if (k % 1000 == 999) world.Step();
        }
        int maxRun = 0, maxPending = 0;
        for (int t = 0; t < 60; t++)
        {
            world.Step();
            maxRun = Math.Max(maxRun, world.LastDiscretionaryRun);
            maxPending = Math.Max(maxPending, world.PendingDiscretionary);
        }
        Check(maxRun <= budget, $"decisions per tick {maxRun} exceed budget");
        Check(maxPending <= 1000, $"backlog grew beyond one pending decision per entity ({maxPending})");
    }

    private static void Survival()
    {
        string[] rows =
        [
            "~~~~~~~~~~~~",
            "~..........~",
            "~..........~",
            "~..........~",
            "~~~~~~~~~~~~",
        ];
        var world = World(rows, VitalsConfig.Default, MindConfig.Default,
            new ResourcePlacement(3, 2, ResourceKind.Food, 20), new ResourcePlacement(8, 2, ResourceKind.Food, 20),
            new ResourcePlacement(0, 2, ResourceKind.Water, 1000), new ResourcePlacement(11, 2, ResourceKind.Water, 1000));
        long id = Spawn(world, 5, 2);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 3 * 2400 * 2, ref cursor).Where(e => e.Actor == id || e.Target == id).ToList();
        var d = world.ReadEntityDetails(W, new EntityId(id))!;
        Check(d.LifeState == LifeStates.Alive, $"alive after 6 days ({d})");
        Check(events.Count(e => e.Kind == EventKinds.EntityAte) >= 2 && events.Count(e => e.Kind == EventKinds.EntityDrank) >= 2, "ate and drank repeatedly");
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Rest), "rested");
    }

    private static void Determinism()
    {
        string RunOnce()
        {
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W, Seed = 77, MapWidth = 64, MapHeight = 64,
                Vitals = VitalsConfig.Default with { HungerPerTick = 2_000, ThirstPerTick = 3_000 },
                Mind = MindConfig.Default with { FearOf = ImmutableSortedDictionary<int, ImmutableArray<int>>.Empty.Add(Prey, [Predator]) },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            var map = world.ReadMap();
            var land = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Take(300).ToList();
            for (int k = 0; k < land.Count; k++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land[k] % 64, land[k] / 64, k % 10 == 0 ? Predator : Prey));
            for (int t = 0; t < 1500; t++) world.Step();
            return world.StateHash();
        }
        Check(RunOnce() == RunOnce(), "same seed and script, same hash with AI");
    }
}
