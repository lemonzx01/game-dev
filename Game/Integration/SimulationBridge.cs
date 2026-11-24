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
// Pause, speed, single-step, selection and following are host concerns:
// none of them enters the command stream (A007 §3).
public partial class SimulationBridge : Node
{
    [Export] public int TicksPerSecond = 10;
    [Export] public int MaxTicksPerFrame = 10;
    [Export] public int EventPageLimit = 256;
    [Export] public int MaxEventPagesPerFrame = 8;
    [Export] public ulong Seed = 1;
    // Opening population placed through ordinary God commands (0 = one test entity, as in T007).
    [Export] public int StarterAnimals = 0;
    // Settlers per starter town (human west, demon east); used only when StarterAnimals > 0.
    [Export] public int StarterSettlers = 10;

    private static readonly WorldId World = new(1);
    private const long ScenarioAuthor = 1;

    private SimulationWorld simulation = null!;
    public HostClock Clock { get; private set; } = null!;
    public MapView Map { get; private set; } = null!;
    public WorldSnapshot PreviousSnapshot { get; private set; } = null!;
    internal string StateHash() => simulation.StateHash();
    private long eventCursor;

    public WorldSnapshot Snapshot { get; private set; } = null!;
    public ImmutableArray<WorldEvent> EventsThisFrame { get; private set; } = ImmutableArray<WorldEvent>.Empty;
    public long EventCursor => eventCursor;
    public int TicksRanThisFrame { get; private set; }
    public double BacklogSeconds => (double)Clock.Backlog / TicksPerSecond;
    public (long From, long Through)? LastGap { get; private set; }
    public ImmutableSortedDictionary<int, SpeciesDef> Species => SpeciesCatalog.Default;
    /// <summary>Bumped whenever the terrain view changes (God terrain edits, load) so the renderer rebuilds its texture.</summary>
    public int MapRevision { get; private set; }
    /// <summary>Bumped when the whole world is replaced by a load; observers drop selections and caches.</summary>
    public int WorldGeneration { get; private set; }
    /// <summary>Outcomes of commands the player issued this frame (sequence → event), for the feed.</summary>
    public ImmutableArray<(GameCommand Command, WorldEvent Outcome)> PlayerOutcomesThisFrame { get; private set; } = [];
    private readonly System.Collections.Generic.Dictionary<long, GameCommand> playerCommands = new(); // lookup by sequence only

    public override void _Ready()
    {
        Clock = new HostClock(TicksPerSecond, MaxTicksPerFrame, System.Math.Max(64, MaxTicksPerFrame));
        simulation = new SimulationWorld(new SimulationOptions
        {
            WorldId = World, Seed = Seed, Species = SpeciesCatalog.Default,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(ScenarioAuthor, AuthorityGrant.God)
        });
        Map = simulation.ReadMap();
        if (StarterAnimals <= 0) SpawnOneNearCenter();
        else
        {
            if (StarterSettlers > 0) FoundStarterTowns(StarterSettlers);
            SpawnStarterPopulation(StarterAnimals);
        }
        PreviousSnapshot = Snapshot = simulation.ReadSnapshot();
    }

    // T007 opening scenario: one deer near the centre.
    private void SpawnOneNearCenter()
    {
        int center = Map.Height / 2 * Map.Width + Map.Width / 2;
        for (int offset = 0; offset < Map.Cells.Length; offset++)
        {
            int cell = (center + offset) % Map.Cells.Length;
            if (Map.Cells[cell] is Terrain.Water or Terrain.Mountain) continue;
            simulation.SubmitCommand(new SpawnEntityCommand(World, ScenarioAuthor, CommandModes.God, cell % Map.Width, cell / Map.Width, 1));
            break;
        }
    }

    // Mixed herd spread over land in a fixed pattern (deterministic for a given seed); arrives on tick 1.
    // T015 opening: a human town in the west and a demon town in the east, each with ten settlers, founded through
    // ordinary God commands in the same tick (founding runs first, so the settlers join on arrival).
    private void FoundStarterTowns(int settlersEach)
    {
        var sites = new[] { (Faction.Human, "Ashford", Map.Width / 4), (Faction.Demon, "Cinderhold", Map.Width * 3 / 4) };
        foreach (var (faction, name, column) in sites)
        {
            if (FindTownSite(column) is not { } site) continue;
            simulation.SubmitCommand(new FoundSettlementCommand(World, ScenarioAuthor, CommandModes.God, site.X, site.Y, faction, name));
            int species = faction == Faction.Human ? SpeciesCatalog.Human : SpeciesCatalog.Demon;
            for (int k = 0; k < settlersEach; k++)
            {
                int x = site.X - 2 + k % 5, y = site.Y + 1 + k / 5;
                if (x < 0 || y >= Map.Height || Map.At(x, y) is Terrain.Water or Terrain.Mountain) { x = site.X; y = site.Y; }
                simulation.SubmitCommand(new SpawnEntityCommand(World, ScenarioAuthor, CommandModes.God, x, y, species, k % 2 == 0 ? Sex.Female : Sex.Male));
            }
        }
    }

    // First grass tile, scanning outward from the map's middle row in the given column, with grass around it.
    private (int X, int Y)? FindTownSite(int column)
    {
        for (int d = 0; d < Map.Height / 2; d++)
            foreach (int y in new[] { Map.Height / 2 - d, Map.Height / 2 + d })
                for (int dx = -6; dx <= 6; dx++)
                {
                    int x = column + dx;
                    if (x < 3 || y < 3 || x >= Map.Width - 3 || y >= Map.Height - 4) continue;
                    int grass = 0;
                    for (int oy = -2; oy <= 2; oy++)
                        for (int ox = -2; ox <= 2; ox++)
                            if (Map.At(x + ox, y + oy) == Terrain.Grass) grass++;
                    if (Map.At(x, y) == Terrain.Grass && grass >= 18) return (x, y);
                }
        return null;
    }

    private void SpawnStarterPopulation(int count)
    {
        int[] mix = [SpeciesCatalog.Deer, SpeciesCatalog.Rabbit, SpeciesCatalog.Sheep, SpeciesCatalog.Rabbit, SpeciesCatalog.Deer, SpeciesCatalog.Rabbit, SpeciesCatalog.Sheep, SpeciesCatalog.Wolf];
        int placed = 0;
        int step = System.Math.Max(1, Map.Cells.Length / (count * 2)) | 1; // odd stride spreads the herd over the whole map
        for (int cell = 0; cell < Map.Cells.Length && placed < count; cell += step)
        {
            if (Map.Cells[cell] is not (Terrain.Grass or Terrain.Forest)) continue;
            var receipt = simulation.SubmitCommand(new SpawnEntityCommand(World, ScenarioAuthor, CommandModes.God, cell % Map.Width, cell / Map.Width, mix[placed % mix.Length]));
            if (receipt.Status != "Queued") break; // command queue full: stop rather than drop silently
            placed++;
        }
    }

    public override void _Process(double delta)
    {
        TicksRanThisFrame = Clock.Update(delta);
        for (int i = 0; i < TicksRanThisFrame; i++)
            simulation.Step();
        if (TicksRanThisFrame > 0)
        {
            PreviousSnapshot = Snapshot;
            Snapshot = simulation.ReadSnapshot();
        }
        DrainEvents();
        ReportPlayerOutcomes();
    }

    // Matches drained outcome events to commands the player issued; refreshes the map after applied terrain edits.
    private void ReportPlayerOutcomes()
    {
        if (playerCommands.Count == 0) { PlayerOutcomesThisFrame = []; return; }
        var found = ImmutableArray.CreateBuilder<(GameCommand, WorldEvent)>();
        foreach (var e in EventsThisFrame)
        {
            if (e.Kind is not (EventKinds.CommandApplied or EventKinds.CommandRejected) || !playerCommands.Remove(e.CommandSequence, out var command))
                continue;
            found.Add((command, e));
            if (command is SetTerrainCommand && e.Kind == EventKinds.CommandApplied) RefreshMap();
        }
        PlayerOutcomesThisFrame = found.ToImmutable();
    }

    private void RefreshMap()
    {
        Map = simulation.ReadMap();
        MapRevision++;
    }

    // ---- God tools: the player acts only through ordinary, validated God commands ----

    public CommandReceipt SpawnAt(int x, int y, int species) =>
        Submit(new SpawnEntityCommand(World, ScenarioAuthor, CommandModes.God, x, y, species));

    public CommandReceipt FoundAt(int x, int y, Faction faction, string name) =>
        Submit(new FoundSettlementCommand(World, ScenarioAuthor, CommandModes.God, x, y, faction, name));

    public CommandReceipt PaintTerrain(int x, int y, Terrain terrain) =>
        Submit(new SetTerrainCommand(World, ScenarioAuthor, CommandModes.God, x, y, terrain));

    private CommandReceipt Submit(GameCommand command)
    {
        var receipt = simulation.SubmitCommand(command);
        if (receipt.CommandSequence is long sequence) playerCommands[sequence] = command;
        return receipt;
    }

    // ---- Save / load (between frames on the owner thread; never inside Step) ----

    public static string QuickSavePath => ProjectSettings.GlobalizePath("user://saves/quicksave.fws");

    public void SaveTo(string path)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        SaveFiles.Write(simulation, path); // temp + verify + replace with .bak
    }

    /// <summary>Replaces the world with a saved one. Pending receipts, cursors and caches of the old world are dropped (A007 §3).</summary>
    public void LoadFrom(string path)
    {
        var loaded = SaveFiles.Read(path); // throws SaveFormatException and leaves the current world untouched on failure
        simulation = loaded;
        playerCommands.Clear();
        eventCursor = simulation.ReadEvents(0, 1).LatestSequence; // old events belong to the replaced timeline view
        PreviousSnapshot = Snapshot = simulation.ReadSnapshot();
        EventsThisFrame = [];
        PlayerOutcomesThisFrame = [];
        LastGap = null;
        RefreshMap();
        WorldGeneration++;
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

    // ---- Read-only queries for the observer UI (never mutate, never grant NPC knowledge) ----

    public EntityDetails? Details(long id) => simulation.ReadEntityDetails(World, new EntityId(id));

    public EntityMindView? Mind(long id) => simulation.ReadEntityMind(World, new EntityId(id));

    public HistoryPage History(long after, int limit) => simulation.ReadHistory(after, limit);

    public System.Collections.Generic.IReadOnlyList<SettlementView> Settlements() => simulation.ReadSettlements();

    public System.Collections.Generic.IReadOnlyList<(int X, int Y, int Crop)> FarmPlots(int id) => simulation.ReadFarmPlots(id);

    public System.Collections.Generic.IReadOnlyList<ResourceView> ResourceNodes() => simulation.ReadResourceNodes();

    public int TicksPerDay => simulation.Options.Calendar.TicksPerDay;

    public string SpeciesName(int key) => Species.TryGetValue(key, out var d) ? d.Name : $"Species {key}";
}
