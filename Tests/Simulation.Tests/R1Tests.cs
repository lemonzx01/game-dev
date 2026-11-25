using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>R1 acceptance: species bodies (own hunger/thirst rates), sleep/wake cycle, waking on nearby fights, faster healing asleep.</summary>
internal static class R1Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);
    private const int Day = 2400; // CalendarConfig.Default.TicksPerDay

    public static (string, Action)[] Cases =>
    [
        ("R1 each species gets hungry and thirsty at its own rate (off: all equal)", SpeciesRates),
        ("R1 people sleep at home at night, guards keep watch, wolves sleep by day", SleepCycle),
        ("R1 a fight nearby wakes sleepers; distant danger does not", WokenByFight),
        ("R1 healing is faster asleep than awake", Healing),
        ("R1 save while asleep continues identically", SaveAsleep),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static SimulationWorld World(VitalsConfig vitals, bool body = true)
    {
        var rows = Enumerable.Range(0, 20).Select(y => y is 0 or 19 ? new string('^', 50) : "^" + new string('.', 48) + "^").ToList();
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 31, MapWidth = 50, MapHeight = 20, Map = MapOverride.FromRows(rows, []), Species = Default,
            Vitals = vitals,
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0 },
            Body = body ? BodyConfig.Realistic : BodyConfig.Default,
            Magic = MagicConfig.Default with { MinScholarshipLevel = 10 }, // nobody becomes a healer in these scenarios
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static readonly VitalsConfig NoNeeds = new(0, 0, 0, 0, 100, 250_000, 100_000, 4);

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

    private static void RunTo(SimulationWorld world, long tick, ref long cursor)
    {
        long now = world.ReadSnapshot().Tick;
        if (tick > now) Run(world, (int)(tick - now), ref cursor);
    }

    private static List<long> Live(SimulationWorld w, int species) =>
        w.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == species && r.LifeState == LifeStates.Alive).Select(r => r.Id.Value).ToList();

    private static void SpeciesRates()
    {
        foreach (bool body in new[] { true, false })
        {
            var world = World(new VitalsConfig(1000, 500, 0, 0, 0, 250_000, 100_000, 4), body);
            int[] kinds = [Rabbit, Deer, Wolf, Human, SpeciesCatalog.Dragon];
            for (int k = 0; k < kinds.Length; k++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 5 + 8 * k, 10, kinds[k]));
            long cursor = 0;
            var spawned = Run(world, 1, ref cursor).Where(e => e.Kind == EventKinds.CommandApplied).ToList();
            Check(spawned.Count == kinds.Length, $"every species spawned ({spawned.Count})"); // FIX-R3 #16
            Run(world, 60, ref cursor);
            long now = world.ReadSnapshot().Tick;
            foreach (var (spawn, kind) in spawned.Zip(kinds))
            {
                var d = world.ReadEntityDetails(W, new EntityId(spawn.Target))!;
                var sd = Default[kind];
                long elapsed = now - spawn.Tick;
                long hunger = 1000L * (body ? sd.HungerPercent : 100) / 100 * elapsed, thirst = 500L * (body ? sd.ThirstPercent : 100) / 100 * elapsed;
                Check(d.Hunger == hunger && d.Thirst == thirst, $"{sd.Name} body={body}: hunger {d.Hunger} vs {hunger}, thirst {d.Thirst} vs {thirst}");
            }
        }
        Check(Default[Rabbit].HungerPercent > Default[Human].HungerPercent && Default[SpeciesCatalog.Dragon].HungerPercent < Default[Wolf].HungerPercent,
            "rabbits eat often, dragons rarely");
    }

    // A human town at (25,10) with 7 members (one guard) and two wolves in the open.
    private static (SimulationWorld World, long Cursor, long Guard, List<long> Others, List<long> Wolves) Town(VitalsConfig? vitals = null)
    {
        var world = World(vitals ?? NoNeeds);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 25, 10, Faction.Human, "Lamplight"));
        for (int k = 0; k < 7; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 22 + k, 12, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 4, 4, Wolf));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 45, 15, Wolf));
        long cursor = 0;
        Run(world, 2, ref cursor);
        world.SetStorageForTest(1, 300, 300, 100);
        RunTo(world, 60, ref cursor);
        var people = Live(world, Human);
        long guard = people.Single(id => world.ReadEntityDetails(W, new EntityId(id))!.Job == Job.Guard);
        return (world, cursor, guard, people.Where(id => id != guard).ToList(), Live(world, Wolf));
    }

    private static void SleepCycle()
    {
        var (world, cursor, guard, others, wolves) = Town();
        Check(wolves.Count == 2 && others.Count == 6, "both wolves and six villagers present"); // FIX-R3 #16
        RunTo(world, 300, ref cursor); // 03:00
        foreach (long id in others)
        {
            var d = world.ReadEntityDetails(W, new EntityId(id))!;
            Check(world.IsAsleepForTest(id) && d.X == 25 && d.Y == 10, $"#{id} asleep at home at night ({world.ActionOf(id)} at {d.X},{d.Y})");
        }
        Check(!world.IsAsleepForTest(guard), "the guard keeps watch at night");
        Check(wolves.All(id => !world.IsAsleepForTest(id)), "wolves are awake at night");
        RunTo(world, Day / 2, ref cursor); // noon
        Check(others.All(id => !world.IsAsleepForTest(id)), "nobody sleeps at noon");
        Check(wolves.All(world.IsAsleepForTest), "wolves sleep by day");
        RunTo(world, Day + 300, ref cursor); // the next night
        Check(others.All(world.IsAsleepForTest), "they sleep again the next night");
    }

    private static void WokenByFight()
    {
        var (world, cursor, guard, others, _) = Town();
        RunTo(world, 250, ref cursor);
        Check(others.All(world.IsAsleepForTest), "everyone asleep before the fight");
        // A wolf 4 tiles from the sleepers is beyond their awareness: nobody flees until the fight starts.
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 29, 10, Wolf));
        var events = Run(world, 120, ref cursor);
        var firstHit = events.FirstOrDefault(e => e.Kind == EventKinds.Attacked);
        Check(firstHit.Kind == EventKinds.Attacked, "the guard fought the wolf");
        var woke = events.Where(e => e.Kind == EventKinds.WokeUp).ToList();
        Check(woke.Any(w => others.Contains(w.Actor)), "sleeping villagers woke");
        Check(woke.Min(w => w.Tick) >= firstHit.Tick, "nobody woke before the first blow");
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Flee && others.Contains(e.Actor) && e.Tick < firstHit.Tick),
            "sleepers did not notice the wolf 4 tiles away before the fight");
        foreach (var w in woke.Where(w => others.Contains(w.Actor)))
            Check(events.Any(a => a.Kind == EventKinds.Attacked && a.Tick == w.Tick && Math.Max(Math.Abs(a.X - w.X), Math.Abs(a.Y - w.Y)) <= BodyConfig.Default.NoiseRadius),
                $"#{w.Actor} woke because a fight was within earshot");
    }

    private static void Healing()
    {
        var (world, cursor, _, others, _) = Town();
        long sleeper = others[0], waker = others[1];
        RunTo(world, 200, ref cursor);
        Check(world.IsAsleepForTest(sleeper), "asleep");
        world.SetHealthForTest(sleeper, 300_000);
        Run(world, 100, ref cursor);
        long asleepGain = world.ReadEntityDetails(W, new EntityId(sleeper))!.Health - 300_000;
        RunTo(world, Day / 2, ref cursor);
        Check(!world.IsAsleepForTest(waker) && world.ActionOf(waker) != ActionKind.Rest, "awake at noon");
        world.SetHealthForTest(waker, 600_000); // above BodyConfig.InjuredRestHealth, so it keeps its normal day (FIX-R3 #11)
        Run(world, 100, ref cursor);
        long awakeGain = world.ReadEntityDetails(W, new EntityId(waker))!.Health - 600_000;
        long regen = NoNeeds.RegenPerTick * 100;
        Check(awakeGain == regen, $"awake: base healing ({awakeGain} vs {regen})");
        Check(asleepGain == regen * BodyConfig.Default.SleepHealPercent / 100, $"asleep: {BodyConfig.Default.SleepHealPercent}% healing ({asleepGain})");
    }

    private static void SaveAsleep()
    {
        var (world, cursor, _, others, _) = Town();
        RunTo(world, 250, ref cursor);
        Check(others.All(world.IsAsleepForTest), "asleep at save");
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        for (int t = 0; t < Day; t++)
        {
            world.Step();
            loaded.Step();
        }
        Check(loaded.StateHash() == world.StateHash(), "a full day after load is identical");
    }
}
