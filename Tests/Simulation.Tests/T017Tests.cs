using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T017 acceptance: skills gain from activities; interrupted reading awards actual progress; no duplicate XP.</summary>
internal static class T017Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T017 interrupted reading credits exactly the pages read, never twice", InterruptedReading),
        ("T017 work XP equals the units actually produced", WorkXp),
        ("T017 skilled farmers harvest more per trip", SkillEffect),
        ("T017 save mid-reading continues identically", SaveMidRead),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static SimulationWorld World(SettlementConfig? town = null, SkillConfig? skills = null, VitalsConfig? vitals = null)
    {
        var rows = Enumerable.Range(0, 14).Select(y => y is 0 or 13 ? new string('^', 40) : "^" + new string('.', 38) + "^").ToList();
        var resources = new List<ResourcePlacement>();
        for (int x = 3; x < 37; x += 4) { resources.Add(new ResourcePlacement(x, 2, ResourceKind.Food, 20)); resources.Add(new ResourcePlacement(x, 11, ResourceKind.Wood, 50)); }
        var map = MapOverride.FromRows(rows, [.. resources]);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 17, MapWidth = 40, MapHeight = 14, Map = map, Species = Default,
            Vitals = vitals ?? new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = town ?? SettlementConfig.Default,
            Skills = skills ?? SkillConfig.Default,
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

    // One reader in a town with nothing to do (no farm plots, storage full).
    private static (SimulationWorld World, long Reader, long Cursor) Library()
    {
        var world = World(SettlementConfig.Default with { FarmPlots = 0 });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 12, 6, Faction.Human, "Quill"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 6, Human));
        world.Step();
        world.SetStorageForTest(1, 500, 500, 100);
        long reader = world.ReadSnapshot().Chunks.SelectMany(c => c).Single().Id.Value;
        return (world, reader, world.ReadEvents(0, 1).LatestSequence);
    }

    private static void InterruptedReading()
    {
        var (world, reader, cursor) = Library();
        var events = new List<WorldEvent>();
        long readStarted = -1;
        for (int t = 0; t < 40 && readStarted < 0; t++)
        {
            var batch = Run(world, 1, ref cursor);
            events.AddRange(batch);
            var start = batch.FirstOrDefault(e => e.Kind == EventKinds.ActionStarted && e.Actor == reader && e.Reason == (int)ActionKind.Read);
            if (start.Kind == EventKinds.ActionStarted) readStarted = start.Tick;
        }
        Check(readStarted > 0, "the idle member started reading");
        events.AddRange(Run(world, 25, ref cursor));
        // A wolf walks into town: the reader (the town's only member, hence its guard) stops to defend.
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 15, 6, Wolf));
        events.AddRange(Run(world, 3000, ref cursor));
        var alert = events.First(e => e.Kind == EventKinds.GuardAlert && e.Actor == reader);
        var progress = events.Where(e => e.Kind == EventKinds.ReadProgress && e.Actor == reader).ToList();
        var first = progress[0];
        Check(first.Tick == alert.Tick && first.Reason == 0 && first.Amount == alert.Tick - (readStarted + 1),
            $"interrupted session credited {first.Amount} pages = ticks read ({alert.Tick} − {readStarted + 1})");
        Check(progress.Count >= 3 && progress[^1].Reason == 1, "reading resumed in later sessions and finished the book");
        Check(progress.Sum(e => e.Amount) == SkillConfig.Default.BookPages, $"pages credited {progress.Sum(e => e.Amount)} = book length");
        var skills = world.ReadSkills(W, new EntityId(reader))!;
        Check(skills.Scholarship == SkillConfig.Default.BookPages && skills.BooksFinished == 1, "Scholarship XP equals pages read, once");
        long lastTick = progress[^1].Tick;
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == reader && e.Reason == (int)ActionKind.Read && e.Tick > lastTick),
            "a finished book is never read again");
        Check(events.Any(e => e.Kind == EventKinds.SkillLevelUp && e.Actor == reader && e.Reason == (int)Skill.Scholarship), "scholarship levelled up");
    }

    private static void WorkXp()
    {
        var world = World(skills: SkillConfig.Default with { ScholarBonusPercentPerLevel = 0 }, vitals: VitalsConfig.Default with { HungerPerTick = 1500 });
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 6, Faction.Demon, "Forge"));
        for (int k = 0; k < 6; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 18 + k, 7, Demon));
        world.Step();
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 2500, ref cursor);
        int checkedMembers = 0;
        foreach (var row in world.ReadSnapshot().Chunks.SelectMany(c => c))
        {
            var s = world.ReadSkills(W, row.Id)!;
            long Units(int kind, ResourceKind? res = null) => events.Where(e => e.Kind == kind && e.Actor == row.Id.Value && (res is null || e.Reason == (int)res)).Sum(e => e.Amount);
            Check(s.Farming == Units(EventKinds.Harvested) * 10, $"#{row.Id} farming XP {s.Farming}");
            Check(s.Foraging == Units(EventKinds.Gathered, ResourceKind.Food) * 10, $"#{row.Id} foraging XP {s.Foraging}");
            Check(s.Woodcutting == Units(EventKinds.Gathered, ResourceKind.Wood) * 10, $"#{row.Id} woodcutting XP {s.Woodcutting}");
            checkedMembers++;
        }
        Check(checkedMembers == 6 && events.Any(e => e.Kind == EventKinds.Harvested) && events.Any(e => e.Kind == EventKinds.Gathered), "members worked");
    }

    private static void SkillEffect()
    {
        int MaxHarvest(bool skilled)
        {
            var world = World(vitals: VitalsConfig.Default with { HungerPerTick = 500 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 6, Faction.Human, "Fields"));
            for (int k = 0; k < 3; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 19 + k, 7, Human));
            world.Step();
            if (skilled)
                foreach (var row in world.ReadSnapshot().Chunks.SelectMany(c => c)) world.SetSkillForTest(row.Id.Value, Skill.Farming, 10_000); // level 10
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            return Run(world, 400, ref cursor).Where(e => e.Kind == EventKinds.Harvested).Select(e => (int)e.Amount).DefaultIfEmpty(0).Max();
        }
        int novice = MaxHarvest(false), expert = MaxHarvest(true);
        Check(novice <= SettlementConfig.Default.CarryCapacity && expert > SettlementConfig.Default.CarryCapacity, $"novice {novice}, expert {expert}");
    }

    private static void SaveMidRead()
    {
        var (world, _, cursor) = Library();
        Run(world, 40, ref cursor);
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        for (int t = 0; t < 500; t++) { world.Step(); loaded.Step(); }
        Check(loaded.StateHash() == world.StateHash(), "reading continues identically after load");
    }
}
