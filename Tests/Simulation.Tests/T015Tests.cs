using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T015 acceptance: two settlements (humans, demons), farms/storage/jobs, stock conservation,
/// production and consumption, display read model, no inherent good/evil rule.</summary>
internal static class T015Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T015 founding settlements: plots, validation, spacing", Founding),
        ("T015 membership follows faction and distance", Membership),
        ("T015 jobs produce: harvest, gather, deliver; members eat from storage", Production),
        ("T015 food and wood are conserved through every step", Conservation),
        ("T015 humans and demons follow identical rules", Symmetry),
        ("T015 settlements survive save/load and continue identically", SaveLoad),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    // 60×24 grass field: forest band with wood (left), food bushes, a lake row for drinking. Two town sites far apart.
    private static SimulationWorld World(VitalsConfig? vitals = null)
    {
        var rows = new List<string>();
        for (int y = 0; y < 24; y++)
        {
            var row = new char[60];
            for (int x = 0; x < 60; x++)
                row[x] = y is 0 or 23 || x is 0 or 59 ? '^' : y == 12 && x is > 20 and < 40 ? '~' : x is >= 2 and <= 5 || x is >= 54 and <= 57 ? 'T' : '.';
            rows.Add(new string(row));
        }
        var resources = new List<ResourcePlacement>();
        for (int y = 2; y < 22; y += 3)
        {
            resources.Add(new ResourcePlacement(3, y, ResourceKind.Wood, 50));
            resources.Add(new ResourcePlacement(56, y, ResourceKind.Wood, 50));
            resources.Add(new ResourcePlacement(9, y, ResourceKind.Food, 20));
            resources.Add(new ResourcePlacement(50, y, ResourceKind.Food, 20));
        }
        for (int x = 21; x < 40; x++) resources.Add(new ResourcePlacement(x, 12, ResourceKind.Water, 1000));
        var map = MapOverride.FromRows(rows, [.. resources]);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 15, MapWidth = 60, MapHeight = 24, Map = map, Species = Default,
            Vitals = vitals ?? VitalsConfig.Default with { HungerPerTick = 1500, ThirstPerTick = 600 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static List<WorldEvent> Run(SimulationWorld world, int ticks, ref long cursor, Action? perTick = null)
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
            perTick?.Invoke();
        }
        return all;
    }

    private static (SimulationWorld World, long Cursor) TwoTowns(int humanX, int demonX, int membersEach)
    {
        var world = World();
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, humanX, 6, Faction.Human, "Ashford"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, demonX, 6, Faction.Demon, "Cinderhold"));
        world.Step();
        for (int k = 0; k < membersEach; k++)
        {
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, humanX - 2 + k % 5, 8 + k / 5, Human, k % 2 == 0 ? Sex.Female : Sex.Male));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, demonX - 2 + k % 5, 8 + k / 5, Demon, k % 2 == 0 ? Sex.Female : Sex.Male));
        }
        world.Step();
        return (world, world.ReadEvents(0, 1).LatestSequence);
    }

    private static void Founding()
    {
        var world = World();
        long cursor = 0;
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 15, 6, Faction.Human, "Ashford"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 25, 6, Faction.Demon, "Too Near"));   // within 2 radii
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 45, 12, Faction.Demon, "Wet"));      // lake tile? (x 45 is grass)
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 30, 12, Faction.Demon, "Lake"));     // water
        Check(world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 40, 6, Faction.None, "X")).Reason == "InvalidCommand", "faction required");
        Check(world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 40, 6, Faction.Human, " ")).Reason == "InvalidCommand", "name required");
        Check(world.SubmitCommand(new FoundSettlementCommand(W, 99, CommandModes.God, 40, 6, Faction.Human, "X")).Reason == "Unauthorized", "God only");
        var events = Run(world, 1, ref cursor).Where(e => e.Kind is EventKinds.CommandApplied or EventKinds.CommandRejected).ToList();
        Check(events.Select(e => (CommandReason)e.Reason).SequenceEqual([CommandReason.None, CommandReason.TooClose, CommandReason.None, CommandReason.Impassable]),
            string.Join(",", events.Select(e => (CommandReason)e.Reason)));
        var towns = world.ReadSettlements();
        Check(towns.Count == 2 && towns[0] is { Id: 1, Name: "Ashford", Faction: Faction.Human, X: 15, Y: 6, Food: 0, Wood: 0 } && towns[1].Faction == Faction.Demon, "two towns");
        var plots = world.ReadFarmPlots(1);
        Check(plots.Count == SettlementConfig.Default.FarmPlots && plots.All(p => world.ReadMap().At(p.X, p.Y) == Terrain.Grass && Math.Abs(p.X - 15) + Math.Abs(p.Y - 6) <= 3), "farm plots on nearby grass");
    }

    private static void Membership()
    {
        var (world, _) = TwoTowns(15, 45, 3);
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 16, 7, Demon));   // demon near the human town
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 16, 8, Wolf));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 40, 20, Human));  // 25 tiles from Ashford > 2 radii
        world.Step();
        var rows = world.ReadSnapshot().Chunks.SelectMany(c => c).ToList();
        int Town(EntityRow r) => world.ReadEntityDetails(W, r.Id)!.SettlementId;
        Check(rows.Where(r => r.VisualKey == Human && r.X < 25).All(r => Town(r) == 1), "humans near Ashford join it");
        Check(rows.Where(r => r.VisualKey == Demon && r.X > 35).All(r => Town(r) == 2), "demons near Cinderhold join it");
        Check(rows.Where(r => r.X == 16 && r.Y == 7).All(r => Town(r) == 0), "a demon does not join a human town");
        Check(rows.Where(r => r.VisualKey == Wolf).All(r => Town(r) == 0), "animals never join");
        Check(rows.Where(r => r.X == 40 && r.Y == 20).All(r => Town(r) == 0), "settlers too far stay independent");
        Check(world.ReadSettlements().All(s => s.Population == 3), "population counts members");
    }

    private static void Production()
    {
        var (world, cursor) = TwoTowns(15, 45, 6);
        var events = Run(world, 2500, ref cursor);
        foreach (var town in world.ReadSettlements())
        {
            var mine = events.Where(e => e.Target == town.Id).ToList();
            Check(mine.Any(e => e.Kind == EventKinds.Harvested), $"{town.Name} harvested crops");
            Check(mine.Any(e => e.Kind == EventKinds.Gathered && e.Reason == (int)ResourceKind.Food), $"{town.Name} gathered wild food");
            Check(mine.Any(e => e.Kind == EventKinds.Gathered && e.Reason == (int)ResourceKind.Wood), $"{town.Name} cut wood");
            Check(mine.Any(e => e.Kind == EventKinds.Deposited), $"{town.Name} deposited into storage");
            Check(mine.Any(e => e.Kind == EventKinds.EntityAte), $"{town.Name} members ate from storage");
            Check(town.Wood > 0, $"{town.Name} stored wood");
        }
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Work), "work actions started");
    }

    private static void Conservation()
    {
        var (world, cursor) = TwoTowns(15, 45, 8);
        bool negative = false;
        var events = Run(world, 3000, ref cursor, () => negative |= world.ReadSettlements().Any(s => s.Food < 0 || s.Wood < 0));
        Check(!negative, "storage never negative");
        long carried = world.ReadSnapshot().Chunks.SelectMany(c => c).Sum(r => (long)world.ReadEntityDetails(W, r.Id)!.CarryAmount);
        foreach (var town in world.ReadSettlements())
        {
            long Sum(int kind, ResourceKind res) => events.Where(e => e.Kind == kind && e.Target == town.Id && e.Reason == (int)res).Sum(e => e.Amount);
            long harvested = events.Where(e => e.Kind == EventKinds.Harvested && e.Target == town.Id).Sum(e => e.Amount);
            long gatheredFood = Sum(EventKinds.Gathered, ResourceKind.Food), depositedFood = Sum(EventKinds.Deposited, ResourceKind.Food);
            long eaten = events.Where(e => e.Kind == EventKinds.EntityAte && e.Target == town.Id).Sum(e => e.Amount);
            Check(town.Food == depositedFood - eaten, $"{town.Name}: food stock {town.Food} = deposited {depositedFood} − eaten {eaten}");
            Check(town.Wood == Sum(EventKinds.Deposited, ResourceKind.Wood), $"{town.Name}: wood stock = wood deposited");
            Check(harvested + gatheredFood > 0, $"{town.Name} produced food");
        }
        long produced = events.Where(e => e.Kind is EventKinds.Harvested or EventKinds.Gathered).Sum(e => e.Amount);
        long deposited = events.Where(e => e.Kind == EventKinds.Deposited).Sum(e => e.Amount);
        long lost = events.Where(e => e.Kind == EventKinds.CarryLost).Sum(e => e.Amount);
        Check(produced == deposited + carried + lost, $"produced {produced} = deposited {deposited} + carried {carried} + lost {lost}");
    }

    private static void Symmetry()
    {
        // Same world, factions swapped between the two sites: every per-site number must match.
        (SimulationWorld World, long Cursor) Mirror(bool humansWest)
        {
            var world = World();
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 15, 6, humansWest ? Faction.Human : Faction.Demon, "West"));
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 45, 6, humansWest ? Faction.Demon : Faction.Human, "East"));
            world.Step();
            for (int k = 0; k < 6; k++)
            {
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 13 + k % 5, 8 + k / 5, humansWest ? Human : Demon, k % 2 == 0 ? Sex.Female : Sex.Male));
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 43 + k % 5, 8 + k / 5, humansWest ? Demon : Human, k % 2 == 0 ? Sex.Female : Sex.Male));
            }
            world.Step();
            return (world, 0);
        }
        var (a, _) = Mirror(true);
        var (b, _) = Mirror(false);
        for (int t = 0; t < 2000; t++) { a.Step(); b.Step(); }
        var sa = a.ReadSettlements(); var sb = b.ReadSettlements();
        for (int i = 0; i < 2; i++)
            Check(sa[i] with { Faction = Faction.None } == sb[i] with { Faction = Faction.None }, $"site {sa[i].Name}: {sa[i]} vs {sb[i]}");
        Check(sa[0].Faction != sb[0].Faction, "factions really swapped");
    }

    private static void SaveLoad()
    {
        var (world, _) = TwoTowns(15, 45, 6);
        for (int t = 0; t < 900; t++) world.Step();
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        Check(loaded.StateHash() == world.StateHash() && loaded.ReadSettlements().SequenceEqual(world.ReadSettlements()), "settlements roundtrip");
        for (int t = 0; t < 600; t++) { world.Step(); loaded.Step(); }
        Check(loaded.StateHash() == world.StateHash(), "settlement economy continues identically after load");
    }
}
