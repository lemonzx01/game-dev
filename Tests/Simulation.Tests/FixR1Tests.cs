using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>Regression tests for FIX-R1 (Codex review findings 1–12 for T015–T017).</summary>
internal static class FixR1Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("FIX-R1 #1 unreachable buyer returns cargo, releases trader slot, bounds retries, resumes when opened", UnreachableBuyerResumesWhenOpened),
        ("FIX-R1 #2 long routes obey combined node budget, count expansions, serve locals, replay identically", LongRouteBudgetAndReplay),
        ("FIX-R1 #3 flooding empty farm plot or centre is rejected and work continues", FloodedPlotAndCentreRejected),
        ("FIX-R1 #4 guard promotion defers until carried load is deposited", PromotionDoesNotStrandCarriedLoad),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
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

    // ---- Finding 1 ------------------------------------------------------------------------------

    private static void UnreachableBuyerResumesWhenOpened()
    {
        // Enclose paying East buyer behind mountains; leave West seller and carrier together.
        var rows = Enumerable.Range(0, 20).Select(y =>
        {
            if (y is 0 or 19) return new string('^', 50);
            var line = new char[50];
            for (int x = 0; x < 50; x++) line[x] = x is 0 or 49 or 35 ? '^' : '.';
            return new string(line);
        }).ToList();
        var map = MapOverride.FromRows(rows);
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 101,
            MapWidth = 50,
            MapHeight = 20,
            Map = map,
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 10, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 42, 10, Faction.Demon, "East"));
        // 3 members in West (1 becomes guard, 2 carriers), 1 in East.
        for (int k = 0; k < 3; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 9 + k, 10, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 42, 10, Demon));
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        world.SetStorageForTest(2, food: 0, wood: 100, coins: 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;

        // Run long enough for initial trade attempt, return, and multiple potential retry cycles.
        var closedEvents = Run(world, 600, ref cursor);
        var loadsClosed = closedEvents.Where(e => e.Kind == EventKinds.TradeLoaded).ToList();
        var depositsClosed = closedEvents.Where(e => e.Kind == EventKinds.Deposited && e.Target == 1).ToList();
        var failedClosed = closedEvents.Where(e => e.Kind == EventKinds.PathFailed && e.Reason == (int)ActionKind.Work).ToList();
        Check(loadsClosed.Count >= 1 && failedClosed.Count >= 1, $"expected attempted outbound trade failure (loads={loadsClosed.Count}, pathFailed={failedClosed.Count})");
        Check(loadsClosed.Count <= 3, $"unreachable deal retried too often ({loadsClosed.Count} loads in 600 ticks)");
        Check(!closedEvents.Any(e => e.Kind is EventKinds.TradeSold or EventKinds.TradePaid), "zero sales and zero payments while enclosed");

        // Wait until any final in-progress return completes (or verify right after a brief settle window with no new load).
        var west = world.ReadSettlements()[0];
        var east = world.ReadSettlements()[1];
        Check(west.Food == 300, $"exact stock restoration expected 300, got {west.Food}");
        Check(west.Coins == 100 && east.Coins == 100, $"zero payment expected (west={west.Coins}, east={east.Coins})");
        Check(west.TradersOut == 0, $"zero outstanding traders expected, got {west.TradersOut}");
        Check(depositsClosed.Sum(e => e.Amount) == loadsClosed.Sum(e => e.Amount), "every loaded unit was returned to home storage");

        // Open a gap in the mountain wall and verify trade resumes.
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 35, 10, Terrain.Grass));
        var openEvents = Run(world, 1200, ref cursor);
        Check(openEvents.Any(e => e.Kind == EventKinds.TradeSold && e.Target == 2), "trade resumed after opening the mountain");
        Check(openEvents.Any(e => e.Kind == EventKinds.TradePaid && e.Actor == 2 && e.Target == 1), "payment occurred after opening the mountain");
    }

    // ---- Finding 2 ------------------------------------------------------------------------------

    private static void LongRouteBudgetAndReplay()
    {
        // Large 80x80 map with a distant inaccessible buyer enclosed behind mountains;
        // queue both long trade requests and local food requests under a strict per-tick budget.
        int w = 80, h = 80;
        var rows = Enumerable.Range(0, h).Select(y =>
        {
            if (y is 0 or 79) return new string('^', w);
            var line = new char[w];
            for (int x = 0; x < w; x++)
            {
                bool box = (x == 65 && y >= 65 && y <= 75) || (x == 75 && y >= 65 && y <= 75)
                        || (y == 65 && x >= 65 && x <= 75) || (y == 75 && x >= 65 && x <= 75);
                line[x] = x is 0 or 79 || box ? '^' : '.';
            }
            return new string(line);
        }).ToList();
        var foods = Enumerable.Range(0, 12).Select(k => new ResourcePlacement(5 + k * 2, 12, ResourceKind.Food, 20)).ToArray();
        var map = MapOverride.FromRows(rows, foods);
        var mind = MindConfig.Default with { FatiguePerTick = 0, SearchNodeBudget = 400, PathNodeBudgetPerTick = 1200 };
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 202,
            MapWidth = w,
            MapHeight = h,
            Map = map,
            Species = Default,
            Vitals = new VitalsConfig(50_000, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = mind,
            Settlements = SettlementConfig.Default with { FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
            Trade = TradeConfig.Default with { MaxTradersOut = 2, RouteSearchesPerTick = 2 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });

        // Spawn 20 rabbits first so they reach hunger >= EatStartNeed while settlers start well-fed.
        for (int k = 0; k < 20; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 6 + k % 16, 14, Rabbit));
        for (int t = 0; t < 5; t++) world.Step();

        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 10, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 70, 70, Faction.Demon, "IsolatedEast"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 70, 70, Demon));
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        world.SetStorageForTest(2, food: 0, wood: 100, coins: 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;

        // Capture checkpoint on the tick the long route runs (while local requests remain queued) and step both together.
        long maxTickExpansions = 0;
        long totalExpansions = 0;
        bool sawPathFailed = false;
        bool sawDeferredLocalBehindLong = false;
        SimulationWorld? checkpoint = null;
        long checkpointCursor = 0;
        var events = new List<WorldEvent>();

        for (int t = 0; t < 120; t++)
        {
            var batch = Run(world, 1, ref cursor);
            events.AddRange(batch);
            maxTickExpansions = Math.Max(maxTickExpansions, world.LastPathExpansions);
            totalExpansions += world.LastPathExpansions;
            Check(world.LastPathExpansions <= mind.PathNodeBudgetPerTick,
                $"tick {world.LastCompletedTick} expansions {world.LastPathExpansions} exceeded budget {mind.PathNodeBudgetPerTick}");

            bool longFailedThisTick = batch.Any(e => e.Kind == EventKinds.PathFailed && e.Reason == (int)ActionKind.Work);
            if (longFailedThisTick)
            {
                sawPathFailed = true;
                // A blocked distant buyer on an 80x80 map must hit the long-route expansion allotment (not 0 or small local-only count).
                Check(world.LastPathExpansions == mind.PathNodeBudgetPerTick,
                    $"long route PathFailed tick {world.LastCompletedTick} reported {world.LastPathExpansions} expansions, expected {mind.PathNodeBudgetPerTick}");
                if (world.PendingPathRequests > 0) sawDeferredLocalBehindLong = true;
            }

            if (checkpoint is null && longFailedThisTick && world.PendingPathRequests > 0)
            {
                using var ms = new MemoryStream();
                world.Save(ms);
                checkpoint = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
                checkpointCursor = cursor;
            }
            else if (checkpoint is not null)
            {
                Run(checkpoint, 1, ref checkpointCursor);
                Check(checkpoint.LastPathExpansions == world.LastPathExpansions,
                    $"checkpoint path expansions diverged at tick {world.LastCompletedTick}: {checkpoint.LastPathExpansions} vs {world.LastPathExpansions}");
                Check(checkpoint.StateHash() == world.StateHash(),
                    $"checkpoint state hash diverged at tick {world.LastCompletedTick}");
            }
        }

        Check(sawPathFailed, "long route to isolated buyer must fail under budget");
        Check(sawDeferredLocalBehindLong, "long route budget must defer remaining local requests to next tick");
        Check(maxTickExpansions > 0 && totalExpansions > 0, $"long route expansions must be recorded in LastPathExpansions (total={totalExpansions})");
        Check(checkpoint is not null, "expected checkpoint capture while local requests waited behind long route");
        Check(events.Any(e => e.Kind == EventKinds.EntityAte), "local food searches were eventually served");
    }

    // ---- Finding 3 ------------------------------------------------------------------------------

    private static void FloodedPlotAndCentreRejected()
    {
        var rows = Enumerable.Range(0, 24).Select(y => y is 0 or 23 ? new string('^', 24) : "^" + new string('.', 22) + "^").ToList();
        var map = MapOverride.FromRows(rows);
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 303,
            MapWidth = 24,
            MapHeight = 24,
            Map = map,
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 1, WoodTarget = 0, FoodTarget = 60, CropGrowTicks = 20 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });

        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 10, Faction.Human, "Meadow"));
        world.Step();
        var plots = world.ReadFarmPlots(1);
        Check(plots.Count == 1 && plots[0].Crop > 0, "town founded with one empty planted plot");
        var (px, py, _) = plots[0];
        long cursor = world.ReadEvents(0, 1).LatestSequence;

        // Both plot and centre have zero occupants; flooding either must be rejected.
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, px, py, Terrain.Water));
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 10, 10, Terrain.Water));
        var cmdEvents = Run(world, 1, ref cursor).Where(e => e.Kind is EventKinds.CommandApplied or EventKinds.CommandRejected).ToList();
        Check(cmdEvents.Count == 2 && cmdEvents.All(e => e.Kind == EventKinds.CommandRejected),
            $"expected both floods rejected, got: {string.Join(", ", cmdEvents.Select(e => $"{e.Kind}:{(CommandReason)e.Reason}"))}");
        Check(world.ReadMap().At(px, py) == Terrain.Grass && world.ReadMap().At(10, 10) == Terrain.Grass, "terrain unchanged");

        // Spawn workers and verify harvesting and delivery continue normally.
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 10, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 13, 10, Human));
        var workEvents = Run(world, 120, ref cursor);
        Check(workEvents.Any(e => e.Kind == EventKinds.Harvested && e.Target == 1), "harvesting continued");
        Check(workEvents.Any(e => e.Kind == EventKinds.Deposited && e.Target == 1), "delivery continued");
        Check(world.ReadSettlements()[0].Food > 0, "food stored in centre");
    }

    // ---- Finding 4 ------------------------------------------------------------------------------

    private static void PromotionDoesNotStrandCarriedLoad()
    {
        var rows = Enumerable.Range(0, 24).Select(y => y is 0 or 23 ? new string('^', 24) : "^" + new string('.', 22) + "^").ToList();
        var map = MapOverride.FromRows(rows);
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 404,
            MapWidth = 24,
            MapHeight = 24,
            Map = map,
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 1, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });

        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 10, Faction.Human, "Keep"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Human));
        world.Step();
        long cursor = world.ReadEvents(0, 1).LatestSequence;

        // Wait for one guard and for the other member to harvest crops.
        long worker = 0;
        long guard = 0;
        long harvestedAmount = 0;
        for (int t = 0; t < 100 && harvestedAmount == 0; t++)
        {
            var batch = Run(world, 1, ref cursor);
            var h = batch.FirstOrDefault(e => e.Kind == EventKinds.Harvested);
            if (h.Kind == EventKinds.Harvested)
            {
                worker = h.Actor;
                harvestedAmount = h.Amount;
                guard = world.ReadSnapshot().Chunks.SelectMany(c => c)
                    .Single(r => r.Id.Value != worker && world.ReadEntityDetails(W, r.Id)!.Job == Job.Guard).Id.Value;
            }
        }
        Check(harvestedAmount > 0 && worker != 0 && guard != 0, "worker harvested while guard active");

        // Remove the current guard right after the worker harvests, then step through the worker's next think and delivery.
        world.KillForTest(guard);
        var afterEvents = Run(world, 60, ref cursor);
        var deposits = afterEvents.Where(e => e.Kind == EventKinds.Deposited && e.Actor == worker).ToList();
        Check(deposits.Count == 1 && deposits[0].Amount == harvestedAmount,
            $"expected one deposit of {harvestedAmount}, got {deposits.Count} ({deposits.Sum(d => d.Amount)})");
        var details = world.ReadEntityDetails(W, new EntityId(worker))!;
        Check(details.CarryAmount == 0, $"no stranded cargo (carry={details.CarryAmount})");
        Check(details.Job == Job.Guard && world.ReadSettlements()[0].Guards == 1, $"worker eventually replaced guard (job={details.Job}, guards={world.ReadSettlements()[0].Guards})");
        Check(world.ReadSettlements()[0].Food == harvestedAmount, $"exact town food {harvestedAmount}, got {world.ReadSettlements()[0].Food}");
    }
}
