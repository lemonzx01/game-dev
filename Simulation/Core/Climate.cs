namespace FantasyWorld.Simulation;

/// <summary>
/// T012 climate. Temperature (whole °C) is a pure function of tick and tile: the season's base, a daily swing
/// (warmest at midday, coldest at midnight) and a terrain offset. Plant (Food) and Wood regrowth intervals scale with
/// the season's growth percent (0 = dormant) and stop on tiles colder than MinGrowthTemperature. Water is unaffected.
/// Values are provisional balance numbers (GDD open decision: seasonal duration and effects).
/// </summary>
public sealed record ClimateConfig(
    int SpringTemperature = 12, int SummerTemperature = 24, int AutumnTemperature = 10, int WinterTemperature = -4,
    int DailySwing = 5, int SnowOffset = -8, int MountainOffset = -6, int WaterOffset = 2, int ForestOffset = -1,
    int SpringGrowthPercent = 150, int SummerGrowthPercent = 100, int AutumnGrowthPercent = 60, int WinterGrowthPercent = 0,
    int MinGrowthTemperature = 2)
{
    public static ClimateConfig Default { get; } = new();

    internal void Validate()
    {
        foreach (int t in new[] { SpringTemperature, SummerTemperature, AutumnTemperature, WinterTemperature, SnowOffset, MountainOffset, WaterOffset, ForestOffset, MinGrowthTemperature })
            if (t is < -100 or > 100) throw new ArgumentOutOfRangeException(nameof(ClimateConfig), "Temperatures must be within ±100 °C.");
        ArgumentOutOfRangeException.ThrowIfNegative(DailySwing);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(DailySwing, 50);
        foreach (int g in new[] { SpringGrowthPercent, SummerGrowthPercent, AutumnGrowthPercent, WinterGrowthPercent })
        {
            ArgumentOutOfRangeException.ThrowIfNegative(g);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(g, 1000);
        }
    }

    public int SeasonTemperature(Season s) => s switch
    {
        Season.Spring => SpringTemperature,
        Season.Summer => SummerTemperature,
        Season.Autumn => AutumnTemperature,
        _ => WinterTemperature,
    };

    public int GrowthPercent(Season s) => s switch
    {
        Season.Spring => SpringGrowthPercent,
        Season.Summer => SummerGrowthPercent,
        Season.Autumn => AutumnGrowthPercent,
        _ => WinterGrowthPercent,
    };

    public int TerrainOffset(Terrain t) => t switch
    {
        Terrain.Snow => SnowOffset,
        Terrain.Mountain => MountainOffset,
        Terrain.Water => WaterOffset,
        Terrain.Forest => ForestOffset,
        _ => 0,
    };

    /// <summary>Open-air temperature (grass, no terrain offset) at a tick.</summary>
    public int AirTemperature(long tick, CalendarConfig calendar)
    {
        var time = WorldTime.From(tick, calendar);
        int half = Math.Max(1, calendar.TicksPerDay / 2);
        int fromNoon = Math.Abs(time.TickOfDay - half);
        int daily = DailySwing - 2 * DailySwing * Math.Min(fromNoon, half) / half;
        return SeasonTemperature(time.Season) + daily;
    }

    internal void WriteCanonical(BinaryWriter w)
    {
        foreach (int v in new[] { SpringTemperature, SummerTemperature, AutumnTemperature, WinterTemperature, DailySwing, SnowOffset, MountainOffset,
                     WaterOffset, ForestOffset, SpringGrowthPercent, SummerGrowthPercent, AutumnGrowthPercent, WinterGrowthPercent, MinGrowthTemperature })
            w.Write(v);
    }
}
