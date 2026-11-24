namespace FantasyWorld.Simulation;

public enum Season { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

/// <summary>Day/season lengths in ticks. Defaults are provisional (GDD open decision: time scale).</summary>
public sealed record CalendarConfig(int TicksPerDay, int DaysPerSeason)
{
    public const int SeasonsPerYear = 4;
    public static CalendarConfig Default { get; } = new(2400, 7);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(TicksPerDay, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DaysPerSeason, 1);
    }
}

/// <summary>Calendar derived purely from the integer tick; all fields are zero-based.</summary>
/// <remarks>Temperature is the open-air temperature (°C) filled in by the world from its climate; 0 when built from the calendar alone.</remarks>
public readonly record struct WorldTime(long Tick, long Day, int TickOfDay, long Year, Season Season, int DayOfSeason, int Temperature = 0)
{
    public static WorldTime From(long tick, CalendarConfig calendar)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        long day = tick / calendar.TicksPerDay;
        long seasonIndex = day / calendar.DaysPerSeason;
        return new(tick, day, (int)(tick % calendar.TicksPerDay), seasonIndex / CalendarConfig.SeasonsPerYear,
            (Season)(seasonIndex % CalendarConfig.SeasonsPerYear), (int)(day % calendar.DaysPerSeason));
    }
}
