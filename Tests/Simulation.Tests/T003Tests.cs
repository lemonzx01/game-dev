using FantasyWorld.Simulation;
using System.Collections.Immutable;

/// <summary>T003 acceptance: clock/calendar, deterministic map, resources, commands + authority (N4), scheduler, N1, host pause/step.</summary>
internal static class T003Tests
{
    private const long God = 7, Watcher = 8, Stranger = 9;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T003 calendar derives from integer tick", Calendar),
        ("T003 deterministic map and resources", Map),
        ("T003 command admission, sequence and queue capacity", Admission),
        ("T003 N4 authority comes only from session grants", Authority),
        ("T003 H3 ordered execution sees earlier writes", OrderedStock),
        ("T003 spawn, snapshot chunks and entity details", Spawn),
        ("T003 scheduler order, budget carry-forward and regrowth", Scheduling),
        ("T003 N1 command outcome survives event flood", EventFlood),
        ("T003 host clock pause, single-step and speed", HostPacing),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static readonly ClimateConfig Neutral = ClimateConfig.Default with { SpringGrowthPercent = 100, AutumnGrowthPercent = 100, WinterGrowthPercent = 100, MinGrowthTemperature = -100 };

    private static SimulationWorld NewWorld(ulong seed = 42, int size = 64, Func<SimulationOptions, SimulationOptions>? tweak = null)
    {
        var options = new SimulationOptions
        {
            WorldId = W,
            Seed = seed,
            MapWidth = size,
            MapHeight = size,
            Mind = MindConfig.Disabled, // T003 checks commands/map only; AI is covered by T005
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty
                .Add(God, AuthorityGrant.God | AuthorityGrant.Observe)
                .Add(Watcher, AuthorityGrant.Observe),
        };
        return new SimulationWorld(tweak is null ? options : tweak(options));
    }

    private static (int X, int Y) FindCell(SimulationWorld world, Func<int, int, bool> match)
    {
        var map = world.ReadMap();
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (match(x, y)) return (x, y);
        throw new Exception("No matching cell");
    }

    private static List<WorldEvent> Drain(SimulationWorld world, ref long cursor)
    {
        var all = new List<WorldEvent>();
        while (true)
        {
            var page = world.ReadEvents(cursor, 256);
            Check(page.Status == PageStatus.Ok, $"event page {page.Status}");
            all.AddRange(page.Records);
            if (page.NextCursor == cursor) return all;
            cursor = page.NextCursor;
        }
    }

    private static void Calendar()
    {
        var c = new CalendarConfig(10, 3);
        var t0 = WorldTime.From(0, c);
        Check(t0 == new WorldTime(0, 0, 0, 0, Season.Spring, 0), "tick 0");
        var t = WorldTime.From(10 * 3 * 4 + 10 * 3 + 10 + 7, c); // year 1, summer, day 1, tick 7
        Check(t.Year == 1 && t.Season == Season.Summer && t.DayOfSeason == 1 && t.TickOfDay == 7 && t.Day == 16, "year rollover");
        var world = NewWorld(tweak: o => o with { Calendar = c });
        for (int i = 0; i < 31; i++) world.Step();
        var snap = world.ReadSnapshot();
        Check(snap.Time.Tick == 31 && snap.Time.Season == Season.Summer && snap.Time.DayOfSeason == 0, "snapshot time");
    }

    private static void Map()
    {
        var a = NewWorld(42, 128);
        var b = NewWorld(42, 128);
        var c = NewWorld(43, 128);
        Check(a.ReadMap().Cells.SequenceEqual(b.ReadMap().Cells), "same seed, same map");
        Check(!a.ReadMap().Cells.SequenceEqual(c.ReadMap().Cells), "seed had no effect on map");
        Check(a.StateHash() == b.StateHash(), "same seed, same initial hash");
        var counts = a.ReadMap().Cells.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        foreach (var t in Enum.GetValues<Terrain>())
            Check(counts.GetValueOrDefault(t) > 0, $"missing terrain {t}");
        Check(counts[Terrain.Water] * 100 / (128 * 128) is >= 20 and <= 30, "water share");

        var map = a.ReadMap();
        int nodes = 0;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                foreach (var kind in Enum.GetValues<ResourceKind>())
                {
                    if (a.ReadResource(x, y, kind) is not { } r) continue;
                    nodes++;
                    Check(r.Amount == r.Capacity && r.Amount > 0, "nodes start full and positive");
                    var t = map.At(x, y);
                    Check(kind switch
                    {
                        ResourceKind.Water => t == Terrain.Water,
                        ResourceKind.Wood => t == Terrain.Forest,
                        _ => t is Terrain.Grass or Terrain.Forest,
                    }, $"{kind} on {t}");
                }
        Check(nodes == a.ResourceNodeCount && nodes > 100, "resource node count");
        Check(a.ReadResource(-1, 0, ResourceKind.Food) is null && a.ReadResource(128, 0, ResourceKind.Food) is null, "bounds");
    }

    private static void Admission()
    {
        var world = NewWorld(tweak: o => o with { CommandQueueCapacity = 2 });
        var land = FindCell(world, (x, y) => world.ReadMap().At(x, y) == Terrain.Grass);
        world.Step();
        string before = world.StateHash();
        Check(world.SubmitCommand(new SpawnEntityCommand(new WorldId(2), God, CommandModes.God, land.X, land.Y, 1)).Reason == "InvalidWorld", "world");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, God, "Admin", land.X, land.Y, 1)).Reason == "InvalidMode", "mode");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, -1, 0, 1)).Reason == "OutOfBounds", "bounds");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 0)).Reason == "InvalidCommand", "species");
        Check(world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, 0, 0, ResourceKind.Food, 0)).Reason == "InvalidAmount", "amount");
        Check(world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, 0, 0, (ResourceKind)77, 1)).Reason == "InvalidCommand", "kind");
        Check(world.SubmitCommand(null!).Reason == "InvalidCommand", "null");
        Check(world.StateHash() == before, "rejections changed state");

        var r1 = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 1));
        var r2 = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 2));
        Check(r1 == new CommandReceipt("Queued", "", 2, 1) && r2 == new CommandReceipt("Queued", "", 2, 2), "target tick and sequence");
        string full = world.StateHash();
        Check(world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 3)).Reason == "QueueFull", "queue full");
        Check(world.StateHash() == full, "full queue consumed a sequence");
        world.Step();
        var r3 = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 3));
        Check(r3.CommandSequence == 3 && r3.TargetTick == 3, "sequence continues after rejection");
    }

    private static void Authority()
    {
        var world = NewWorld();
        var land = FindCell(world, (x, y) => world.ReadMap().At(x, y) == Terrain.Grass);
        string before = world.StateHash();
        Check(world.SubmitCommand(new SpawnEntityCommand(W, Watcher, CommandModes.Observer, land.X, land.Y, 1)).Reason == "Unauthorized", "observer mutate");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, Watcher, CommandModes.God, land.X, land.Y, 1)).Reason == "Unauthorized", "observer self-grant");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, Stranger, CommandModes.God, land.X, land.Y, 1)).Reason == "Unauthorized", "no grant");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.Observer, land.X, land.Y, 1)).Reason == "Unauthorized", "god in observer mode");
        Check(world.StateHash() == before, "unauthorized changed state");
        var ungranted = new SimulationWorld(W, 42); // T002 constructor: no grants at all
        Check(ungranted.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 1)).Reason == "Unauthorized", "default no grant");
        Check(world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 1)).Status == "Queued", "god spawn");
        Check(world.ReadEntityDetails(new WorldId(2), new EntityId(1)) is null, "other world read");
    }

    private static void OrderedStock()
    {
        var world = NewWorld();
        var food = FindCell(world, (x, y) => world.ReadResource(x, y, ResourceKind.Food) is not null);
        int stock = world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount;
        // Leave exactly 1 unit, then two commands each taking 1 before a single Step.
        Check(world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, food.X, food.Y, ResourceKind.Food, stock - 1)).Status == "Queued", "drain");
        world.Step();
        Check(world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount == 1, "stock=1");
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var a = world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, food.X, food.Y, ResourceKind.Food, 1));
        var b = world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, food.X, food.Y, ResourceKind.Food, 1));
        var water = FindCell(world, (x, y) => world.ReadMap().At(x, y) == Terrain.Water);
        var c = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, water.X, water.Y, 1));
        var d = world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, water.X, water.Y, ResourceKind.Wood, 1));
        world.Step();
        var outcomes = Drain(world, ref cursor);
        Check(outcomes.Count == 4, $"exactly one outcome per command, got {outcomes.Count}");
        Check(outcomes[0].CommandSequence == a.CommandSequence && outcomes[0].Kind == EventKinds.CommandApplied && outcomes[0].Amount == 1, "first applied");
        Check(outcomes[1].CommandSequence == b.CommandSequence && outcomes[1].Kind == EventKinds.CommandRejected
            && outcomes[1].Reason == (int)CommandReason.InsufficientStock, "second rejected");
        Check(outcomes[2].CommandSequence == c.CommandSequence && outcomes[2].Reason == (int)CommandReason.Impassable, "execution target check");
        Check(outcomes[3].CommandSequence == d.CommandSequence && outcomes[3].Reason == (int)CommandReason.NoResource, "missing node");
        Check(outcomes.All(e => e.Tick == 2 && e.Actor == God), "event tick/actor");
        Check(world.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount == 0, "stock=0, never negative");
    }

    private static void Spawn()
    {
        var world = NewWorld();
        var land = FindCell(world, (x, y) => world.ReadMap().At(x, y) == Terrain.Grass);
        var empty = world.ReadSnapshot();
        long cursor = 0;
        int total = 0;
        for (int round = 0; round < 3; round++) // 3 x 1024 = 3072 entities > one 2048-row chunk
        {
            for (int i = 0; i < 1024; i++)
                Check(world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land.X, land.Y, 1 + i % 5)).Status == "Queued", "spawn queued");
            world.Step();
            total += 1024;
            var outcomes = Drain(world, ref cursor);
            Check(outcomes.Count == 1024 && outcomes.All(e => e.Kind == EventKinds.CommandApplied), "spawn outcomes");
        }
        var snap = world.ReadSnapshot();
        Check(empty.Chunks.IsEmpty && empty.Tick == 0, "old snapshot unchanged");
        var rows = snap.Chunks.SelectMany(c => c).ToList();
        Check(rows.Count == total && snap.Chunks.Length == 2 && snap.Chunks.All(c => c.Length * 28 + 64 < 85_000), "chunk bound");
        Check(rows.Select(r => r.Id.Value).SequenceEqual(Enumerable.Range(1, total).Select(i => (long)i)), "monotonic IDs from 1");
        Check(rows.All(r => r.LifeState == LifeStates.Alive && r.X == land.X && r.Y == land.Y), "row data");
        Check(world.ReadEntityDetails(W, new EntityId(total)) is { } d && d.EntityId.Value == total, "details");
        Check(world.ReadEntityDetails(W, new EntityId(total + 1)) is null, "unknown id");
    }

    private static void Scheduling()
    {
        // Pure scheduler: order is (due, priority, entity, ordinal) regardless of insertion order.
        var s = new Scheduler(2);
        var order = new List<long>();
        s.Schedule(5, 20, 9, new WorkItem(WorkKind.TestNoop, 1), false);
        s.Schedule(5, 20, 3, new WorkItem(WorkKind.TestNoop, 2), false);
        s.Schedule(4, 30, 1, new WorkItem(WorkKind.TestNoop, 3), false);
        s.Schedule(5, 10, 9, new WorkItem(WorkKind.TestNoop, 4), false);
        s.Schedule(5, 20, 3, new WorkItem(WorkKind.TestNoop, 5), false);
        s.Schedule(6, 0, 0, new WorkItem(WorkKind.TestNoop, 6), true);
        s.Schedule(5, 99, 0, new WorkItem(WorkKind.TestNoop, 7), true);
        Check(s.Run(5, w => { order.Add(w.Subject); return true; }) == 2, "budget");
        Check(order.SequenceEqual([7L, 3, 4]), $"order {string.Join(",", order)}");
        Check(s.Run(6, w => { order.Add(w.Subject); return true; }) == 2, "carry");
        Check(order.SequenceEqual([7L, 3, 4, 6, 2, 5]), "required first, remainder carried in order");
        s.Run(7, w => { order.Add(w.Subject); return true; });
        Check(order[^1] == 1 && s.DiscretionaryCount == 0, "last carried item");
        bool threw = false;
        try { s.Schedule(7, 0, 0, new WorkItem(WorkKind.TestNoop, 0), true); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "past work rejected");

        // Through the world: budget limits discretionary work per tick and the remainder carries forward.
        var world = NewWorld(tweak: o => o with { DiscretionaryWorkBudget = 3 });
        for (int i = 0; i < 7; i++) world.ScheduleForTest(1, 20, i, isRequired: false);
        world.Step();
        Check(world.PendingDiscretionaryWork == 4, "3 run at tick 1");
        world.Step();
        world.Step();
        Check(world.PendingDiscretionaryWork == 0, "remainder drained");

        // Regrowth: 1 unit per interval, never above capacity, schedule clears when full.
        var rules = ResourceConfig.Default with { Food = new ResourceRule(5, 2, 4) };
        var grow = NewWorld(tweak: o => o with { Resources = rules, Climate = Neutral }); // flat growth; seasons are T012
        var food = FindCell(grow, (x, y) => grow.ReadResource(x, y, ResourceKind.Food) is not null);
        grow.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, food.X, food.Y, ResourceKind.Food, 5));
        grow.Step(); // tick 1: stock 0, regrow due tick 5
        int Amount() => grow.ReadResource(food.X, food.Y, ResourceKind.Food)!.Value.Amount;
        for (int t = 2; t <= 4; t++) { grow.Step(); Check(Amount() == 0, $"no early regrowth t{t}"); }
        grow.Step(); Check(Amount() == 2, "t5");
        for (int t = 6; t <= 9; t++) grow.Step();
        Check(Amount() == 4, "t9");
        for (int t = 10; t <= 13; t++) grow.Step();
        Check(Amount() == 5, "capped at capacity");
        string full = grow.StateHash();
        for (int t = 14; t <= 30; t++) grow.Step();
        Check(Amount() == 5, "stays full");
        Check(grow.StateHash() != full, "tick advanced");

        // Regression (review of 48456c6): regrowth near int.MaxValue must clamp, not wrap negative.
        var huge = ResourceConfig.Default with { Food = new ResourceRule(int.MaxValue, int.MaxValue, 1) };
        var big = NewWorld(tweak: o => o with { Resources = huge });
        var bigFood = FindCell(big, (x, y) => big.ReadResource(x, y, ResourceKind.Food) is not null);
        big.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, bigFood.X, bigFood.Y, ResourceKind.Food, 1));
        big.Step(); // stock int.MaxValue - 1, regrow due tick 2
        big.Step();
        Check(big.ReadResource(bigFood.X, bigFood.Y, ResourceKind.Food)!.Value.Amount == int.MaxValue, "regrowth overflow");
    }

    private static void EventFlood()
    {
        var world = NewWorld(); // default capacity 4096, per-tick cap 2048
        var food = FindCell(world, (x, y) => world.ReadResource(x, y, ResourceKind.Food) is not null);
        long cursor = 0;
        for (int round = 0; round < 3; round++)
        {
            var receipt = world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, food.X, food.Y, ResourceKind.Food, 1));
            world.Advance(() => { for (int i = 0; i < 5000; i++) world.Emit(new WorldEvent(0, world.LastCompletedTickUnsafe + 1, 1)); });
            var tickEvents = Drain(world, ref cursor);
            Check(tickEvents.Any(e => e.CommandSequence == receipt.CommandSequence && e.Kind == EventKinds.CommandApplied), "outcome lost");
            var summary = tickEvents[^1];
            Check(summary.Kind == EventKinds.EventsDropped && summary.Amount == 5000 - 2046, $"drop summary {summary.Amount}");
            Check(tickEvents.Count == 2048, $"per-tick cap {tickEvents.Count}");
        }
        bool threw = false;
        try { NewWorld(tweak: o => o with { EventCapacity = 100, MaxEventsPerTick = 100, CommandQueueCapacity = 100 }); }
        catch (ArgumentException) { threw = true; }
        Check(threw, "capacity validation");
    }

    private static void HostPacing()
    {
        var clock = new HostClock(ticksPerSecond: 10, maxTicksPerUpdate: 8, maxBacklogTicks: 20);
        Check(clock.Update(0.1) == 1 && clock.Update(0.25) == 2 && clock.Backlog == 0, "1x pacing");
        clock.Paused = true;
        Check(clock.Update(5) == 0, "paused");
        clock.RequestSingleStep();
        clock.RequestSingleStep();
        Check(clock.Update(0) == 1 && clock.Update(0) == 1 && clock.Update(0) == 0, "single steps");
        clock.Paused = false;
        clock.Speed = 4;
        Check(clock.Update(0.1) == 4, "speed 4x");
        Check(clock.Update(10) == 8 && clock.Overloaded && clock.Backlog == 12, "overload caps and reports backlog");
        Check(clock.Update(0) == 8 && clock.Update(0) == 4 && clock.Update(0) == 0, "backlog drains, no ticks skipped from what was admitted");

        // Paused host performs no Steps, so the world tick does not move.
        var world = NewWorld();
        clock.Paused = true;
        for (int i = 0; i < clock.Update(1); i++) world.Step();
        Check(world.ReadSnapshot().Tick == 0, "paused world");
    }
}
