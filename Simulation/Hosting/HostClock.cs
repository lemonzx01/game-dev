namespace FantasyWorld.Simulation;

/// <summary>
/// Host-side pacing (pause, single-step, speed). Never part of world state: it only decides how many
/// whole Step() calls a host makes per frame. When the host cannot keep up it stops accruing real-time
/// debt beyond <c>maxBacklogTicks</c> (effective speed slows) and reports <see cref="Overloaded"/>;
/// it never skips simulation ticks.
/// </summary>
public sealed class HostClock
{
    private readonly int ticksPerSecond;
    private readonly int maxTicksPerUpdate;
    private readonly int maxBacklogTicks;
    private double pending;
    private int singleSteps;
    private int speed = 1;

    public HostClock(int ticksPerSecond = 10, int maxTicksPerUpdate = 8, int maxBacklogTicks = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTicksPerUpdate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBacklogTicks, maxTicksPerUpdate);
        this.ticksPerSecond = ticksPerSecond;
        this.maxTicksPerUpdate = maxTicksPerUpdate;
        this.maxBacklogTicks = maxBacklogTicks;
    }

    public bool Paused { get; set; }
    public bool Overloaded { get; private set; }
    public int Backlog => (int)pending;

    public int Speed
    {
        get => speed;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 64);
            speed = value;
        }
    }

    /// <summary>While paused, the next Update returns exactly one tick per request.</summary>
    public void RequestSingleStep() => singleSteps++;

    /// <summary>Returns how many Step() calls to make this frame.</summary>
    public int Update(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (Paused)
        {
            pending = 0;
            Overloaded = false;
            if (singleSteps == 0) return 0;
            singleSteps--;
            return 1;
        }
        singleSteps = 0;
        pending += elapsedSeconds * ticksPerSecond * speed;
        Overloaded = pending > maxBacklogTicks;
        if (Overloaded) pending = maxBacklogTicks;
        int run = (int)Math.Min(Math.Floor(pending), maxTicksPerUpdate);
        pending -= run;
        return run;
    }
}
