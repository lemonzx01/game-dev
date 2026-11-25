using System.Collections.Immutable;
using FantasyWorld.Simulation;

// Keep placement, command order and species mix in sync with SimulationBridge._Ready.
internal sealed class Opening(SimulationWorld simulation, MapView Map)
{
    private static readonly WorldId World = new(1);
    private const long ScenarioAuthor = 1;

    public static SimulationWorld Create(Settings settings)
    {
        var options = new SimulationOptions
        {
            WorldId = World, Seed = settings.Seed, MapWidth = settings.Size, MapHeight = settings.Size,
            Species = settings.Species is { } file ? SpeciesCatalog.FromJson(File.ReadAllText(file)) : SpeciesCatalog.Default, Body = BodyConfig.Default,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(ScenarioAuthor, AuthorityGrant.God)
        };
        var simulation = new SimulationWorld(settings.Body ? Realism.Apply(options) : options); // same realism as the game
        var opening = new Opening(simulation, simulation.ReadMap());
        if (settings.Animals == 0) opening.SpawnOneNearCenter();
        else
        {
            if (settings.Settlers > 0) opening.FoundStarterTowns(settings.Settlers);
            opening.SpawnStarterPopulation(settings.Animals);
        }
        return simulation;
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
                if (x < 0 || x >= Map.Width || y >= Map.Height || Map.At(x, y) is Terrain.Water or Terrain.Mountain) { x = site.X; y = site.Y; }
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
}
