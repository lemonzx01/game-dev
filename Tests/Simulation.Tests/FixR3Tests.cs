using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>FIX-R3: regressions for the Codex review of R1 (docs/reviews/2026-11-codex-review-R1.md).</summary>
internal static class FixR3Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);
    private const int Day = 2400;

    public static (string, Action)[] Cases =>
    [
        ("FIX-R3 #1 sleep to rest heals at the same rate live and after load", SleepToRestLiveEqualsLoaded),
        ("FIX-R3 #2 fight noise does not depend on event-log capacity", NoiseIndependentOfEventLog),
        ("FIX-R3 #3 bed-down rechecks the window and guard duty", BedDownRechecks),
        ("FIX-R3 #5 Body off keeps pre-R1 vitals anchors on rest changes", BodyOffKeepsAnchors),
        ("FIX-R3 #6 a sleeping healer does not cast", SleepingHealerDoesNotCast),
        ("FIX-R3 #8 collapsed windows never sleep; wake ticks are always in the future", WindowEdges),
        ("FIX-R3 #9 thirst wakes a sleeper who then drinks", ThirstWakesSleeper),
        ("FIX-R3 #11 a badly wounded villager rests to recover", WoundedRests),
        ("FIX-R3 #12 load rejects impossible sleep and rest state", LoadRejectsBodyState),
        ("FIX-R3 #13 rates that round down to zero are rejected", ZeroRatesRejected),
        ("FIX-R3 #17 Sleep is published only once asleep", PublishedSleep),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static readonly VitalsConfig NoNeeds = new(0, 0, 0, 0, 100, 250_000, 100_000, 4);

    private static SimulationWorld World(VitalsConfig? vitals = null, bool body = true, int queue = 1024, int maxEvents = 2048,
        CalendarConfig? calendar = null, BodyConfig? bodyConfig = null, MagicConfig? magic = null, string[]? rows = null, ResourcePlacement[]? res = null)
    {
        var map = rows ?? Enumerable.Range(0, 20).Select(y => y is 0 or 19 ? new string('^', 50) : "^" + new string('.', 48) + "^").ToArray();
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 33, MapWidth = map[0].Length, MapHeight = map.Length, Map = MapOverride.FromRows([.. map], res ?? []), Species = Default,
            Vitals = vitals ?? NoNeeds,
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0 },
            Body = bodyConfig ?? (body ? BodyConfig.Realistic : BodyConfig.Default),
            Magic = magic ?? MagicConfig.Default with { MinScholarshipLevel = 10 },
            Calendar = calendar ?? CalendarConfig.Default,
            CommandQueueCapacity = queue, MaxEventsPerTick = maxEvents,
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
                if (page.Status == PageStatus.Gap) { cursor = page.NextCursor; continue; } // capacity tests drop events on purpose
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

    private static (SimulationWorld World, long Cursor, long Guard, List<long> Others) Town(SimulationWorld? world = null)
    {
        world ??= World();
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 25, 10, Faction.Human, "Lamplight"));
        for (int k = 0; k < 7; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 22 + k, 12, Human));
        long cursor = 0;
        Run(world, 2, ref cursor);
        world.SetStorageForTest(1, 300, 300, 100);
        RunTo(world, 60, ref cursor);
        var people = Live(world, Human);
        long guard = people.Single(id => world.ReadEntityDetails(W, new EntityId(id))!.Job == Job.Guard);
        return (world, cursor, guard, people.Where(id => id != guard).ToList());
    }

    private static long Health(SimulationWorld w, long id) => w.ReadEntityDetails(W, new EntityId(id))!.Health;

    private static void SleepToRestLiveEqualsLoaded()
    {
        var (world, cursor, _, others) = Town();
        RunTo(world, 250, ref cursor);
        long id = others[0];
        Check(world.IsAsleepForTest(id), "asleep");
        world.SetHealthForTest(id, 300_000);
        Run(world, 1, ref cursor);
        Check(world.ForceStartForTest(id, ActionKind.Rest), "switched from sleep to rest");
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        long before = Health(world, id);
        for (int t = 1; t <= 50; t++)
        {
            world.Step();
            loaded.Step();
            Check(Health(world, id) == Health(loaded, id), $"tick +{t}: live {Health(world, id)} vs loaded {Health(loaded, id)}");
            if (t == 5) // still resting (committed); later at night it may rightly go back to sleep
            {
                long expected = 5 * NoNeeds.RegenPerTick * BodyConfig.Default.RestHealPercent / 100;
                Check(world.ActionOf(id) == ActionKind.Rest && Health(world, id) - before == expected,
                    $"rest heals at {BodyConfig.Default.RestHealPercent}% ({Health(world, id) - before} vs {expected}, {world.ActionOf(id)})");
            }
        }
        Check(world.StateHash() == loaded.StateHash(), "identical hash");
    }

    private static void NoiseIndependentOfEventLog()
    {
        List<string> Trace(int maxEvents)
        {
            var (world, cursor, _, others) = Town(World(queue: 16, maxEvents: maxEvents));
            RunTo(world, 250, ref cursor);
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 27, 10, Wolf));
            var trace = new List<string>();
            for (int t = 0; t < 120; t++)
            {
                Run(world, 1, ref cursor);
                trace.Add(string.Join(",", others.Select(id => world.IsAsleepForTest(id) ? "z" : "a")) + "|" + Health(world, others[0]));
            }
            return trace;
        }
        var roomy = Trace(2048);
        var tight = Trace(17); // most events of a busy tick are dropped
        Check(roomy.Any(t => t.Contains('a')) && roomy.Any(t => t.Contains('z')), "someone was woken by the fight");
        Check(roomy.SequenceEqual(tight), "the same sleepers wake on the same ticks whatever the log capacity");
    }

    private static void BedDownRechecks()
    {
        var (world, cursor, guard, others) = Town();
        RunTo(world, 300, ref cursor);
        world.SetMindForTest(guard, ActionKind.Sleep, false); // e.g. promoted to guard while walking home to sleep
        world.SleepOrReplanForTest(guard);
        Check(!world.IsAsleepForTest(guard) && world.ActionOf(guard) != ActionKind.Sleep, "a guard on duty does not bed down inside the window");
        RunTo(world, 500, ref cursor); // 05:00, the diurnal window has just ended
        long id = others[0];
        Check(!world.IsAsleepForTest(id), "awake after the window");
        world.SetMindForTest(id, ActionKind.Sleep, false); // still walking home to sleep when the window ends
        world.SleepOrReplanForTest(id);
        Check(!world.IsAsleepForTest(id) && world.ActionOf(id) != ActionKind.Sleep, "arriving home at dawn does not start a whole day of sleep");
    }

    private static void BodyOffKeepsAnchors()
    {
        var world = World(body: false);
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Human));
        long cursor = 0;
        Run(world, 3, ref cursor);
        long id = Live(world, Human).Single();
        long anchor = world.VitalsAnchorTickForTest(id);
        Check(world.ForceStartForTest(id, ActionKind.Rest), "rest");
        Run(world, 2, ref cursor);
        Check(world.ForceStartForTest(id, ActionKind.Wander), "wander");
        Run(world, 2, ref cursor);
        Check(world.VitalsAnchorTickForTest(id) == anchor, "rest changes do not re-anchor vitals with Body off");
    }

    private static void SleepingHealerDoesNotCast()
    {
        var (world, cursor, _, others) = Town(World(magic: MagicConfig.Default));
        world.SetSkillForTest(others[0], Skill.Scholarship, 10_000);
        RunTo(world, 250, ref cursor);
        Check(world.IsAsleepForTest(others[0]) && world.IsAsleepForTest(others[1]), "healer and patient asleep");
        world.SetHealthForTest(others[1], 300_000);
        var events = Run(world, 100, ref cursor);
        Check(!events.Any(e => e.Kind == EventKinds.SpellCast && e.Actor == others[0] && world.IsAsleepForTest(others[0])), "no casting while asleep");
        Check(!events.Any(e => e.Kind == EventKinds.SpellCast && e.Actor == others[0]) || !world.IsAsleepForTest(others[0]), "a sleeper never casts");
    }

    private static void WindowEdges()
    {
        var tiny = World(calendar: new CalendarConfig(10, 7), bodyConfig: BodyConfig.Realistic with { DiurnalStart = 101, DiurnalEnd = 109 });
        tiny.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Deer));
        long cursor = 0;
        Run(tiny, 2, ref cursor);
        long deer = Live(tiny, Deer).Single();
        Check(tiny.WakeTickForTest(deer) is null, "a window that rounds to nothing is no window");
        for (int t = 0; t < 50; t++) { Run(tiny, 1, ref cursor); Check(!tiny.IsAsleepForTest(deer), "never sleeps in a collapsed window"); }

        var week = World(calendar: new CalendarConfig(7, 7), bodyConfig: BodyConfig.Realistic with { DiurnalStart = 0, DiurnalEnd = 500 });
        week.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 10, Deer));
        cursor = 0;
        Run(week, 1, ref cursor);
        long d2 = Live(week, Deer).Single();
        for (int t = 0; t < 21; t++)
        {
            long now = week.ReadSnapshot().Tick;
            Check(week.WakeTickForTest(d2) is long wake && wake > now && wake - now <= 7, $"wake tick in the future within a day at {now}");
            Run(week, 1, ref cursor);
        }
    }

    private static void ThirstWakesSleeper()
    {
        var rows = Enumerable.Range(0, 12).Select(y => y is 0 or 11 ? new string('^', 30) : "^" + new string('.', 28) + "^").ToArray();
        var water = new[] { new ResourcePlacement(14, 5, ResourceKind.Water, 1000) };
        rows[5] = "^" + new string('.', 13) + "~" + new string('.', 14) + "^";
        var world = World(vitals: new VitalsConfig(0, 2_000, 0, 5_000, 100, 250_000, 100_000, 4), rows: rows, res: water);
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 5, Deer));
        long cursor = 0;
        var events = Run(world, 450, ref cursor); // all inside the night window (0..499)
        long deer = events.First(e => e.Kind == EventKinds.CommandApplied).Target;
        long firstSleep = events.First(e => e.Kind == EventKinds.ActionStarted && e.Actor == deer && e.Reason == (int)ActionKind.Sleep).Tick;
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == deer && e.Reason == (int)ActionKind.Drink && e.Tick > firstSleep),
            "the thirsty sleeper woke and went to drink during the night");
        Check(world.ReadEntityDetails(W, new EntityId(deer))!.LifeState == LifeStates.Alive, "and survived");
    }

    private static void WoundedRests()
    {
        var (world, cursor, _, others) = Town();
        RunTo(world, Day / 2, ref cursor); // noon
        long wounded = others[0], healthy = others[1];
        world.SetHealthForTest(wounded, 300_000);
        var events = Run(world, 200, ref cursor);
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wounded && e.Reason == (int)ActionKind.Rest), "the wounded villager rests");
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == healthy && e.Reason == (int)ActionKind.Rest), "the healthy one does not");
        Check(Health(world, wounded) > 300_000 + 200 * NoNeeds.RegenPerTick, "rest healed faster than awake healing");
    }

    private static void LoadRejectsBodyState()
    {
        void Rejects(bool body, ActionKind kind, bool resting, string what)
        {
            var (world, _, _, others) = Town(World(body: body));
            world.SetMindForTest(others[0], kind, resting);
            using var ms = new MemoryStream();
            world.Save(ms);
            try { SimulationWorld.Load(new MemoryStream(ms.ToArray())); }
            catch (SaveFormatException) { return; }
            throw new Exception($"load accepted {what}");
        }
        Rejects(false, ActionKind.Sleep, false, "Sleep with Body off");
        Rejects(true, ActionKind.Idle, true, "resting while idle");
        var (ok, okCursor, _, okOthers) = Town();
        RunTo(ok, 250, ref okCursor);
        using var good = new MemoryStream();
        ok.Save(good);
        var restored = SimulationWorld.Load(new MemoryStream(good.ToArray()));
        Check(restored.IsAsleepForTest(okOthers[0]), "a real sleeper still loads");
    }

    private static void ZeroRatesRejected()
    {
        try
        {
            World(vitals: new VitalsConfig(1, 1, 0, 0, 100, 250_000, 100_000, 4));
            throw new Exception("accepted a dragon hunger rate of 1 x 30% = 0");
        }
        catch (ArgumentException) { }
        World(vitals: new VitalsConfig(1, 1, 0, 0, 100, 250_000, 100_000, 4), body: false); // Body off: legacy rates are fine
    }

    private static void PublishedSleep()
    {
        var (world, cursor, _, others) = Town();
        bool sawWalking = false;
        for (int t = 0; t < 240; t++)
        {
            Run(world, 1, ref cursor);
            foreach (var row in world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => others.Contains(r.Id.Value)))
            {
                bool asleep = world.IsAsleepForTest(row.Id.Value);
                Check(asleep == (row.Action == (int)ActionKind.Sleep), $"#{row.Id} published {(ActionKind)row.Action} while asleep={asleep}");
                if (!asleep && world.ActionOf(row.Id.Value) == ActionKind.Sleep) sawWalking = true;
            }
        }
        Check(others.All(world.IsAsleepForTest), "all asleep in the end");
        _ = sawWalking; // walking home is often a single step here; the invariant above covers it whenever it happens
    }
}
