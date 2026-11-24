using FantasyWorld.Simulation;
using System.Collections.Immutable;

/// <summary>T011 acceptance: new IDs, valid parents/age, ageing and old-age death, capped reproduction with resource checks.</summary>
internal static class T011Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);
    private const int Hare = 1;

    public static (string, Action)[] Cases =>
    [
        ("T011 birth: new IDs, parents, age, mother pays, milestones", Birth),
        ("T011 juvenile matures; juveniles do not breed", Maturity),
        ("T011 old-age death at lifespan", OldAge),
        ("T011 local density cap blocks conception", DensityCap),
        ("T011 global entity cap limits litter", GlobalCap),
        ("T011 unaffordable birth fails without new entity", Unaffordable),
        ("T011 mother dying loses the pregnancy", PregnancyLost),
        ("T011 breeding ecosystem bounded and deterministic", Population),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static readonly SpeciesDef HareDef = new(Hare, "Hare", Diet.Herbivore, false, 1, 2, 0, 0, [], [],
        MaturityTicks: 20, LifespanTicks: 400, GestationTicks: 10, ReproCooldownTicks: 30, LitterSize: 2, LocalDensityCap: 5, BirthHungerCost: 300_000);

    private static readonly VitalsConfig Calm = new(0, 0, 0, 0, 0, 250_000, 100_000, 4);
    private static readonly MindConfig QuietMind = MindConfig.Default with { FatiguePerTick = 0 };

    private static string[] Field(int w, int h) =>
        Enumerable.Range(0, h).Select(y => y == 0 || y == h - 1 ? new string('^', w) : "^" + new string('.', w - 2) + "^").ToArray();

    private static SimulationWorld World(SpeciesDef hare, VitalsConfig? vitals = null, EcologyConfig? eco = null, int w = 14, int h = 7)
    {
        var map = MapOverride.FromRows(Field(w, h));
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 11, MapWidth = map.Width, MapHeight = map.Height, Map = map,
            Vitals = vitals ?? Calm, Mind = QuietMind,
            Species = SpeciesCatalog.Build([hare]),
            Ecology = eco ?? EcologyConfig.Default with { LifespanJitterPercent = 0 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static long Spawn(SimulationWorld world, int x, int y, Sex sex)
    {
        var receipt = world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, Hare, sex));
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

    private static EntityDetails D(SimulationWorld world, long id) => world.ReadEntityDetails(W, new EntityId(id))!;

    private static void Birth()
    {
        var world = World(HareDef);
        long mother = Spawn(world, 3, 3, Sex.Female);
        long father = Spawn(world, 6, 3, Sex.Male);
        Check(D(world, mother) is { Stage: LifeStage.Adult, Sex: Sex.Female, Age: 21 } && D(world, father).Sex == Sex.Male, "God spawns arrive as adults");
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = new List<WorldEvent>();
        for (int t = 0; t < 80 && events.Count(e => e.Kind == EventKinds.EntityBorn) < 2; t++) events.AddRange(Run(world, 1, ref cursor));
        var conceived = events.First(e => e.Kind == EventKinds.Conceived);
        Check(conceived.Actor == mother && conceived.Target == father, "conception records both parents");
        var births = events.Where(e => e.Kind == EventKinds.EntityBorn).Take(2).ToList();
        Check(births.Count == 2 && births.All(b => b.Tick == conceived.Tick + HareDef.GestationTicks), "litter of 2 after gestation");
        Check(births.Select(b => b.Target).SequenceEqual([father + 1, father + 2]), "children get new, increasing IDs");
        foreach (var b in births)
        {
            var c = D(world, b.Target);
            Check(b.Actor == mother && b.Amount == father && c.MotherId == mother && c.FatherId == father, "parents recorded");
            Check(c.BirthTick == b.Tick && c.Age == world.LastCompletedTick - b.Tick && c.Stage == LifeStage.Juvenile && c.SpeciesKey == Hare, "child age and stage");
        }
        long birthTick = births[0].Tick;
        Check(D(world, mother).Hunger == 2 * HareDef.BirthHungerCost, "mother paid 2 x 300k hunger");
        var history = world.ReadHistory(0, 256).Records.Where(r => r.Kind == EventKinds.EntityBorn).Take(2).ToList();
        Check(history.Select(h => h.Target).SequenceEqual(births.Select(b => b.Target)) && history.All(h => h.Tick == birthTick), "births are durable milestones");
    }

    private static void Maturity()
    {
        var world = World(HareDef);
        long mother = Spawn(world, 3, 3, Sex.Female);
        Spawn(world, 4, 3, Sex.Male);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 40, ref cursor);
        long child = events.First(e => e.Kind == EventKinds.EntityBorn).Target;
        long born = D(world, child).BirthTick;
        while (world.LastCompletedTick < born + HareDef.MaturityTicks - 1)
        {
            var more = Run(world, 1, ref cursor);
            Check(world.StageOfForTest(child) == LifeStage.Juvenile, "juvenile before maturity");
            Check(!more.Any(e => e.Kind == EventKinds.Conceived && e.Actor == child), "juvenile never conceives");
        }
        Run(world, 1, ref cursor);
        Check(world.StageOfForTest(child) == LifeStage.Adult, "adult at maturity");
        Check(D(world, mother).Stage == LifeStage.Adult, "mother still adult");
    }

    private static void OldAge()
    {
        var world = World(HareDef with { LitterSize = 0, LocalDensityCap = 0 });
        long id = Spawn(world, 3, 3, Sex.Female); // spawned at tick 1 as adult: BirthTick = 1 - 20
        long dueTick = 1 - HareDef.MaturityTicks + HareDef.LifespanTicks;
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, (int)(dueTick - 1 - world.LastCompletedTick), ref cursor);
        Check(D(world, id) is { LifeState: LifeStates.Alive, Stage: LifeStage.Elder }, "elder just before lifespan");
        var events = Run(world, 1, ref cursor);
        var death = events.Single(e => e.Kind == EventKinds.EntityDied);
        Check(death.Target == id && death.Reason == (int)DeathCause.OldAge && death.Tick == dueTick, "old-age death exactly at lifespan");
        Check(D(world, id) is { DeathCause: DeathCause.OldAge, Age: 400 }, "durable age at death");
    }

    private static void DensityCap()
    {
        // Field interior 8 x 4, so every hare always sees all 7 (perception radius 8) > cap 5.
        var world = World(HareDef with { LocalDensityCap = 5 }, w: 10, h: 6);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        for (int k = 0; k < 4; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 2 + k, 2, Hare, Sex.Female));
        for (int k = 0; k < 3; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 2 + k, 3, Hare, Sex.Male)); // all 7 arrive together
        var events = Run(world, 200, ref cursor);
        Check(!events.Any(e => e.Kind is EventKinds.Conceived or EventKinds.EntityBorn), "crowded hares do not conceive");
    }

    private static void GlobalCap()
    {
        var world = World(HareDef, eco: EcologyConfig.Default with { LifespanJitterPercent = 0, MaxEntities = 3 });
        Spawn(world, 3, 3, Sex.Female);
        Spawn(world, 5, 3, Sex.Male);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 200, ref cursor);
        Check(events.Count(e => e.Kind == EventKinds.EntityBorn) == 1, "litter of 2 cut to 1 by the cap of 3 entities");
        Check(world.LiveEntityCount <= 3, "never above the global cap");
    }

    private static void Unaffordable()
    {
        // Hunger rises 60k per tick: conception happens below 300k, but by birth (10 ticks later) she cannot pay 300k.
        var world = World(HareDef, Calm with { HungerPerTick = 60_000 });
        long mother = Spawn(world, 3, 3, Sex.Female);
        Spawn(world, 4, 3, Sex.Male);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 25, ref cursor);
        Check(events.Any(e => e.Kind == EventKinds.Conceived && e.Actor == mother), "conceived while fed");
        Check(events.Any(e => e.Kind == EventKinds.BirthFailed && e.Actor == mother), "birth failed for lack of reserves");
        Check(!events.Any(e => e.Kind == EventKinds.EntityBorn) && world.LiveEntityCount == 2, "no child created from nothing");
    }

    private static void PregnancyLost()
    {
        var world = World(HareDef with { GestationTicks = 500 });
        long mother = Spawn(world, 3, 3, Sex.Female);
        Spawn(world, 4, 3, Sex.Male);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var events = Run(world, 450, ref cursor);
        Check(events.Any(e => e.Kind == EventKinds.Conceived && e.Actor == mother), "conceived");
        Check(events.Any(e => e.Kind == EventKinds.EntityDied && e.Target == mother && e.Reason == (int)DeathCause.OldAge), "mother died of old age while pregnant");
        Check(!events.Any(e => e.Kind is EventKinds.EntityBorn or EventKinds.BirthFailed), "no posthumous birth");
    }

    private static void Population()
    {
        (string Hash, int Born, int MaxLive) RunOnce()
        {
            var species = SpeciesCatalog.Default.Values.Select(d => d with
            {
                MaturityTicks = d.MaturityTicks / 60, LifespanTicks = d.LifespanTicks / 60,
                GestationTicks = d.GestationTicks / 60, ReproCooldownTicks = d.ReproCooldownTicks / 60,
            });
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W, Seed = 31, MapWidth = 64, MapHeight = 64, Species = SpeciesCatalog.Build(species),
                Ecology = EcologyConfig.Default with { MaxEntities = 400 },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            var map = world.ReadMap();
            var land = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Where((_, k) => k % 11 == 0).Take(120).ToList();
            int[] mix = [SpeciesCatalog.Rabbit, SpeciesCatalog.Deer, SpeciesCatalog.Rabbit, SpeciesCatalog.Sheep, SpeciesCatalog.Rabbit, SpeciesCatalog.Deer, SpeciesCatalog.Wolf];
            for (int k = 0; k < land.Count; k++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land[k] % 64, land[k] / 64, mix[k % mix.Length]));
            int maxLive = 0;
            for (int t = 0; t < 4000; t++)
            {
                world.Step();
                maxLive = Math.Max(maxLive, world.LiveEntityCount);
            }
            int born = 0;
            for (var page = world.ReadHistory(0, 256); !page.Records.IsEmpty; page = world.ReadHistory(page.NextCursor, 256))
                born += page.Records.Count(r => r.Kind == EventKinds.EntityBorn);
            return (world.StateHash(), born, maxLive);
        }
        var a = RunOnce();
        Check(a == RunOnce(), "same seed and script, same hash with births and deaths");
        Check(a.Born > 0, "animals were born");
        Check(a.MaxLive <= 400, $"live population {a.MaxLive} within cap");
    }
}
