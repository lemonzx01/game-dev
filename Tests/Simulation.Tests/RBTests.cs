using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>RB ecology balance: breeding seasons and the shared realism preset (balance numbers are measured with Tests/WorldReport).</summary>
internal static class RBTests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("RB wild animals breed only in spring and summer; people all year", BreedingSeasons),
        ("RB realism preset sets the game's ecology", Preset),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    // Short calendar (100-tick days, 2-day seasons): spring 0–199, summer 200–399, autumn 400–599, winter 600–799.
    private static List<WorldEvent> MateStarts(int species, long fromTick, int ticks)
    {
        var rows = Enumerable.Range(0, 10).Select(y => y is 0 or 9 ? new string('^', 20) : "^" + new string('.', 18) + "^").ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 5, MapWidth = 20, MapHeight = 10, Map = MapOverride.FromRows(rows, []), Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Calendar = new CalendarConfig(100, 2),
            Body = BodyConfig.Realistic with { DiurnalStart = 999, DiurnalEnd = 0 }, // keep them awake: sleep is not under test
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        while (world.ReadSnapshot().Tick < fromTick) world.Step();
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 9, 4, species, Sex.Female));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 4, species, Sex.Male));
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        var all = new List<WorldEvent>();
        for (int i = 0; i < ticks; i++)
        {
            world.Step();
            var page = world.ReadEvents(cursor, 256);
            all.AddRange(page.Records);
            cursor = page.NextCursor;
        }
        return all.Where(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Mate).ToList();
    }

    private static void BreedingSeasons()
    {
        Check(MateStarts(Deer, 1, 180).Count > 0, "deer court in spring");
        Check(MateStarts(Deer, 400, 380).Count == 0, "deer do not court in autumn or winter");
        Check(MateStarts(Human, 400, 380).Count > 0, "people court in any season");
    }

    private static void Preset()
    {
        var o = Realism.Apply(new SimulationOptions { WorldId = W });
        Check(o.Body.Enabled && o.Body.SeasonalBreeding, "Body and breeding seasons on");
        Check(o.Ecology.CarcassDecayTicks == 4800, "carcasses last two days");
        Check(o.Climate.WinterGrowthPercent == 60 && o.Climate.MinGrowthTemperature == -3, "winter forage");
        Check(o.Resources.Food.RegrowIntervalTicks == 240, "berry regrowth");
        var plain = new SimulationOptions { WorldId = W };
        Check(!plain.Body.Enabled && plain.Ecology.CarcassDecayTicks == EcologyConfig.Default.CarcassDecayTicks, "scenario defaults are untouched");
    }
}
