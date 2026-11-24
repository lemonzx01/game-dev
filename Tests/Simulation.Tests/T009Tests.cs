using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T009 acceptance: 3–5 species incl. farm/wild/predator; kills consume/transfer real resources.</summary>
internal static class T009Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T009 species catalog, data file and validation", Catalog),
        ("T009 predator kills prey; meat transferred and conserved", KillTransfersMeat),
        ("T009 diets: carnivores skip plants, herbivores skip meat", Diets),
        ("T009 farm animal stays leashed, wild animal roams", Leash),
        ("T009 uneaten carcass decays with explicit sink", Decay),
        ("T009 mixed ecology is deterministic and has predation", Ecosystem),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static readonly VitalsConfig Calm = new(0, 0, 0, 0, 0, 250_000, 100_000, 4);
    private static readonly MindConfig QuietMind = MindConfig.Default with { FatiguePerTick = 0 };

    private static string[] Field(int w, int h) =>
        Enumerable.Range(0, h).Select(y => y == 0 || y == h - 1 ? new string('^', w) : "^" + new string('.', w - 2) + "^").ToArray();

    private static SimulationWorld World(string[] rows, VitalsConfig vitals, MindConfig mind, EcologyConfig? ecology = null, params ResourcePlacement[] resources)
    {
        var map = MapOverride.FromRows(rows, resources);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 42, MapWidth = map.Width, MapHeight = map.Height, Map = map, Vitals = vitals, Mind = mind,
            Species = Default, Ecology = ecology ?? EcologyConfig.Default,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static long Spawn(SimulationWorld world, int x, int y, int species)
    {
        var receipt = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species));
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

    private static void Catalog()
    {
        Check(Default.Count == 4, "4 species");
        Check(Default.Values.Any(d => d.Domestic) && Default.Values.Any(d => !d.Domestic && d.Diet == Diet.Herbivore), "farm and wild animals");
        Check(Default[Wolf].Diet == Diet.Carnivore && Default[Wolf].Hunts.Length == 3, "predator");
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Data", "species.json"))) dir = Path.GetDirectoryName(dir) ?? throw new Exception("Data/species.json not found");
        var loaded = FromJson(File.ReadAllText(Path.Combine(dir, "Data", "species.json")));
        Check(loaded.Keys.SequenceEqual(Default.Keys), "file keys");
        foreach (var (k, d) in Default)
        {
            var f = loaded[k];
            Check(f with { Fears = d.Fears, Hunts = d.Hunts } == d && f.Fears.SequenceEqual(d.Fears) && f.Hunts.SequenceEqual(d.Hunts), $"file matches default for {d.Name}");
        }
        bool Rejects(Func<object> build)
        {
            try { build(); return false; } catch (ArgumentException) { return true; }
        }
        Check(Rejects(() => Build([Default[Deer] with { Fears = [99] }])), "unknown reference rejected");
        Check(Rejects(() => Build([Default[Deer], Default[Deer] with { Hunts = [Deer] }])), "duplicate key rejected");
        Check(Rejects(() => Build([Default[Deer] with { Hunts = [Deer] }])), "herbivore cannot hunt");
        Check(Rejects(() => Build([Default[Sheep] with { Fears = [], LeashRadius = 0 }])), "domestic needs leash");
    }

    private static void KillTransfersMeat()
    {
        var world = World(Field(16, 7), Calm with { HungerPerTick = 50_000 }, QuietMind);
        long deer = Spawn(world, 12, 3, Deer);
        long wolf = Spawn(world, 2, 3, Wolf);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 400, ref cursor);
        var death = events.Single(e => e.Kind == EventKinds.EntityDied);
        Check(death.Target == deer && death.Actor == wolf && death.Reason == (int)DeathCause.Predation, "wolf killed the deer");
        Check(events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == deer), "attacks recorded");
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == deer && e.Reason == (int)ActionKind.Flee), "deer fled first");
        var details = world.ReadEntityDetails(W, new EntityId(deer))!;
        Check(details.DeathCause == DeathCause.Predation, "durable record keeps the cause");
        var meatEaten = events.Where(e => e.Kind == EventKinds.EntityAte && e.Actor == wolf && e.Reason == (int)ResourceKind.Meat).ToList();
        Check(meatEaten.Count >= 1 && meatEaten.All(e => (e.X, e.Y) is var p && Math.Abs(p.X - death.X) + Math.Abs(p.Y - death.Y) == 0), "wolf ate from the carcass");
        long decayed = events.Where(e => e.Kind == EventKinds.CarcassDecayed).Sum(e => e.Amount);
        long eaten = meatEaten.Sum(e => e.Amount);
        Check(eaten + world.TotalCarcassMeat + decayed == Default[Deer].MeatYield, $"meat conserved: eaten {eaten} + left {world.TotalCarcassMeat} + decayed {decayed}");
        Check(!events.Any(e => e.Kind == EventKinds.EntityAte && e.Reason == (int)ResourceKind.Food && e.Actor == wolf), "no plant eating by wolf");
    }

    private static void Diets()
    {
        // Two chambers split by a wall, more than a perception radius apart: the hungry wolf has plants beside it
        // and no prey in sight, the deer has plants and no predator in sight.
        string[] rows =
        [
            "^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^",
            "^.............^..............^",
            "^.............^..............^",
            "^.............^..............^",
            "^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^",
        ];
        var world = World(rows, Calm with { HungerPerTick = 100_000 }, QuietMind, null,
            new ResourcePlacement(3, 2, ResourceKind.Food, 20), new ResourcePlacement(5, 2, ResourceKind.Food, 20),
            new ResourcePlacement(27, 2, ResourceKind.Food, 20));
        long wolf = Spawn(world, 4, 2, Wolf);
        long deer = Spawn(world, 25, 2, Deer);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 5, ref cursor);
        CommandReason wolfPlant = CommandReason.None, deerMeat = CommandReason.None;
        world.Advance(() =>
        {
            wolfPlant = world.TryConsume(new EntityId(wolf), ResourceKind.Food, world.NextTickUnsafe);
            deerMeat = world.TryConsume(new EntityId(deer), ResourceKind.Meat, world.NextTickUnsafe);
        });
        Check(wolfPlant == CommandReason.WrongDiet && deerMeat == CommandReason.WrongDiet, $"diet rules ({wolfPlant}, {deerMeat})");
        var events = Run(world, 200, ref cursor);
        Check(!events.Any(e => e.Kind == EventKinds.EntityAte && e.Actor == wolf && e.Reason == (int)ResourceKind.Food), "wolf never ate plants");
        Check(events.Any(e => e.Kind == EventKinds.EntityAte && e.Actor == deer && e.Reason == (int)ResourceKind.Food), "deer ate plants");
    }

    private static void Leash()
    {
        var world = World(Field(40, 40), Calm, QuietMind);
        long sheep = Spawn(world, 20, 20, Sheep);
        long deer = Spawn(world, 20, 20, Deer);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        int sheepMax = 0, deerMax = 0;
        for (int t = 0; t < 3000; t++)
        {
            Run(world, 1, ref cursor);
            var (sx, sy) = world.PositionOf(sheep);
            var (dx, dy) = world.PositionOf(deer);
            sheepMax = Math.Max(sheepMax, Math.Max(Math.Abs(sx - 20), Math.Abs(sy - 20)));
            deerMax = Math.Max(deerMax, Math.Max(Math.Abs(dx - 20), Math.Abs(dy - 20)));
        }
        Check(world.HomeOf(sheep) == (20, 20), "home is the spawn tile");
        Check(sheepMax <= Default[Sheep].LeashRadius, $"sheep strayed {sheepMax} > leash {Default[Sheep].LeashRadius}");
        Check(deerMax > Default[Sheep].LeashRadius, $"wild deer roamed further ({deerMax})");
    }

    private static void Decay()
    {
        var vitals = new VitalsConfig(100_000, 0, 250_000, 0, 0, 250_000, 100_000, 4); // starves at tick 15 (spawn at 1)
        var world = World(Field(8, 5), vitals, MindConfig.Disabled, new EcologyConfig(50, 4, 80, 60));
        long deer = Spawn(world, 3, 2, Deer);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 15, ref cursor);
        Check(events.Any(e => e.Kind == EventKinds.EntityDied && e.Target == deer && e.Reason == (int)DeathCause.Starvation), "starved");
        Check(world.ReadCarcassMeat(3, 2) == Default[Deer].MeatYield, "carcass holds the body's meat");
        events = Run(world, (int)(15 + 50 - 1 - world.LastCompletedTick), ref cursor); // up to tick 64
        Check(world.ReadCarcassMeat(3, 2) == Default[Deer].MeatYield && !events.Any(e => e.Kind == EventKinds.CarcassDecayed), "not yet decayed");
        events = Run(world, 1, ref cursor);
        var sink = events.Single(e => e.Kind == EventKinds.CarcassDecayed);
        Check(sink.Amount == Default[Deer].MeatYield && (sink.X, sink.Y) == (3, 2) && world.ReadCarcassMeat(3, 2) == 0, "decay removes all meat with an event");
    }

    private static void Ecosystem()
    {
        (string Hash, int Predation) RunOnce()
        {
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W, Seed = 2024, MapWidth = 64, MapHeight = 64, Species = Default,
                Vitals = VitalsConfig.Default with { HungerPerTick = 600 },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            var map = world.ReadMap();
            var land = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Where((_, k) => k % 9 == 0).Take(240).ToList();
            int[] mix = [Deer, Deer, Sheep, Rabbit, Rabbit, Deer, Sheep, Wolf];
            for (int k = 0; k < land.Count; k++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land[k] % 64, land[k] / 64, mix[k % mix.Length]));
            for (int t = 0; t < 2500; t++) world.Step();
            int predation = 0;
            for (var page = world.ReadHistory(0, 256); !page.Records.IsEmpty; page = world.ReadHistory(page.NextCursor, 256))
                predation += page.Records.Count(r => r.Reason == (int)DeathCause.Predation);
            return (world.StateHash(), predation);
        }
        var a = RunOnce();
        Check(a == RunOnce(), "same seed and script, same hash");
        Check(a.Predation > 0, "wolves killed prey in the mixed world");
    }
}
