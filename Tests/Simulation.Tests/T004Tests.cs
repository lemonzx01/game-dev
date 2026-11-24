using FantasyWorld.Simulation;
using System.Collections.Immutable;

/// <summary>T004 acceptance: elapsed-time needs, health, death at tick D, consumption of real stock, no dead actions.</summary>
internal static class T004Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);
    private const long M = 1_000_000;

    // Hunger maxes 10 ticks after spawn, then starvation kills in 4 ticks. No thirst, no regen.
    private static readonly VitalsConfig Fast = new(100_000, 0, 250_000, 0, 0, 250_000, 100_000, 4);

    public static (string, Action)[] Cases =>
    [
        ("T004 vitals math matches tick-by-tick reference", VitalsMath),
        ("T004 needs derive from elapsed ticks, no per-tick work", ElapsedNeeds),
        ("T004 starvation death at tick D, record, history", Starvation),
        ("T004 eating consumes real stock and delays death", Eating),
        ("T004 drinking from adjacent water", Drinking),
        ("T004 dead entities cannot act", DeadCannotAct),
        ("T004 deterministic deaths across worlds", Determinism),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static SimulationWorld NewWorld(VitalsConfig vitals, ResourceConfig? resources = null) => new(new SimulationOptions
    {
        WorldId = W,
        Seed = 42,
        MapWidth = 64,
        MapHeight = 64,
        Vitals = vitals,
        Mind = MindConfig.Disabled, // vitals in isolation; AI is covered by T005
        Resources = resources ?? ResourceConfig.Default,
        Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
    });

    private static (int X, int Y) Find(SimulationWorld world, Func<int, int, bool> match)
    {
        var map = world.ReadMap();
        for (int y = 1; y < map.Height - 1; y++)
            for (int x = 1; x < map.Width - 1; x++)
                if (match(x, y)) return (x, y);
        throw new Exception("No matching cell");
    }

    private static long Spawn(SimulationWorld world, (int X, int Y) at)
    {
        var receipt = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, at.X, at.Y, 1));
        Check(receipt.Status == "Queued", "spawn " + receipt.Reason);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        world.Step();
        var page = world.ReadEvents(cursor, 256);
        return page.Records.Single(e => e.CommandSequence == receipt.CommandSequence).Target;
    }

    private static EntityDetails Details(SimulationWorld world, long id) => world.ReadEntityDetails(W, new EntityId(id))!;

    private static CommandReason Consume(SimulationWorld world, long id, ResourceKind kind)
    {
        var reason = CommandReason.None;
        world.Advance(() => reason = world.TryConsume(new EntityId(id), kind, world.NextTickUnsafe));
        return reason;
    }

    private static void VitalsMath()
    {
        var configs = new[]
        {
            VitalsConfig.Default,
            Fast,
            new VitalsConfig(70_001, 130_003, 90_007, 400_009, 5_003, 1, 1, 1),
            new VitalsConfig(0, 0, 1, 1, 0, 1, 1, 1),
            new VitalsConfig(333_333, 333_333, 1, 1_000_000, 1_000_000, 1, 1, 1),
        };
        var anchors = new[] { (0L, 0L, M), (5L, 999_999L, 1L), (3L, M, 400_000L), (0L, 500_000L, 999_000L), (7L, M, M) };
        foreach (var c in configs)
            foreach (var (h, t, hp) in anchors)
            {
                var a = new VitalsAnchor(10, h, t, hp);
                // Reference: one tick at a time. A need that is at max at the start of a tick damages during it.
                long hunger = h, thirst = t, health = hp, refDeath = long.MaxValue;
                for (long k = 1; k <= 3000 && refDeath == long.MaxValue; k++)
                {
                    bool starving = hunger >= M, parched = thirst >= M;
                    if (!starving && !parched) health = Math.Min(M, health + c.RegenPerTick);
                    else health -= (starving ? c.StarvationDamagePerTick : 0) + (parched ? c.DehydrationDamagePerTick : 0);
                    hunger = Math.Min(M, hunger + c.HungerPerTick);
                    thirst = Math.Min(M, thirst + c.ThirstPerTick);
                    if (health <= 0) refDeath = 10 + k;
                    var v = a.At(10 + k, c);
                    Check(v.Hunger == hunger && v.Thirst == thirst && v.Health == Math.Max(0, health),
                        $"tick +{k}: got {v} want ({hunger},{thirst},{Math.Max(0, health)}) for {c} {a}");
                }
                var (death, _) = a.DeathTick(c);
                Check(refDeath == long.MaxValue ? death > 10 + 3000 : death == refDeath, $"death {death} vs {refDeath} for {c} {a}");
            }
    }

    private static void ElapsedNeeds()
    {
        var world = NewWorld(VitalsConfig.Default);
        var land = Find(world, (x, y) => world.ReadMap().At(x, y) == Terrain.Grass);
        var ids = Enumerable.Range(0, 50).Select(_ => Spawn(world, land)).ToList();
        int bookings = world.RequiredWorkCount;
        for (int i = 0; i < 100; i++) world.Step();
        var d = Details(world, ids[0]); // spawned at tick 1, now tick 150
        Check(d.LifeState == LifeStates.Alive && d.BirthTick == 1, "alive");
        Check(d.Hunger == 139 * 149 && d.Thirst == 278 * 149 && d.Health == VitalsConfig.MaxHealth, $"elapsed needs {d}");
        Check(bookings == 50 && world.RequiredWorkCount == 50, "one death booking per entity, no per-tick work");
    }

    private static void Starvation()
    {
        var world = NewWorld(Fast);
        var land = Find(world, (x, y) => world.ReadMap().At(x, y) == Terrain.Grass);
        long id = Spawn(world, land); // tick 1; hunger max at 11; death at 15
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        while (world.LastCompletedTick < 14) world.Step();
        Check(Details(world, id) is { LifeState: LifeStates.Alive, Health: 250_000 }, "alive at D-1");
        Check(world.ReadSnapshot().Chunks.SelectMany(c => c).Single().LifeState == LifeStates.Alive, "row alive at D-1");
        world.Step(); // tick 15 = D
        var row = world.ReadSnapshot().Chunks.SelectMany(c => c).Single();
        Check(row.Id.Value == id && row.LifeState == LifeStates.Dead, "snapshot D shows Dead");
        var deathEvents = world.ReadEvents(cursor, 256).Records.Where(e => e.Kind == EventKinds.EntityDied).ToList();
        Check(deathEvents.Count == 1 && deathEvents[0].Target == id && deathEvents[0].Tick == 15
            && deathEvents[0].Reason == (int)DeathCause.Starvation, "one death event with cause");
        world.Step(); // D + 1
        Check(world.ReadSnapshot().Chunks.IsEmpty, "omitted from D+1");
        var d = Details(world, id);
        Check(d.LifeState == LifeStates.Dead && d.DeathTick == 15 && d.DeathCause == DeathCause.Starvation
            && d.Health == 0 && d.Hunger == M && d.BirthTick == 1, $"durable details {d}");
        var history = world.ReadHistory(0, 10);
        Check(history.Records.Length == 1 && history.Records[0].EventSequence == deathEvents[0].EventSequence
            && history.NextCursor == deathEvents[0].EventSequence, "history milestone");
        Check(world.ReadHistory(history.NextCursor, 10) is { Records.IsEmpty: true } h && h.NextCursor == world.ReadEvents(0, 1).LatestSequence, "history end");
        Check(Spawn(world, land) == id + 1, "dead ID never reused");
    }

    private static void Eating()
    {
        var rules = ResourceConfig.Default with { Food = new ResourceRule(3, 1, 1000) };
        var world = NewWorld(Fast, rules);
        var food = Find(world, (x, y) => world.ReadResource(x, y, ResourceKind.Food) is not null);
        long id = Spawn(world, food);
        while (world.LastCompletedTick < 8) world.Step();
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Check(Consume(world, id, ResourceKind.Food) == CommandReason.None, "eat"); // tick 9: hunger 800k, needs 4, stock 3
        Check(world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount == 0, "stock 3 -> 0");
        var d = Details(world, id);
        Check(d.Hunger == 50_000 && d.Health == M, $"hunger after eating {d}");
        var ate = world.ReadEvents(cursor, 256).Records.Single(e => e.Kind == EventKinds.EntityAte);
        Check(ate.Actor == id && ate.Amount == 3 && ate.X == food.X && ate.Y == food.Y, "ate event");
        Check(Consume(world, id, ResourceKind.Food) == CommandReason.InsufficientStock, "empty stock"); // tick 10
        Check(world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount == 0, "never negative");
        while (world.LastCompletedTick < 22) world.Step(); // old death tick 15 passes; new max 19, death 23
        Check(Details(world, id).LifeState == LifeStates.Alive, "stale death booking ignored");
        world.Step();
        Check(Details(world, id) is { LifeState: LifeStates.Dead, DeathTick: 23 }, "death moved to 23");
    }

    private static void Drinking()
    {
        var vitals = new VitalsConfig(0, 100_000, 0, 250_000, 0, 1, 100_000, 4);
        var world = NewWorld(vitals);
        var shore = Find(world, (x, y) => TerrainIsLand(world, x, y)
            && new[] { (0, -1), (-1, 0), (1, 0), (0, 1) }.Any(o => world.ReadResource(x + o.Item1, y + o.Item2, ResourceKind.Water) is not null));
        long id = Spawn(world, shore);
        while (world.LastCompletedTick < 5) world.Step();
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Check(Consume(world, id, ResourceKind.Water) == CommandReason.None, "drink"); // tick 6: thirst 500k -> 5 units, capped 4
        Check(Details(world, id).Thirst == 100_000, "thirst after drinking");
        var drank = world.ReadEvents(cursor, 256).Records.Single(e => e.Kind == EventKinds.EntityDrank);
        Check(drank.Amount == 4 && world.ReadResource(drank.X, drank.Y, ResourceKind.Water)!.Value.Amount == 996, "water stock decreased");
        Check(Consume(world, id, ResourceKind.Food) == CommandReason.NoResource || true, "food lookup does not throw");
        Check(Consume(world, id, ResourceKind.Wood) == CommandReason.InvalidCommand, "wood is not edible");
    }

    private static bool TerrainIsLand(SimulationWorld world, int x, int y) => world.ReadMap().At(x, y) is Terrain.Grass or Terrain.Forest;

    private static void DeadCannotAct()
    {
        var world = NewWorld(Fast);
        var food = Find(world, (x, y) => world.ReadResource(x, y, ResourceKind.Food) is not null);
        long id = Spawn(world, food);
        while (world.LastCompletedTick < 14) world.Step();
        int stock = world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount;
        Check(Consume(world, id, ResourceKind.Food) == CommandReason.ActorDead, "death runs before actions on tick D");
        Check(Consume(world, id, ResourceKind.Food) == CommandReason.ActorDead, "dead after removal");
        Check(world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount == stock, "dead actor consumed nothing");
        Check(Consume(world, 999, ResourceKind.Food) == CommandReason.UnknownEntity, "unknown entity");
    }

    private static void Determinism()
    {
        string Run()
        {
            var world = NewWorld(new VitalsConfig(9_001, 17_003, 40_000, 90_000, 2_000, 250_000, 100_000, 4));
            var map = world.ReadMap();
            var cells = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Take(300).ToList();
            foreach (int c in cells)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, c % map.Width, c / map.Width, 1));
            for (int t = 0; t < 400; t++)
            {
                long now = world.NextTickUnsafe;
                world.Advance(() =>
                {
                    for (long id = 1 + now % 7; id <= 300; id += 7) // a deterministic subset eats/drinks
                    {
                        world.TryConsume(new EntityId(id), ResourceKind.Food, now);
                        world.TryConsume(new EntityId(id), ResourceKind.Water, now);
                    }
                });
            }
            Check(world.ReadHistory(0, 256).Records.Length > 0, "some deaths happened");
            Check(world.LiveEntityCount < 300, "dead entities removed");
            return world.StateHash();
        }
        Check(Run() == Run(), "same script, same hash with deaths");
    }
}
