using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>R2 acceptance: threat assessment, guard retreat, town alarms, group defence, and danger memory avoidance.</summary>
internal static class R2Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("R2 one guard vs full-health dragon raises alarm, retreats, and survives", LoneGuardRetreatsFromDragonAndSurvives),
        ("R2 4+ guards and brave villagers attack a badly wounded dragon together", GroupDefenceEngagesWoundedDragonTogether),
        ("R2 single wolf in town is still fought alone by one guard without alarm", WolfHandledAloneByGuardWithoutAlarm),
        ("R2 alarm stops villagers from Work/Read and resumes after threat dies or leaves", AlarmStopsWorkAndReadingThenResumesAfterThreatGone),
        ("R2 save and load mid-alarm and mid-retreat continues identically", SaveMidAlarmAndRetreatContinuesIdentically),
        ("R2 survivors remember strong threat area and avoid it", SurvivorsRememberStrongThreatAreaAndAvoidIt),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static SimulationWorld World(int width = 44, int height = 16, ulong seed = 200,
        SettlementConfig? settlements = null, TradeConfig? trade = null, DangerConfig? danger = null,
        VitalsConfig? vitals = null, ResourcePlacement[]? extraResources = null)
    {
        var rows = Enumerable.Range(0, height)
            .Select(y => y == 0 || y == height - 1 ? new string('^', width) : "^" + new string('.', width - 2) + "^")
            .ToList();
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = seed,
            MapWidth = width,
            MapHeight = height,
            Map = MapOverride.FromRows(rows, extraResources is null ? [] : [.. extraResources]),
            Species = Default,
            Vitals = vitals ?? new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = settlements ?? SettlementConfig.Default with { FarmPlots = 0 },
            Trade = trade ?? TradeConfig.Default,
            Danger = danger ?? DangerConfig.Default,
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

    private static long Spawn(SimulationWorld world, int x, int y, int species, ref long cursor, Sex sex = Sex.Unspecified)
    {
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species, sex));
        return Run(world, 1, ref cursor).First(e => e.Kind == EventKinds.CommandApplied).Target;
    }

    private static byte[] SaveBytes(SimulationWorld world)
    {
        using var ms = new MemoryStream();
        world.Save(ms);
        return ms.ToArray();
    }

    private static void LoneGuardRetreatsFromDragonAndSurvives()
    {
        var world = World(width: 44, height: 16, seed: 201);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 8, Faction.Human, "Ashford"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 22, 8, Human, Sex.Female));
        world.Step();
        world.SetStorageForTest(1, 300, 300, 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 20, ref cursor);

        long guard = world.ReadSnapshot().Chunks.SelectMany(c => c).Single(r => r.VisualKey == Human).Id.Value;
        Check(world.ReadEntityDetails(W, new EntityId(guard))!.Job == Job.Guard, "sole settler must become Guard");

        long dragon = Spawn(world, 25, 8, SpeciesCatalog.Dragon, ref cursor);
        var events = Run(world, 250, ref cursor);

        Check(world.ReadEntityDetails(W, new EntityId(guard))!.LifeState == LifeStates.Alive,
            "lone guard charged the full-health dragon and died");
        Check(events.Any(e => e.Kind == EventKinds.TownAlarm && e.Actor == guard && e.Target == dragon && e.Reason == 1),
            "lone guard did not raise TownAlarm");
        Check(events.Any(e => e.Kind == EventKinds.GuardRetreated && e.Actor == guard && e.Target == dragon && e.Reason == 1),
            "lone guard did not record GuardRetreated");
        Check(!events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == guard && e.Target == dragon),
            "outmatched lone guard still attacked the full-health dragon");
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == guard && e.Reason == (int)ActionKind.Defend && e.Target == dragon),
            "outmatched lone guard entered Defend against dragon");
    }

    private static void GroupDefenceEngagesWoundedDragonTogether()
    {
        // 4 guards + 4 brave villagers vs a badly wounded dragon (400,000 HP, 25% damage taken).
        // Group strength overcomes the threat threshold so defenders rally and attack together with ally bonus.
        var trade = TradeConfig.Default with { MembersPerGuard = 2 };
        var world = World(width: 44, height: 16, seed: 202, trade: trade);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 8, Faction.Human, "Citadel"));
        for (int k = 0; k < 8; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 19 + k % 3, 7 + k / 3, Human, k % 2 == 0 ? Sex.Female : Sex.Male));
        world.Step();
        world.SetStorageForTest(1, 300, 300, 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 30, ref cursor);

        var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == Human).Select(r => r.Id.Value).ToList();
        var guards = people.Where(id => world.ReadEntityDetails(W, new EntityId(id))!.Job == Job.Guard).ToList();
        var villagers = people.Where(id => world.ReadEntityDetails(W, new EntityId(id))!.Job != Job.Guard).ToList();
        Check(guards.Count == 4, $"expected 4 guards with MembersPerGuard=2, got {guards.Count}");
        foreach (long v in villagers) world.SetBraveryForTest(v, 1000);

        // Spawn wounded dragon 4 tiles east of center (beyond FightBackDistance=2 so pre-R2 villagers never joined Defend).
        long dragon = Spawn(world, 25, 8, SpeciesCatalog.Dragon, ref cursor);
        world.SetHealthForTest(dragon, 400_000);
        cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 200, ref cursor);

        var defendStarters = events.Where(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Defend && e.Target == dragon && people.Contains(e.Actor))
            .Select(e => e.Actor).Distinct().ToList();
        var attackers = events.Where(e => e.Kind == EventKinds.Attacked && e.Target == dragon && people.Contains(e.Actor))
            .Select(e => e.Actor).Distinct().ToList();
        Check(defendStarters.Count >= 2, $"expected >= 2 defenders to start Defend together, got {defendStarters.Count}");
        Check(attackers.Count >= 2, $"expected >= 2 distinct defenders to strike dragon together, got {attackers.Count}");
        Check(events.Any(e => e.Kind == EventKinds.EntityDied && e.Target == dragon && e.Reason == (int)DeathCause.Combat),
            "grouped defenders did not slay the wounded dragon in combat");
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Defend && e.Target == dragon && villagers.Contains(e.Actor)),
            "brave townmates at range 4–5 did not join group defence");
    }

    private static void WolfHandledAloneByGuardWithoutAlarm()
    {
        var world = World(width: 44, height: 16, seed: 203);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 8, Faction.Human, "Village"));
        for (int k = 0; k < 9; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 18 + k % 4, 7 + k / 4, Human));
        world.Step();
        world.SetStorageForTest(1, 300, 300, 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 30, ref cursor);

        long guard = world.ReadSnapshot().Chunks.SelectMany(c => c).Single(r => r.VisualKey == Human && world.ReadEntityDetails(W, r.Id)!.Job == Job.Guard).Id.Value;
        long wolf = Spawn(world, 24, 8, Wolf, ref cursor);
        var events = Run(world, 200, ref cursor);

        Check(events.Any(e => e.Kind == EventKinds.GuardAlert && e.Actor == guard && e.Target == wolf), "guard should emit GuardAlert against wolf");
        Check(events.Any(e => e.Kind == EventKinds.EntityDied && e.Target == wolf && e.Reason == (int)DeathCause.Combat && e.Actor == guard),
            "guard did not defeat wolf alone in combat");
        Check(!events.Any(e => e.Kind is EventKinds.TownAlarm or EventKinds.GuardRetreated or EventKinds.TownAlarmEnded),
            "wolf must not trigger TownAlarm or GuardRetreated");
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Gather),
            "wolf must not make villagers Gather");
    }

    private static void AlarmStopsWorkAndReadingThenResumesAfterThreatGone()
    {
        // Part A: Alarm halts Work/Read while strong threat is in town, then resumes after threat dies.
        {
            var world = World(width: 28, height: 16, seed: 204,
                settlements: SettlementConfig.Default with { FarmPlots = 6, Radius = 10, FoodTarget = 60, WoodTarget = 30 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 16, 8, Faction.Human, "Haven"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 16, 8, Human, Sex.Female)); // Guard
            for (int k = 0; k < 4; k++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 13 + k, 8, Human, Sex.Female));
            world.Step();
            world.SetStorageForTest(1, 10, 10, 100); // keep work demand active (harvest/deliver)
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            Run(world, 25, ref cursor);

            var workers = world.ReadSnapshot().Chunks.SelectMany(c => c)
                .Where(r => r.VisualKey == Human && world.ReadEntityDetails(W, r.Id)!.Job != Job.Guard)
                .Select(r => r.Id.Value).ToList();
            foreach (long id in workers) world.SetTraitsForTest(id, 100, 500);

            long dragon = Spawn(world, 25, 8, SpeciesCatalog.Dragon, ref cursor);
            var duringAlarm = Run(world, 45, ref cursor);

            var alarmEv = duringAlarm.FirstOrDefault(e => e.Kind == EventKinds.TownAlarm && e.Target == dragon && e.Reason == 1);
            Check(alarmEv.Kind == EventKinds.TownAlarm, "TownAlarm was not raised when dragon entered town");
            var afterAlarm = duringAlarm.Where(e => e.EventSequence > alarmEv.EventSequence).ToList();
            Check(!afterAlarm.Any(e => e.Kind == EventKinds.ActionStarted && workers.Contains(e.Actor)
                && e.Reason is (int)ActionKind.Work or (int)ActionKind.Read),
                "villagers started Work/Read during an active town alarm");
            Check(afterAlarm.Any(e => e.Kind == EventKinds.ActionStarted && workers.Contains(e.Actor)
                && e.Reason is (int)ActionKind.Gather or (int)ActionKind.Flee),
                "villagers neither gathered nor fled during alarm");

            // Threat dies -> alarm clears deterministically and villagers resume Work within a bounded time.
            world.KillForTest(dragon);
            var postDeath = Run(world, 120, ref cursor);
            Check(postDeath.Any(e => e.Kind == EventKinds.TownAlarmEnded && e.Target == 1), "alarm did not end after dragon died");
            Check(!world.IsTownAlarmActiveForTest(1), "settlement alarm still flagged active after end event");
            Check(postDeath.Any(e => e.Kind == EventKinds.ActionStarted && workers.Contains(e.Actor) && e.Reason == (int)ActionKind.Work),
                "villagers did not return to Work after dragon died");
        }

        // Part B: Threat leaves town radius -> alarm ends and villagers resume Work.
        {
            var world = World(width: 48, height: 16, seed: 205,
                vitals: new VitalsConfig(15_000, 0, 0, 0, 0, 250_000, 100_000, 4),
                settlements: SettlementConfig.Default with { FarmPlots = 6, Radius = 8, FoodTarget = 60, WoodTarget = 30 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 14, 8, Faction.Human, "Border"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 14, 8, Human, Sex.Female));
            for (int k = 0; k < 4; k++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12 + k % 3, 8, Human, Sex.Female));
            world.Step();
            world.SetStorageForTest(1, 20, 10, 100);
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            Run(world, 25, ref cursor);

            var workers = world.ReadSnapshot().Chunks.SelectMany(c => c)
                .Where(r => r.VisualKey == Human && world.ReadEntityDetails(W, r.Id)!.Job != Job.Guard)
                .Select(r => r.Id.Value).ToList();
            foreach (long id in workers) world.SetTraitsForTest(id, 100, 500);

            long dragon = Spawn(world, 21, 8, SpeciesCatalog.Dragon, ref cursor); // inside radius 8
            Run(world, 18, ref cursor);
            Check(world.IsTownAlarmActiveForTest(1), "alarm did not activate when dragon entered border town");

            // Lure hungry dragon east outside town radius (14 + 8 + margin 2 = 24) within PerceptionRadius 8 of (21, 8).
            Spawn(world, 27, 8, Deer, ref cursor);
            var lureEvents = Run(world, 260, ref cursor);
            Check(lureEvents.Any(e => e.Kind == EventKinds.TownAlarmEnded && e.Target == 1),
                "alarm did not end after living dragon left town radius");
            Check(lureEvents.Any(e => e.Kind == EventKinds.ActionStarted && workers.Contains(e.Actor) && e.Reason == (int)ActionKind.Work),
                "villagers did not resume Work after threat left town");
        }
    }

    private static void SaveMidAlarmAndRetreatContinuesIdentically()
    {
        var world = World(width: 44, height: 16, seed: 206,
            settlements: SettlementConfig.Default with { FarmPlots = 6, Radius = 10, FoodTarget = 60, WoodTarget = 30 });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 8, Faction.Human, "Keep"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 23, 8, Human, Sex.Female));
        for (int k = 0; k < 4; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 19 + k % 2, 7 + k / 2, Human, Sex.Female));
        world.Step();
        world.SetStorageForTest(1, 20, 10, 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 25, ref cursor);

        var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == Human).Select(r => r.Id.Value).ToList();
        long guard = people.Single(id => world.ReadEntityDetails(W, new EntityId(id))!.Job == Job.Guard);
        foreach (long v in people.Where(id => id != guard)) world.SetTraitsForTest(v, 100, 500);

        long dragon = Spawn(world, 25, 8, SpeciesCatalog.Dragon, ref cursor);
        bool checkpointReady = false;
        for (int t = 0; t < 40; t++)
        {
            var step = Run(world, 1, ref cursor);
            if (world.IsTownAlarmActiveForTest(1)
                && (world.ActionOf(guard) is ActionKind.Gather or ActionKind.Flee)
                && people.Any(id => id != guard && world.ActionOf(id) is ActionKind.Gather or ActionKind.Flee))
            {
                checkpointReady = true;
                break;
            }
        }
        Check(checkpointReady, "did not observe active alarm with retreating guard and gathering/fleeing villagers before save");

        byte[] saved = SaveBytes(world);
        var loaded = SimulationWorld.Load(new MemoryStream(saved));
        for (int t = 0; t < 300; t++)
        {
            world.Step();
            loaded.Step();
            Check(loaded.StateHash() == world.StateHash(), $"mid-alarm replay diverged at step {t + 1}");
        }
        _ = dragon;
    }

    private static void SurvivorsRememberStrongThreatAreaAndAvoidIt()
    {
        // Danger berry east at (17, 8) (distance 5 from centre 12,8); safe berry west at (6, 8) (distance 6).
        // After dragon threatens (17, 8), survivor remembers danger there and chooses the west berry instead.
        var berries = new[]
        {
            new ResourcePlacement(17, 8, ResourceKind.Food, 20),
            new ResourcePlacement(6, 8, ResourceKind.Food, 20),
        };
        var world = World(width: 36, height: 16, seed: 207,
            settlements: SettlementConfig.Default with { FarmPlots = 0, Radius = 10, FoodTarget = 60, WoodTarget = 0 },
            danger: DangerConfig.Default with { AlarmClearGraceTicks = 1 },
            extraResources: berries);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 12, 8, Faction.Human, "Watch"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 13, 8, Human, Sex.Female)); // Guard
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 8, Human, Sex.Female)); // Gatherer
        world.Step();
        world.SetStorageForTest(1, 10, 50, 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 25, ref cursor);

        var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == Human).Select(r => r.Id.Value).ToList();
        long worker = people.Single(id => world.ReadEntityDetails(W, new EntityId(id))!.Job != Job.Guard);
        world.SetTraitsForTest(worker, 100, 500);

        long dragon = Spawn(world, 17, 8, SpeciesCatalog.Dragon, ref cursor);
        Run(world, 25, ref cursor);
        var memories = world.ReadEntityMind(W, new EntityId(worker))!.Memories;
        Check(memories.Any(m => m.Kind is MemoryKind.SawThreat or MemoryKind.WasAttacked
            && Math.Max(Math.Abs(m.X - 17), Math.Abs(m.Y - 8)) <= 3),
            "survivor did not remember strong threat location");

        world.KillForTest(dragon);
        var post = Run(world, 250, ref cursor);
        var gathered = post.Where(e => e.Kind == EventKinds.Gathered && e.Actor == worker).ToList();
        Check(gathered.Count > 0, "survivor did not resume gathering food from safe west berry");
        Check(gathered.All(e => e.X == 6 && e.Y == 8),
            $"survivor gathered at remembered danger tile ({gathered[0].X},{gathered[0].Y}) instead of avoiding (17,8)");
    }
}
