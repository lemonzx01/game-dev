using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T010 acceptance: personalities choose differently, danger changes emotion, memory/relationship caps,
/// eviction order and durable history (ARCHITECTURE §9 M5).</summary>
internal static class T010Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T010 bold and timid deer choose differently under the same inputs", Personalities),
        ("T010 attack raises fear, memory and a one-way grudge; fear decays", DangerEmotion),
        ("T010 memory cap 64 with (importance, lastRelevant, id) eviction", MemoryCap),
        ("T010 relationship cap 32, weakest evicted, directional", RelationshipCap),
        ("T010 durable history survives working-memory eviction", DurableHistory),
        ("T010 children inherit parent traits within jitter", Inheritance),
        ("T010 remembered danger steers wandering", DangerAvoidance),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static readonly VitalsConfig Calm = new(0, 0, 0, 0, 0, 250_000, 100_000, 4);
    private static readonly MindConfig QuietMind = MindConfig.Default with { FatiguePerTick = 0 };

    private static SimulationWorld World(string[] rows, VitalsConfig vitals, ImmutableSortedDictionary<int, SpeciesDef>? species = null,
        PsycheConfig? psyche = null, params ResourcePlacement[] resources)
    {
        var map = MapOverride.FromRows(rows, resources);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 3, MapWidth = map.Width, MapHeight = map.Height, Map = map, Vitals = vitals, Mind = QuietMind,
            Species = species ?? Default, Psyche = psyche ?? PsycheConfig.Default,
            Ecology = EcologyConfig.Default with { LifespanJitterPercent = 0 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static long Spawn(SimulationWorld world, int x, int y, int species, Sex sex = Sex.Unspecified)
    {
        var receipt = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species, sex));
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

    private static EntityMindView Mind(SimulationWorld world, long id) => world.ReadEntityMind(W, new EntityId(id))!;

    // Wolf penned at (10,6); the deer stands on food at (2,3): Chebyshev 8 (in sight), Manhattan 11 (not close).
    private static readonly string[] Meadow =
    [
        "^^^^^^^^^^^^^^",
        "^............^",
        "^............^",
        "^............^",
        "^............^",
        "^........^^^.^",
        "^........^.^.^",
        "^........^^^.^",
        "^^^^^^^^^^^^^^",
    ];

    private static void Personalities()
    {
        ActionKind FirstChoice(int bravery)
        {
            var world = World(Meadow, Calm with { HungerPerTick = 500_000 }, null, null, new ResourcePlacement(2, 3, ResourceKind.Food, 20)); // starving at the first decision
            long wolf = Spawn(world, 10, 6, Wolf);
            long deer = Spawn(world, 2, 3, Deer);
            world.SetTraitsForTest(deer, bravery, 500);
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            var events = Run(world, 30, ref cursor);
            var first = events.First(e => e.Kind == EventKinds.ActionStarted && e.Actor == deer && e.Reason is (int)ActionKind.Eat or (int)ActionKind.Flee);
            return (ActionKind)first.Reason;
        }
        var bold = FirstChoice(900);
        var timid = FirstChoice(100);
        Check(bold == ActionKind.Eat && timid == ActionKind.Flee, $"bold chose {bold}, timid chose {timid}");
    }

    private static void DangerEmotion()
    {
        string[] pen = ["^^^^^", "^...^", "^^^^^"];
        var world = World(pen, Calm with { HungerPerTick = 100_000 });
        long deer = Spawn(world, 1, 1, Deer);
        world.SetTraitsForTest(deer, 1000, 500); // fearless baseline so the attack is what raises fear
        Check(Mind(world, deer).Fear == 0, "calm before danger");
        long wolf = Spawn(world, 3, 1, Wolf);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        List<WorldEvent> events = [];
        for (int t = 0; t < 80 && !events.Any(e => e.Kind == EventKinds.Attacked); t++) events.AddRange(Run(world, 1, ref cursor));
        var attack = events.First(e => e.Kind == EventKinds.Attacked);
        Check(attack.Actor == wolf && attack.Target == deer, "wolf attacked deer");
        var mind = Mind(world, deer);
        Check(mind.Fear >= 990, $"attack raised fear to {mind.Fear}");
        Check(mind.Memories.Any(m => m.Kind == MemoryKind.WasAttacked && m.Subject == wolf), "attack remembered");
        Check(mind.Relationships.Single(r => r.Other == wolf).Affinity < 0, "grudge toward the attacker");
        Check(Mind(world, wolf).Relationships.All(r => r.Other != deer), "attacker's view unchanged (directional)");

        var calm = World(pen, Calm);
        long d2 = Spawn(calm, 2, 1, Deer);
        calm.RaiseFearForTest(d2, 1000);
        long c2 = calm.ReadEvents(0, 1).LatestSequence;
        Run(calm, 100, ref c2);
        Check(Mind(calm, d2).Fear == 1000 - 100 * PsycheConfig.Default.FearDecayPerTick, "fear decays with elapsed time");
    }

    private static void MemoryCap()
    {
        var world = World(["^^^^^", "^...^", "^^^^^"], Calm);
        long id = Spawn(world, 2, 1, Deer);
        for (int k = 0; k < 64; k++) world.RememberForTest(id, MemoryKind.FoundFood, 0, k, 0); // importance 100, same tick
        var full = Mind(world, id).Memories;
        Check(full.Count == 64, "filled to cap");
        world.RememberForTest(id, MemoryKind.FoundFood, 0, 5, 0); // duplicate refreshes, does not add
        Check(Mind(world, id).Memories.Count == 64, "duplicate refreshed in place");
        world.RememberForTest(id, MemoryKind.SawThreat, 99, 9, 9); // importance 300 evicts the lowest-id FoundFood
        var after = Mind(world, id).Memories;
        Check(after.Count == 64 && after.Any(m => m.Kind == MemoryKind.SawThreat), "important memory kept at cap");
        Check(!after.Any(m => m.Id == full.Min(f => f.Id)) && after.Any(m => m.X == 5 && m.Y == 0), "lowest (importance, tick, id) evicted; refreshed one kept");
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 1, ref cursor);
        world.RememberForTest(id, MemoryKind.FoundFood, 0, 1, 0); // refresh x=1 to a later tick
        world.RememberForTest(id, MemoryKind.SawThreat, 98, 8, 8);
        var later = Mind(world, id).Memories;
        Check(later.Any(m => m.X == 1 && m.Y == 0) && !later.Any(m => m.X == 2 && m.Y == 0), "older LastRelevantTick evicted before refreshed");
    }

    private static void RelationshipCap()
    {
        var world = World(["^^^^^", "^...^", "^^^^^"], Calm);
        long a = Spawn(world, 1, 1, Deer);
        long b = Spawn(world, 3, 1, Deer);
        for (int k = 0; k < 40; k++) world.RelateForTest(a, 1000 + k, k == 0 ? 900 : 10); // other 1000 is strongest
        var rel = Mind(world, a).Relationships;
        Check(rel.Count == 32, "capped at 32");
        Check(rel.Any(r => r.Other == 1000) && rel.Select(r => r.Other).SequenceEqual(rel.Select(r => r.Other).Order()), "strong bond kept; sorted by id");
        Check(!rel.Any(r => r.Other == 1001), "weakest, oldest, lowest id evicted first");
        world.RelateForTest(a, b, 100);
        Check(Mind(world, b).Relationships.All(r => r.Other != a), "relationship is directional");
    }

    private static void DurableHistory()
    {
        var hare = new SpeciesDef(1, "Hare", Diet.Herbivore, false, 1, 2, 0, 0, [], [],
            MaturityTicks: 20, LifespanTicks: 4000, GestationTicks: 10, ReproCooldownTicks: 3000, LitterSize: 1, LocalDensityCap: 5, BirthHungerCost: 1);
        var world = World(["^^^^^^^", "^.....^", "^^^^^^^"], Calm, Build([hare]), PsycheConfig.Default with { MemoryCap = 2 });
        long mother = Spawn(world, 1, 1, 1, Sex.Female);
        Spawn(world, 2, 1, 1, Sex.Male);
        world.SetTraitsForTest(mother, 500, 1000);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var born = Run(world, 60, ref cursor).First(e => e.Kind == EventKinds.EntityBorn);
        Check(Mind(world, mother).Memories.Any(m => m.Kind == MemoryKind.BecameParent), "parenthood in working memory");
        for (int k = 0; k < 3; k++) world.RememberForTest(mother, MemoryKind.BecameParent, 500 + k, k, 0); // push it out
        Check(!Mind(world, mother).Memories.Any(m => m.Subject == born.Target), "working memory evicted the birth");
        Check(world.ReadHistory(0, 256).Records.Any(r => r.Kind == EventKinds.EntityBorn && r.Target == born.Target), "durable milestone remains");
        Check(world.ReadEntityDetails(W, new EntityId(born.Target))!.MotherId == mother, "parentage remains durable");
    }

    private static void Inheritance()
    {
        var hare = new SpeciesDef(1, "Hare", Diet.Herbivore, false, 1, 2, 0, 0, [], [],
            MaturityTicks: 20, LifespanTicks: 4000, GestationTicks: 10, ReproCooldownTicks: 10, LitterSize: 2, LocalDensityCap: 20, BirthHungerCost: 1);
        var world = World(["^^^^^^^^", "^......^", "^......^", "^^^^^^^^"], Calm, Build([hare]));
        long mother = Spawn(world, 1, 1, 1, Sex.Female);
        long father = Spawn(world, 2, 1, 1, Sex.Male);
        world.SetTraitsForTest(mother, 200, 800);
        world.SetTraitsForTest(father, 400, 1000);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var children = Run(world, 80, ref cursor).Where(e => e.Kind == EventKinds.EntityBorn && e.Actor == mother).Select(e => e.Target).ToList();
        Check(children.Count >= 2, "children born");
        int j = PsycheConfig.Default.TraitJitter;
        foreach (long c in children)
        {
            var m = Mind(world, c);
            Check(Math.Abs(m.Bravery - 300) <= j && Math.Abs(m.Sociability - 900) <= j, $"child {c} traits {m.Bravery}/{m.Sociability} near parents' mean");
            Check(m.Relationships.Any(r => r.Other == mother && r.Affinity > 0), "child bonded to mother");
        }
        Check(Mind(world, mother).Relationships.Any(r => r.Other == father && r.Affinity > 0), "mates bonded");
    }

    private static void DangerAvoidance()
    {
        // A 1-tile corridor: with danger remembered east of the start, wandering never goes back east.
        var world = World(["^^^^^^^^^", "^.......^", "^^^^^^^^^"], Calm, ImmutableSortedDictionary<int, SpeciesDef>.Empty);
        long id = Spawn(world, 4, 1, 9);
        world.RememberForTest(id, MemoryKind.SawThreat, 0, 6, 1);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        int maxX = 0;
        bool moved = false;
        for (int t = 0; t < 200; t++)
        {
            Run(world, 1, ref cursor);
            var row = world.ReadSnapshot().Chunks.SelectMany(c => c).Single();
            moved |= row.X != 4;
            if (moved) maxX = Math.Max(maxX, row.X);
        }
        Check(moved && maxX <= 3, $"wandered but stayed away from remembered danger (max x {maxX})");
    }
}
