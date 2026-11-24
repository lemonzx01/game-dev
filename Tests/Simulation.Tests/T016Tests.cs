using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T016 acceptance: seller loses stock and receives payment, buyer inverse; guard responds to threat.</summary>
internal static class T016Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T016 trade: seller loses goods and gains coins, buyer inverse", Trade),
        ("T016 no trade when the buyer cannot pay", NoMoney),
        ("T016 guard defends the town while others flee", Guard),
        ("T016 save mid-trip continues identically", SaveMidTrip),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    // Open field 70×20 with mountains around; towns far apart so traders need a long route.
    private static SimulationWorld World()
    {
        var rows = Enumerable.Range(0, 20).Select(y => y is 0 or 19 ? new string('^', 70) : "^" + new string('.', 68) + "^").ToList();
        var water = Enumerable.Range(30, 10).Select(x => new ResourcePlacement(x, 10, ResourceKind.Water, 1000)).ToArray();
        rows[10] = "^" + new string('.', 29) + new string('~', 10) + new string('.', 29) + "^";
        var map = MapOverride.FromRows(rows, water);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 16, MapWidth = 70, MapHeight = 20, Map = map, Species = Default,
            Vitals = VitalsConfig.Default with { HungerPerTick = 300, ThirstPerTick = 300 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
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

    // West town (humans) is rich in food; east town (demons) has none but coins.
    private static (SimulationWorld World, long Cursor) Towns(long westFood, long eastCoins, int members = 3)
    {
        var world = World();
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 8, 5, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 61, 5, Faction.Demon, "East"));
        for (int k = 0; k < members; k++)
        {
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 7 + k, 6, Human));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 60 + k, 6, Demon));
        }
        world.Step();
        world.SetStorageForTest(1, westFood, 100, 100);
        world.SetStorageForTest(2, 0, 100, eastCoins);
        return (world, world.ReadEvents(0, 1).LatestSequence);
    }

    private static void Trade()
    {
        var (world, cursor) = Towns(westFood: 300, eastCoins: 100);
        long coinsBefore = world.ReadSettlements().Sum(s => s.Coins);
        var events = Run(world, 1500, ref cursor);
        var loaded = events.Where(e => e.Kind == EventKinds.TradeLoaded).ToList();
        var sold = events.Where(e => e.Kind == EventKinds.TradeSold).ToList();
        var paid = events.Where(e => e.Kind == EventKinds.TradePaid).ToList();
        Check(loaded.Count > 0 && sold.Count > 0, $"a trade happened (loaded {loaded.Count}, sold {sold.Count})");
        Check(loaded.All(e => e.Target == 1) && sold.All(e => e.Target == 2) && sold.All(e => e.Reason == (int)ResourceKind.Food), "west sells food to east");
        Check(paid.Count == sold.Count, $"equal sale ({sold.Count}) and payment ({paid.Count}) counts");
        for (int i = 0; i < sold.Count; i++)
        {
            var s = sold[i]; var p = paid[i];
            int price = TradeConfig.Default.Price((ResourceKind)s.Reason);
            Check(p.Tick == s.Tick && p.Actor == s.Target && p.Target == 1 && p.Reason == price && p.Amount == s.Amount * price,
                $"payment {i} matches sale tick, parties, price and amount");
        }

        var (west, east) = (world.ReadSettlements()[0], world.ReadSettlements()[1]);
        long Sum(IEnumerable<WorldEvent> es, int town, int kind, ResourceKind res) => es.Where(e => e.Kind == kind && e.Target == town && e.Reason == (int)res).Sum(e => e.Amount);
        long eatenWest = events.Where(e => e.Kind == EventKinds.EntityAte && e.Target == 1).Sum(e => e.Amount);
        long eatenEast = events.Where(e => e.Kind == EventKinds.EntityAte && e.Target == 2).Sum(e => e.Amount);
        long westFoodExpected = 300 + Sum(events, 1, EventKinds.Deposited, ResourceKind.Food) - Sum(loaded, 1, EventKinds.TradeLoaded, ResourceKind.Food) - eatenWest;
        long westWoodExpected = 100 + Sum(events, 1, EventKinds.Deposited, ResourceKind.Wood) - Sum(loaded, 1, EventKinds.TradeLoaded, ResourceKind.Wood);
        long eastFoodExpected = 0 + Sum(sold, 2, EventKinds.TradeSold, ResourceKind.Food) + Sum(events, 2, EventKinds.Deposited, ResourceKind.Food) - eatenEast;
        long eastWoodExpected = 100 + Sum(sold, 2, EventKinds.TradeSold, ResourceKind.Wood) + Sum(events, 2, EventKinds.Deposited, ResourceKind.Wood);
        Check(west.Food == westFoodExpected && west.Wood == westWoodExpected,
            $"seller stock conserved (food {west.Food} vs {westFoodExpected}, wood {west.Wood} vs {westWoodExpected})");
        Check(east.Food == eastFoodExpected && east.Wood == eastWoodExpected,
            $"buyer stock conserved (food {east.Food} vs {eastFoodExpected}, wood {east.Wood} vs {eastWoodExpected})");
        long income = paid.Sum(e => e.Amount);
        Check(income > 0 && west.Coins == 100 + income && east.Coins == 100 - income, $"coins moved seller +{income}, buyer −{income}");
        Check(world.ReadSettlements().Sum(s => s.Coins) == coinsBefore, "coins conserved between towns");
        foreach (long actor in loaded.Select(e => e.Actor).Distinct())
        {
            var details = world.ReadEntityDetails(W, new EntityId(actor));
            foreach (var res in new[] { ResourceKind.Food, ResourceKind.Wood })
            {
                long inActor = events.Where(e => e.Actor == actor && e.Reason == (int)res
                    && e.Kind is EventKinds.Harvested or EventKinds.Gathered or EventKinds.TradeLoaded).Sum(e => e.Amount);
                long outActor = events.Where(e => e.Actor == actor && e.Reason == (int)res
                    && e.Kind is EventKinds.Deposited or EventKinds.TradeSold or EventKinds.CarryLost).Sum(e => e.Amount);
                long carriedActor = details is { LifeState: LifeStates.Alive } && details.CarryKind == res ? details.CarryAmount : 0;
                Check(inActor == outActor + carriedActor, $"trader #{actor} {res} cargo conserved ({inActor} vs {outActor} + {carriedActor})");
            }
        }
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Work), "traders worked");
    }

    private static void NoMoney()
    {
        var (world, cursor) = Towns(westFood: 300, eastCoins: 0);
        var events = Run(world, 800, ref cursor);
        Check(!events.Any(e => e.Kind is EventKinds.TradeLoaded or EventKinds.TradeSold), "no trip without a paying buyer");
        Check(world.ReadSettlements()[1].Coins == 0, "buyer still has nothing");
    }

    private static void Guard()
    {
        var (world, cursor) = Towns(westFood: 50, eastCoins: 0, members: 9);
        Run(world, 30, ref cursor); // members think and a guard volunteers
        var guards = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => world.ReadEntityDetails(W, r.Id)!.Job == Job.Guard).ToList();
        Check(guards.Count(r => world.ReadEntityDetails(W, r.Id)!.SettlementId == 1) == 1, "west has one guard for 9 members");
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 5, Wolf)); // inside West's radius
        var events = Run(world, 200, ref cursor);
        long wolf = events.First(e => e.Kind == EventKinds.CommandApplied).Target;
        var alert = events.FirstOrDefault(e => e.Kind == EventKinds.GuardAlert && e.Target == wolf);
        Check(alert.Kind == EventKinds.GuardAlert, "guard raised the alert");
        Check(world.ReadEntityDetails(W, new EntityId(alert.Actor))!.Job == Job.Guard, "the alert came from a guard");
        var death = events.FirstOrDefault(e => e.Kind == EventKinds.EntityDied && e.Target == wolf);
        Check(death.Kind == EventKinds.EntityDied && death.Reason == (int)DeathCause.Combat && death.Actor == alert.Actor, "guard killed the wolf in combat");
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Flee
            && world.ReadEntityDetails(W, new EntityId(e.Actor)) is { Job: not Job.Guard, SettlementId: 1 }), "ordinary villagers fled");
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == alert.Actor && e.Reason == (int)ActionKind.Flee), "the guard did not flee");
    }

    private static void SaveMidTrip()
    {
        var (world, cursor) = Towns(westFood: 300, eastCoins: 100);
        for (int t = 0; t < 2000 && !Run(world, 1, ref cursor).Any(e => e.Kind == EventKinds.TradeLoaded); t++) { }
        Run(world, 20, ref cursor); // trader on the road
        Check(world.ReadSettlements()[0].TradersOut == 1, "a trader is out");
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        for (int t = 0; t < 600; t++) { world.Step(); loaded.Step(); }
        Check(loaded.StateHash() == world.StateHash() && loaded.ReadSettlements().SequenceEqual(world.ReadSettlements()), "trip continues identically after load");
    }
}
