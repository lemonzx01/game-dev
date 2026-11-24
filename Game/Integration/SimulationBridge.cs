using System.Collections.Immutable;
using FantasyWorld.Simulation;
using Godot;

namespace Game.Integration;

// Owns the only access point to ISimulation on Godot's main thread (the
// owner thread per ARCHITECTURE.md §3/§8). Advances on a fixed simulation
// tick independent of render frame rate, exposes at most one WorldSnapshot
// per rendered frame, and drains WorldEvent via an exclusive cursor instead
// of reading events off the snapshot (N3; ARCHITECTURE.md §3 "Events and
// recovery"). Camera/UI code must read from here, never the reverse.
public partial class SimulationBridge : Node
{
    [Export] public int TicksPerSecond = 10;
    [Export] public int MaxTicksPerFrame = 10;
    [Export] public int EventPageLimit = 256;
    [Export] public int MaxEventPagesPerFrame = 8;

    private SimulationWorld simulation = null!;
    private HostClock clock = null!;
    public MapView Map { get; private set; } = null!;
    public WorldSnapshot PreviousSnapshot { get; private set; } = null!;
    internal string StateHash() => simulation.StateHash();
    private long eventCursor;

    public WorldSnapshot Snapshot { get; private set; } = null!;
    public ImmutableArray<WorldEvent> EventsThisFrame { get; private set; } = ImmutableArray<WorldEvent>.Empty;
    public long EventCursor => eventCursor;
    public int TicksRanThisFrame { get; private set; }
    public double BacklogSeconds => (double)clock.Backlog / TicksPerSecond;
    public (long From, long Through)? LastGap { get; private set; }

    public override void _Ready()
    {
        clock = new HostClock(TicksPerSecond, MaxTicksPerFrame, System.Math.Max(64, MaxTicksPerFrame));
        var worldId = new WorldId(1);
        const long scenarioAuthor = 1;
        simulation = new SimulationWorld(new SimulationOptions
        {
            WorldId = worldId, Seed = 1,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(scenarioAuthor, AuthorityGrant.God)
        });
        Map = simulation.ReadMap();
        // The opening scenario places one entity through the normal God command queue.
        int center = Map.Height / 2 * Map.Width + Map.Width / 2;
        for (int offset = 0; offset < Map.Cells.Length; offset++)
        {
            int cell = (center + offset) % Map.Cells.Length;
            if (Map.Cells[cell] is Terrain.Water or Terrain.Mountain) continue;
            simulation.SubmitCommand(new SpawnEntityCommand(worldId, scenarioAuthor, CommandModes.God,
                cell % Map.Width, cell / Map.Width, 1));
            break;
        }
        PreviousSnapshot = Snapshot = simulation.ReadSnapshot();
    }

    public override void _Process(double delta)
    {
        TicksRanThisFrame = clock.Update(delta);
        for (int i = 0; i < TicksRanThisFrame; i++)
            simulation.Step();
        if (TicksRanThisFrame > 0)
        {
            PreviousSnapshot = Snapshot;
            Snapshot = simulation.ReadSnapshot();
        }
        DrainEvents();
    }

    private void DrainEvents()
    {
        var drained = ImmutableArray.CreateBuilder<WorldEvent>();
        LastGap = null;
        for (int page = 0; page < MaxEventPagesPerFrame; page++)
        {
            EventPage result = simulation.ReadEvents(eventCursor, EventPageLimit);
            if (result.Status == PageStatus.InvalidRequest)
                break;
            if (result.Status == PageStatus.Gap)
            {
                LastGap = (result.MissingFrom!.Value, result.MissingThrough!.Value);
                eventCursor = result.NextCursor;
                continue;
            }
            drained.AddRange(result.Records);
            bool advanced = result.NextCursor != eventCursor;
            eventCursor = result.NextCursor;
            if (!advanced || eventCursor >= result.LatestSequence)
                break;
        }
        EventsThisFrame = drained.ToImmutable();
    }
}

