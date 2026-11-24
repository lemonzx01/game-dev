using FantasyWorld.Simulation;
using System.Collections.Immutable;

/// <summary>T012 acceptance: predictable season progression, temperature, seasonal growth differs, season reported.</summary>
internal static class T012Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);
    private static readonly CalendarConfig Short = new(10, 30); // 300-tick seasons, 1200-tick years

    public static (string, Action)[] Cases =>
    [
        ("T012 seasons progress predictably through years", Progression),
        ("T012 temperature by season, time of day and terrain", Temperature),
        ("T012 seasonal regrowth differs; winter dormant", SeasonalGrowth),
        ("T012 cold tiles do not regrow", ColdTiles),
        ("T012 season and temperature in snapshots and saves", Reporting),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static SimulationWorld World(ClimateConfig? climate = null, params ResourcePlacement[] resources)
    {
        var map = MapOverride.FromRows(["^^^^^^", "^..*.~", "^.T..^", "^^^^^^"], resources);
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 1, MapWidth = map.Width, MapHeight = map.Height, Map = map, Calendar = Short,
            Climate = climate ?? ClimateConfig.Default, Mind = MindConfig.Disabled,
            Resources = ResourceConfig.Default with { Food = new ResourceRule(1000, 1, 10) },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static void Progression()
    {
        var world = World();
        var seen = new List<(long Tick, Season Season, long Year)>();
        Season? last = null;
        for (int t = 0; t <= 2 * 1200; t++)
        {
            var time = world.ReadSnapshot().Time;
            Check(time.Season == (Season)(t / 300 % 4) && time.Year == t / 1200 && time.DayOfSeason == t / 10 % 30, $"calendar at tick {t}");
            if (time.Season != last) seen.Add((t, time.Season, time.Year));
            last = time.Season;
            world.Step();
        }
        Check(seen.Select(s => s.Tick).SequenceEqual([0L, 300, 600, 900, 1200, 1500, 1800, 2100, 2400]), "season changes exactly every 300 ticks");
        Check(seen[4] == (1200, Season.Spring, 1), "spring returns in year 1");
    }

    private static void Temperature()
    {
        var c = ClimateConfig.Default;
        int Air(long tick) => c.AirTemperature(tick, Short);
        Check(Air(5) == c.SpringTemperature + c.DailySwing && Air(0) == c.SpringTemperature - c.DailySwing, "midday warmest, midnight coldest");
        Check(Air(305) > Air(5) && Air(5) > Air(905) && Air(605) < Air(305), "summer > spring > winter; autumn cooler than summer");
        var world = World();
        for (int t = 0; t < 905; t++) world.Step(); // winter midday
        int grass = world.ReadTemperature(1, 1), snow = world.ReadTemperature(3, 1), forest = world.ReadTemperature(2, 2);
        Check(grass == Air(905) && snow == grass + c.SnowOffset && forest == grass + c.ForestOffset, $"terrain offsets ({grass}, {snow}, {forest})");
        Check(world.ReadSnapshot().Time.Temperature == grass, "snapshot reports open-air temperature");
    }

    // Stock regrown 280 ticks after draining a 1000-capacity node (+1 per 10-tick base interval) at the start of a season.
    private static int RegrownIn(Season season, ClimateConfig? climate = null)
    {
        var world = World(climate, new ResourcePlacement(1, 1, ResourceKind.Food, 1000));
        while (world.LastCompletedTick < (int)season * 300) world.Step();
        world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, 1, 1, ResourceKind.Food, 1000));
        world.Step();
        for (int t = 0; t < 280; t++) world.Step();
        return world.ReadResource(1, 1, ResourceKind.Food)!.Value.Amount;
    }

    private static void SeasonalGrowth()
    {
        int spring = RegrownIn(Season.Spring), summer = RegrownIn(Season.Summer), autumn = RegrownIn(Season.Autumn), winter = RegrownIn(Season.Winter);
        Check(spring > summer && summer > autumn && autumn > winter && winter == 0, $"spring {spring} > summer {summer} > autumn {autumn} > winter {winter} = 0");
        Check(summer == 28 && spring == 46, $"rates follow 100 % / 150 % of the base interval ({summer}, {spring})");
        // Water ignores seasons.
        var world = World(null, new ResourcePlacement(5, 1, ResourceKind.Water, 1000));
        while (world.LastCompletedTick < 900) world.Step();
        world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, 5, 1, ResourceKind.Water, 500));
        for (int t = 0; t < 201; t++) world.Step();
        Check(world.ReadResource(5, 1, ResourceKind.Water)!.Value.Amount == 600, "water regrows in winter at its base rate");
    }

    private static void ColdTiles()
    {
        int warm = RegrownIn(Season.Summer);
        int cold = RegrownIn(Season.Summer, ClimateConfig.Default with { MinGrowthTemperature = 40 });
        Check(warm > 0 && cold == 0, $"too cold to grow ({warm} vs {cold})");
    }

    private static void Reporting()
    {
        var world = World(null, new ResourcePlacement(1, 1, ResourceKind.Food, 1000));
        for (int t = 0; t < 650; t++) world.Step();
        world.SubmitCommand(new TakeResourceCommand(W, God, CommandModes.God, 1, 1, ResourceKind.Food, 100));
        world.Step();
        var time = world.ReadSnapshot().Time;
        Check(time.Season == Season.Autumn && time.Year == 0, "snapshot season");
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        Check(loaded.ReadSnapshot().Time == time, "season/temperature survive save/load");
        for (int t = 0; t < 400; t++) { world.Step(); loaded.Step(); }
        Check(loaded.StateHash() == world.StateHash() && loaded.ReadSnapshot().Time.Season == Season.Winter, "seasonal growth continues identically after load");
    }
}
