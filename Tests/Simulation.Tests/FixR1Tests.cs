using FantasyWorld.Simulation;
using System.Collections.Immutable;
using System.Security.Cryptography;
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
        ("FIX-R1 #5 max CarryCapacity with skill bonus does not overflow negative", CarryCapacityNoSignedOverflow),
        ("FIX-R1 #6 scholarship XP bonus rejects extreme tuning and saturates at long.MaxValue", ScholarshipBonusXpNoOverflow),
        ("FIX-R1 #7 invalid crop scheduler fixtures (missing plot, duplicate timer, missing timer) are rejected", CropSchedulerInvariantsOnLoad),
        ("FIX-R1 #8 settlement, trade, carry, reading save invariants and positive controls", SettlementTradeReadingInvariantsOnLoad),
        ("FIX-R1 #9 partial trade sale reconciles both towns, payments, unsold return, and slot", PartialSaleReconcilesBothTownsAndCargo),
        ("FIX-R1 #10 ReadSettlements caches one linear aggregation per tick and updates after promotion/death", ReadSettlementsLinearAggregationAndCache),
        ("FIX-R1 #11 TradeDeal shares lookup per economic revision with nearest and ascending-ID tie-break", TradeDealCachedPerRevision),
        ("FIX-R1 #12 founding excludes grass tiles beyond Manhattan distance 3", FoundingSkipsPlotsBeyondManhattan3),
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

    // ---- Finding 5 ------------------------------------------------------------------------------

    private static void CarryCapacityNoSignedOverflow()
    {
        // Part A: farm plot with 5 units, CarryCapacity = int.MaxValue, Farming level 2.
        var rows = Enumerable.Range(0, 16).Select(y => y is 0 or 15 ? new string('^', 16) : "^" + new string('.', 14) + "^").ToList();
        var plotWorld = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 505,
            MapWidth = 16,
            MapHeight = 16,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { CarryCapacity = int.MaxValue, FarmPlots = 1, CropMax = 10, CropGrowTicks = 1_000_000, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        plotWorld.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 8, 8, Faction.Human, "Farms"));
        plotWorld.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        plotWorld.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        plotWorld.Step();
        foreach (var row in plotWorld.ReadSnapshot().Chunks.SelectMany(c => c))
            plotWorld.SetSkillForTest(row.Id.Value, Skill.Farming, 400); // level 2
        long cursorA = plotWorld.ReadEvents(0, 1).LatestSequence;
        var eventsA = new List<WorldEvent>();
        for (int t = 0; t < 120 && !eventsA.Any(e => e.Kind == EventKinds.Harvested); t++)
        {
            eventsA.AddRange(Run(plotWorld, 1, ref cursorA));
        }
        var harvest = eventsA.FirstOrDefault(e => e.Kind == EventKinds.Harvested);
        Check(harvest.Kind == EventKinds.Harvested && harvest.Amount == 5, $"expected 5 harvested from plot, got {harvest.Amount}");
        Check(plotWorld.ReadFarmPlots(1)[0].Crop == 0, $"expected zero crop remainder, got {plotWorld.ReadFarmPlots(1)[0].Crop}");
        Check(plotWorld.ReadEntityDetails(W, new EntityId(harvest.Actor))!.CarryAmount >= 0, "nonnegative cargo after plot harvest");
        eventsA.AddRange(Run(plotWorld, 60, ref cursorA));
        Check(plotWorld.ReadSettlements()[0].Food == 5, $"expected 5 food delivered, got {plotWorld.ReadSettlements()[0].Food}");

        // Part B: wild food node with 5 units, CarryCapacity = int.MaxValue, Foraging level 2.
        var nodeWorld = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 506,
            MapWidth = 16,
            MapHeight = 16,
            Map = MapOverride.FromRows(rows, new ResourcePlacement(10, 8, ResourceKind.Food, 5)),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { CarryCapacity = int.MaxValue, FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
            Resources = ResourceConfig.Default with { Food = new ResourceRule(20, 0, 1000) },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        nodeWorld.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 8, 8, Faction.Human, "Wilds"));
        nodeWorld.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        nodeWorld.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        nodeWorld.Step();
        foreach (var row in nodeWorld.ReadSnapshot().Chunks.SelectMany(c => c))
            nodeWorld.SetSkillForTest(row.Id.Value, Skill.Foraging, 400); // level 2
        long cursorB = nodeWorld.ReadEvents(0, 1).LatestSequence;
        var eventsB = new List<WorldEvent>();
        for (int t = 0; t < 120 && !eventsB.Any(e => e.Kind == EventKinds.Gathered); t++)
        {
            eventsB.AddRange(Run(nodeWorld, 1, ref cursorB));
        }
        var gather = eventsB.FirstOrDefault(e => e.Kind == EventKinds.Gathered);
        Check(gather.Kind == EventKinds.Gathered && gather.Amount == 5, $"expected 5 gathered from node, got {gather.Amount}");
        Check(nodeWorld.ReadResource(10, 8, ResourceKind.Food)!.Value.Amount == 0, $"expected zero node remainder, got {nodeWorld.ReadResource(10, 8, ResourceKind.Food)!.Value.Amount}");
        Check(nodeWorld.ReadEntityDetails(W, new EntityId(gather.Actor))!.CarryAmount >= 0, "nonnegative cargo after node gather");
        eventsB.AddRange(Run(nodeWorld, 60, ref cursorB));
        Check(nodeWorld.ReadSettlements()[0].Food == 5, $"expected 5 food delivered from node, got {nodeWorld.ReadSettlements()[0].Food}");
    }

    // ---- Finding 6 ------------------------------------------------------------------------------

    private static void ScholarshipBonusXpNoOverflow()
    {
        // Reject unsupported ScholarBonusPercentPerLevel (above cap and int.MaxValue).
        foreach (int invalid in new[] { SkillConfig.MaxScholarBonusPercentPerLevel + 1, int.MaxValue })
        {
            bool rejected = false;
            try
            {
                _ = new SimulationWorld(new SimulationOptions
                {
                    WorldId = W,
                    Seed = 606,
                    Skills = SkillConfig.Default with { ScholarBonusPercentPerLevel = invalid },
                });
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            Check(rejected, $"ScholarBonusPercentPerLevel={invalid} must be rejected");
        }

        // Exercise largest accepted bonus and Farming XP near long.MaxValue; XP must saturate monotonically and stay loadable.
        var rows = Enumerable.Range(0, 16).Select(y => y is 0 or 15 ? new string('^', 16) : "^" + new string('.', 14) + "^").ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 607,
            MapWidth = 16,
            MapHeight = 16,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 1, WoodTarget = 0, FoodTarget = 60 },
            Skills = SkillConfig.Default with { ScholarBonusPercentPerLevel = SkillConfig.MaxScholarBonusPercentPerLevel },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 8, 8, Faction.Human, "Academy"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        world.Step();
        foreach (var row in world.ReadSnapshot().Chunks.SelectMany(c => c))
        {
            world.SetSkillForTest(row.Id.Value, Skill.Scholarship, 10_000); // max level 10
            world.SetSkillForTest(row.Id.Value, Skill.Farming, long.MaxValue - 5);
        }
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 80, ref cursor);
        var harvest = events.FirstOrDefault(e => e.Kind == EventKinds.Harvested);
        Check(harvest.Kind == EventKinds.Harvested, "expected harvest XP trigger");
        var skills = world.ReadSkills(W, new EntityId(harvest.Actor))!;
        Check(skills.Farming == long.MaxValue, $"XP should saturate cleanly at long.MaxValue, got {skills.Farming}");

        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        Check(loaded.StateHash() == world.StateHash(), "saturated XP checkpoint loads cleanly");
        Check(loaded.ReadSkills(W, new EntityId(harvest.Actor))!.Farming == long.MaxValue, "loaded XP preserved");
    }

    // ---- Finding 7 ------------------------------------------------------------------------------

    private static byte[] SaveBytes(SimulationWorld world)
    {
        using var ms = new MemoryStream();
        world.Save(ms);
        return ms.ToArray();
    }

    private static void RequireSaveRejected(byte[] bytes, string label)
    {
        try
        {
            SimulationWorld.Load(new MemoryStream(bytes));
        }
        catch (SaveFormatException)
        {
            return;
        }
        throw new Exception($"Expected SaveFormatException for {label}, but load succeeded.");
    }

    private static SimulationWorld TwoTownSaveBase(int farmPlots = 1, int members = 3)
    {
        var rows = Enumerable.Range(0, 24).Select(y => y is 0 or 23 ? new string('^', 50) : "^" + new string('.', 48) + "^").ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 707,
            MapWidth = 50,
            MapHeight = 24,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = farmPlots, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 10, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 40, 10, Faction.Demon, "East"));
        for (int k = 0; k < members; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 40, 10, Demon));
        world.Step();
        return world;
    }

    private static void CropSchedulerInvariantsOnLoad()
    {
        // 1. Missing plot target (plot index out of bounds).
        var missingPlot = TwoTownSaveBase(farmPlots: 1, members: 2);
        missingPlot.AddCropTimerForTest(1, 5);
        RequireSaveRejected(SaveBytes(missingPlot), "CropGrow referencing missing plot");

        // 2. Duplicate crop timer for the same (settlement, plot).
        var dupTimer = TwoTownSaveBase(farmPlots: 1, members: 2);
        dupTimer.AddCropTimerForTest(1, 0, dueOffset: 20);
        RequireSaveRejected(SaveBytes(dupTimer), "duplicate CropGrow timer");

        // 3. Missing timer while GrowScheduled is true.
        var missingTimer = TwoTownSaveBase(farmPlots: 1, members: 2);
        missingTimer.SetPlotGrowScheduledForTest(1, 0, scheduled: true, dropTimer: true);
        RequireSaveRejected(SaveBytes(missingTimer), "missing CropGrow timer when GrowScheduled is true");
    }

    // ---- Finding 8 ------------------------------------------------------------------------------

    private static byte[] RewritePayload(byte[] save, Func<byte[], byte[]> editPayload)
    {
        using var r = new BinaryReader(new MemoryStream(save));
        byte[] head = r.ReadBytes(16);
        byte[] options = r.ReadBytes(r.ReadInt32());
        byte[] payload = r.ReadBytes(r.ReadInt32());
        r.ReadBytes(32);
        byte[] hash = r.ReadBytes(64);
        payload = editPayload(payload);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(head);
        w.Write(options.Length); w.Write(options);
        w.Write(payload.Length); w.Write(payload);
        w.Write(SHA256.HashData([.. options, .. payload]));
        w.Write(hash);
        return ms.ToArray();
    }

    private static void SettlementTradeReadingInvariantsOnLoad()
    {
        // Determine a non-guard carrier id in West town.
        var probe = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) probe.Step();
        long carrier = probe.ReadSnapshot().Chunks.SelectMany(c => c)
            .First(r => probe.ReadEntityDetails(W, r.Id) is { SettlementId: 1, Job: not Job.Guard }).Id.Value;

        // 1. Positive cargo with invalid kind (Water).
        var badCarryKind = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) badCarryKind.Step();
        badCarryKind.SetCarryForTest(carrier, ResourceKind.Water, 3);
        RequireSaveRejected(SaveBytes(badCarryKind), "positive cargo with Water kind");

        // 2. Active TradeTravel without a buyer (TradePartner = 0).
        var travelNoBuyer = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) travelNoBuyer.Step();
        travelNoBuyer.SetCarryForTest(carrier, ResourceKind.Food, 4);
        travelNoBuyer.SetTradeStateForTest(carrier, partner: 0, onTrip: true, goal: WorkGoal.TradeTravel, action: ActionKind.Work);
        travelNoBuyer.SetSettlementCountersForTest(1, members: 3, guards: 1, tradersOut: 1);
        RequireSaveRejected(SaveBytes(travelNoBuyer), "active TradeTravel with TradePartner=0");

        // 3. Mismatched TradersOut counter.
        var badTradersOut = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) badTradersOut.Step();
        badTradersOut.SetSettlementCountersForTest(1, members: 3, guards: 1, tradersOut: 1);
        RequireSaveRejected(SaveBytes(badTradersOut), "mismatched TradersOut");

        // 4. Open reading session with unknown book key.
        var unknownBook = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) unknownBook.Step();
        unknownBook.SetReadingStateForTest(carrier, book: 999_001, since: unknownBook.LastCompletedTick, action: ActionKind.Read);
        RequireSaveRejected(SaveBytes(unknownBook), "open reading session with unknown book");

        // 5. Open reading session with future start tick.
        var futureRead = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) futureRead.Step();
        futureRead.SetReadingStateForTest(carrier, book: 1000, since: futureRead.LastCompletedTick + 5, action: ActionKind.Read);
        RequireSaveRejected(SaveBytes(futureRead), "open reading session with future ReadingSince");

        // 6. Duplicate book key in Reading list.
        var dupBook = TwoTownSaveBase(farmPlots: 0, members: 3);
        for (int t = 0; t < 20; t++) dupBook.Step();
        dupBook.AddReadingProgressForTest(carrier, 1000, 10);
        dupBook.AddReadingProgressForTest(carrier, 1000, 20);
        RequireSaveRejected(SaveBytes(dupBook), "duplicate book key in Reading list");

        // 7. Unsigned collection length >= 0x80000000u (Reading.Count) must not narrow negative and bypass cap.
        var emptyReadWorld = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 708,
            MapWidth = 16,
            MapHeight = 16,
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Disabled,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        emptyReadWorld.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 8, 8, Human));
        emptyReadWorld.Step();
        byte[] cleanSave = SaveBytes(emptyReadWorld);
        // Locate the single entity's Reading.Count (uint 0 followed by ReadingBook=0, ReadingSince=0, Mana, ManaTick, TamedTo=0, TamerId=0, Trust=0, then settlements=0).
        // From the end of payload: settlements(4)=0, carcasses(4)=0... wait: settlements is at the very end!
        // In payload order before settlements: entities' last entity ends with ReadingCount(4), ReadingBook(8), ReadingSince(8), Adventure(4+8+8+4+8? No: Mana(8)+ManaTick(8)+TamedTo(4)+TamerId(8)+Trust(4)=32), deaths(4)=0, milestones(4)=0, pathQueue(4)=0, carcasses(4)=0, settlements(4)=0.
        // WriteEntity writes skills+adventure BEFORE deaths/milestones/pathQueue/carcasses/settlements!
        // Exact trailing size after ReadingCount(uint32): ReadingBook(8) + ReadingSince(8) + Adventure(40) + deaths(4) + milestones(4) + pathQueue(4) + carcasses(4) + settlements(4) + dangerSettlements(4) = 80 bytes!
        byte[] corruptLenSave = RewritePayload(cleanSave, payload =>
        {
            var copy = (byte[])payload.Clone();
            int offset = copy.Length - 80 - 4;
            Check(BitConverter.ToUInt32(copy, offset) == 0u, "expected Reading.Count == 0 at known offset");
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(copy, offset);
            return copy;
        });
        try
        {
            SimulationWorld.Load(new MemoryStream(corruptLenSave));
            throw new Exception("Expected SaveFormatException for 0xFFFFFFFFu Reading.Count");
        }
        catch (SaveFormatException ex)
        {
            Check(ex.Message.Contains("Reading list too long", StringComparison.Ordinal),
                $"expected unsigned Reading list length check to reject 0xFFFFFFFFu, got: {ex.Message}");
        }

        // Positive control A: legitimate trader returning with unsold goods loads and continues identically.
        var retWorld = TwoTownSaveBase(farmPlots: 0, members: 3);
        retWorld.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        retWorld.SetStorageForTest(2, food: 0, wood: 100, coins: 100);
        long retCursor = retWorld.ReadEvents(0, 1).LatestSequence;
        bool loadedEight = false;
        for (int t = 0; t < 200 && !loadedEight; t++)
        {
            var ev = Run(retWorld, 1, ref retCursor);
            if (ev.Any(e => e.Kind == EventKinds.TradeLoaded && e.Amount == 8)) loadedEight = true;
        }
        Check(loadedEight, "trader loaded 8 units");
        // Reduce buyer coins in transit so only 3 units can be paid for on arrival.
        retWorld.SetStorageForTest(2, food: 0, wood: 100, coins: 6);
        bool soldPartial = false;
        for (int t = 0; t < 400 && !soldPartial; t++)
        {
            var ev = Run(retWorld, 1, ref retCursor);
            if (ev.Any(e => e.Kind == EventKinds.TradeSold && e.Amount == 3)) soldPartial = true;
        }
        Check(soldPartial, "trader sold 3 and now carries 5 unsold units home");
        var returning = retWorld.ReadSnapshot().Chunks.SelectMany(c => c)
            .Select(r => retWorld.ReadEntityDetails(W, r.Id)!)
            .Single(d => d.SettlementId == 1 && d.CarryAmount == 5);
        Check(returning.CarryKind == ResourceKind.Food, "returning trader holds unsold Food");
        var retLoaded = SimulationWorld.Load(new MemoryStream(SaveBytes(retWorld)));
        Check(retLoaded.StateHash() == retWorld.StateHash(), "positive control: returning with unsold goods loads");
        for (int t = 0; t < 200; t++) { retWorld.Step(); retLoaded.Step(); }
        Check(retLoaded.StateHash() == retWorld.StateHash(), "positive control: returning trader continues identically");

        // Positive control B: legitimate mid-read checkpoint loads and continues identically.
        var readWorld = TwoTownSaveBase(farmPlots: 0, members: 2);
        readWorld.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        readWorld.SetStorageForTest(2, food: 300, wood: 100, coins: 100);
        for (int t = 0; t < 40; t++) readWorld.Step();
        Check(readWorld.ReadSnapshot().Chunks.SelectMany(c => c).Any(r => r.Action == (int)ActionKind.Read), "expected active reader");
        var readLoaded = SimulationWorld.Load(new MemoryStream(SaveBytes(readWorld)));
        Check(readLoaded.StateHash() == readWorld.StateHash(), "positive control: mid-read checkpoint loads");
        for (int t = 0; t < 200; t++) { readWorld.Step(); readLoaded.Step(); }
        Check(readLoaded.StateHash() == readWorld.StateHash(), "positive control: mid-read continues identically");
    }

    // ---- Finding 9 ------------------------------------------------------------------------------

    private static void PartialSaleReconcilesBothTownsAndCargo()
    {
        // Freeze unrelated production (no farm plots, no wild nodes) and consumption (zero hunger/thirst).
        var rows = Enumerable.Range(0, 16).Select(y => y is 0 or 15 ? new string('^', 48) : "^" + new string('.', 46) + "^").ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 909,
            MapWidth = 48,
            MapHeight = 16,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 8, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 38, 8, Faction.Demon, "East"));
        for (int k = 0; k < 3; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 8, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 38, 8, Demon));
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        world.SetStorageForTest(2, food: 0, wood: 100, coins: 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;

        // Wait for 8 units loaded into transit.
        WorldEvent loadEvent = default;
        var events = new List<WorldEvent>();
        for (int t = 0; t < 150 && loadEvent.Kind != EventKinds.TradeLoaded; t++)
        {
            var batch = Run(world, 1, ref cursor);
            events.AddRange(batch);
            loadEvent = batch.FirstOrDefault(e => e.Kind == EventKinds.TradeLoaded);
        }
        Check(loadEvent.Kind == EventKinds.TradeLoaded && loadEvent.Amount == 8 && loadEvent.Reason == (int)ResourceKind.Food,
            $"expected 8 Food loaded, got {loadEvent}");
        Check(world.ReadSettlements()[0].Food == 292 && world.ReadSettlements()[0].TradersOut == 1, "seller debited and slot reserved on load");

        // Reduce buyer coins in transit so only 3 units can sell; then run until trader returns home.
        int unitPrice = TradeConfig.Default.FoodPrice;
        world.SetStorageForTest(2, food: 0, wood: 100, coins: 3L * unitPrice);
        bool slotReleased = false;
        for (int t = 0; t < 400 && !slotReleased; t++)
        {
            var batch = Run(world, 1, ref cursor);
            events.AddRange(batch);
            if (batch.Any(e => e.Kind == EventKinds.TradeSold) && world.ReadSettlements()[0].TradersOut == 0)
                slotReleased = true;
            else if (events.Any(e => e.Kind == EventKinds.TradeSold) && world.ReadSettlements()[0].TradersOut == 0)
                slotReleased = true;
        }
        Check(slotReleased, "trader returned home and released slot");

        var sold = events.Where(e => e.Kind == EventKinds.TradeSold).ToList();
        var paid = events.Where(e => e.Kind == EventKinds.TradePaid).ToList();
        var deposits = events.Where(e => e.Kind == EventKinds.Deposited && e.Actor == loadEvent.Actor && e.Target == 1 && e.Reason == (int)ResourceKind.Food).ToList();
        Check(sold.Count == 1 && paid.Count == 1, $"expected 1 sale and 1 payment, got sold={sold.Count}, paid={paid.Count}");
        Check(sold[0].Amount == 3 && sold[0].Reason == (int)ResourceKind.Food, $"expected 3 sold, got {sold[0].Amount}");
        Check(paid[0].Tick == sold[0].Tick && paid[0].Actor == 2 && paid[0].Target == 1 && paid[0].Amount == 3L * unitPrice,
            $"expected payment {3L * unitPrice} from 2 to 1 on tick {sold[0].Tick}, got {paid[0]}");
        Check(deposits.Sum(d => d.Amount) == 5, $"expected 5 unsold units returned, got {deposits.Sum(d => d.Amount)}");

        var (west, east) = (world.ReadSettlements()[0], world.ReadSettlements()[1]);
        Check(west.Food == 297 && east.Food == 3, $"exact food reconciliation expected west=297, east=3; got west={west.Food}, east={east.Food}");
        Check(west.Coins == 100 + 3L * unitPrice && east.Coins == 0, $"exact coin exchange expected west={100 + 3L * unitPrice}, east=0; got west={west.Coins}, east={east.Coins}");
        Check(west.TradersOut == 0, "trader slot released");
    }

    // ---- Finding 10 -----------------------------------------------------------------------------

    private static void ReadSettlementsLinearAggregationAndCache()
    {
        // Verify counts after promotion and death, plus single O(entities) aggregation per completed tick across repeated reads.
        foreach (var (towns, popPerTown) in new[] { (3, 8), (9, 10) })
        {
            int width = Math.Max(60, towns * 30);
            var rows = Enumerable.Range(0, 20).Select(y => y is 0 or 19 ? new string('^', width) : "^" + new string('.', width - 2) + "^").ToList();
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W,
                Seed = (ulong)(1010 + towns),
                MapWidth = width,
                MapHeight = 20,
                Map = MapOverride.FromRows(rows),
                Species = Default,
                Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
                Mind = MindConfig.Default with { FatiguePerTick = 0 },
                Settlements = SettlementConfig.Default with { FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            for (int i = 0; i < towns; i++)
            {
                int cx = 15 + i * 25;
                world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, cx, 10, Faction.Human, $"Town{i}"));
                for (int k = 0; k < popPerTown; k++)
                    world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, cx, 10, Human));
            }
            for (int t = 0; t < 20; t++) world.Step(); // lets guards promote

            int aggBefore = world.SettlementAggregationCount;
            long scansBefore = world.SettlementAggregationEntityScans;
            var r1 = world.ReadSettlements();
            var r2 = world.ReadSettlements();
            var r3 = world.ReadSettlements();
            Check(ReferenceEquals(r1, r2) && ReferenceEquals(r2, r3), "repeated same-tick ReadSettlements must share immutable result");
            Check(world.SettlementAggregationCount - aggBefore == 1,
                $"expected 1 aggregation across 3 same-tick reads, got {world.SettlementAggregationCount - aggBefore}");
            Check(world.SettlementAggregationEntityScans - scansBefore == towns * popPerTown,
                $"expected 1 linear pass ({towns * popPerTown} entity scans), got {world.SettlementAggregationEntityScans - scansBefore}");
            Check(r1.Count == towns && r1.All(s => s.Population == popPerTown && s.Guards == Math.Max(1, popPerTown / TradeConfig.Default.MembersPerGuard)),
                "accurate population and guard counts after promotion");

            // Kill the guard of town 1 and verify counts reflect death on the next tick.
            long guard1 = world.ReadSnapshot().Chunks.SelectMany(c => c)
                .First(r => world.ReadEntityDetails(W, r.Id) is { SettlementId: 1, Job: Job.Guard }).Id.Value;
            world.KillForTest(guard1);
            var afterKill = world.ReadSettlements();
            Check(afterKill[0].Population == popPerTown - 1 && afterKill[0].Guards == 0,
                $"expected town 1 pop={popPerTown - 1}, guards=0 immediately after death; got pop={afterKill[0].Population}, guards={afterKill[0].Guards}");
            world.Step();
            var nextTick = world.ReadSettlements();
            Check(nextTick[0].Population == popPerTown - 1, "population stays decremented after death tick");

            // Load invalidates cache and recomputes cleanly.
            var loaded = SimulationWorld.Load(new MemoryStream(SaveBytes(world)));
            var lr = loaded.ReadSettlements();
            Check(lr.SequenceEqual(nextTick), "loaded world recomputes matching settlement views");
        }
    }

    // ---- Finding 11 -----------------------------------------------------------------------------

    private static void TradeDealCachedPerRevision()
    {
        // One surplus town with 30 idle workers, 15 ineligible buyers (0 coins), then one buyer gains funds.
        int width = 420;
        var rows = Enumerable.Range(0, 16).Select(y => y is 0 or 15 ? new string('^', width) : "^" + new string('.', width - 2) + "^").ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 1111,
            MapWidth = width,
            MapHeight = 16,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        // Town 1 (seller at x=20)
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 8, Faction.Human, "Seller"));
        for (int k = 0; k < 30; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 20, 8, Human));
        // 15 buyer towns; towns 5 and 8 are equidistant from Seller (dx=100 vs dx=100 if placed around? Let's put Buyer 2..16:
        // make towns 10 and 11 equidistant nearest buyers at distance 25 (x=45 -> wait: spacing >= 20!).
        // Put Seller at x=120:
        // Wait: all towns must be >= 20 apart. Put Seller at x=100, wait: can two towns be equidistant from Seller at (100,8)?
        // Yes! Put one at (70, 8) (d=30) and one at (130, 8) (d=30), and the rest further east at x=155, 180, ...!
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 70, 8, Faction.Demon, "EquidistantWest"));   // id 2? Wait: Seller was at 20! Let's check positions!
        for (int i = 2; i < 16; i++)
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 70 + i * 22, 8, Faction.Demon, $"Buyer{i}"));
        world.Step();
        // Actually let's test tie-break clearly in a separate dedicated world or arrange equidistant towns cleanly:
        // Wait: in this world, Seller is at x=20; Buyer 2 is at x=70 (d=50), Buyer 3 at x=114 (d=94)...
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        for (int id = 2; id <= 16; id++) world.SetStorageForTest(id, food: 0, wood: 100, coins: 0); // all ineligible

        long scansBefore = world.TradeDealScans;
        for (int t = 0; t < 40; t++) world.Step(); // all 30 idle workers think ~4 times each (~120+ PlanWork/Read checks)
        long idleScans = world.TradeDealScans - scansBefore;
        Check(idleScans <= 2, $"expected cached TradeDeal across 30 idle workers and 40 ticks (<=2 scans), got {idleScans}");

        // Now give Buyer 4 (x=158, further) and Buyer 2 (x=70, nearest) funds; Buyer 2 must win and trigger re-evaluation.
        world.SetStorageForTest(4, food: 0, wood: 100, coins: 100);
        world.SetStorageForTest(2, food: 0, wood: 100, coins: 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 250, ref cursor);
        Check(world.TradeDealScans - scansBefore > idleScans, "funding buyer invalidated deal cache");
        var sold = events.FirstOrDefault(e => e.Kind == EventKinds.TradeSold);
        Check(sold.Kind == EventKinds.TradeSold && sold.Target == 2, $"expected nearest eligible buyer 2, got target={sold.Target}");

        // Ascending-ID tie break when two buyers are equidistant from Seller.
        var tieRows = Enumerable.Range(0, 16).Select(y => y is 0 or 15 ? new string('^', 100) : "^" + new string('.', 98) + "^").ToList();
        var tieWorld = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 1112,
            MapWidth = 100,
            MapHeight = 16,
            Map = MapOverride.FromRows(tieRows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0, WoodTarget = 0, FoodTarget = 60 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        tieWorld.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 50, 8, Faction.Human, "Hub"));      // id 1
        tieWorld.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 8, Faction.Demon, "LowerId"));  // id 2, distance 30
        tieWorld.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 80, 8, Faction.Demon, "HigherId")); // id 3, distance 30
        for (int k = 0; k < 3; k++)
            tieWorld.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 50, 8, Human));
        tieWorld.Step();
        tieWorld.SetStorageForTest(1, food: 300, wood: 100, coins: 100);
        tieWorld.SetStorageForTest(2, food: 0, wood: 100, coins: 100);
        tieWorld.SetStorageForTest(3, food: 0, wood: 100, coins: 100);
        long tieCursor = tieWorld.ReadEvents(0, 1).LatestSequence;
        var tieEvents = Run(tieWorld, 80, ref tieCursor);
        var tieSold = tieEvents.FirstOrDefault(e => e.Kind == EventKinds.TradeSold);
        Check(tieSold.Kind == EventKinds.TradeSold && tieSold.Target == 2,
            $"expected ascending-ID tie-break to pick buyer 2 over equidistant buyer 3, got {tieSold.Target}");
    }

    // ---- Finding 12 -----------------------------------------------------------------------------

    private static void FoundingSkipsPlotsBeyondManhattan3()
    {
        // Make all tiles around (8,8) mountains except (8,8) and a single grass tile at offset (3,3) [distance 6],
        // then separately at offset (2,1) [distance 3].
        static int PlotCountForOffset(int dx, int dy)
        {
            var rows = Enumerable.Range(0, 17).Select(_ => new string('^', 17)).ToArray();
            void SetGrass(int x, int y)
            {
                var arr = rows[y].ToCharArray();
                arr[x] = '.';
                rows[y] = new string(arr);
            }
            SetGrass(8, 8);
            SetGrass(8 + dx, 8 + dy);
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W,
                Seed = 1212,
                MapWidth = 17,
                MapHeight = 17,
                Map = MapOverride.FromRows(rows),
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 8, 8, Faction.Human, "Crag"));
            world.Step();
            return world.ReadFarmPlots(1).Count;
        }

        Check(PlotCountForOffset(3, 3) == 0, "offset (3,3) has Manhattan distance 6 > 3 and must produce 0 plots");
        Check(PlotCountForOffset(2, 1) == 1, "offset (2,1) has Manhattan distance 3 <= 3 and must produce 1 plot");
    }
}
