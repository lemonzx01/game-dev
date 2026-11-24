namespace FantasyWorld.Simulation;

public enum ResourceKind : byte { Food = 1, Water = 2, Wood = 3 }

/// <summary>Per-kind stock rule. Amounts are integer units; regrowth adds RegrowAmount every RegrowIntervalTicks up to Capacity.</summary>
public sealed record ResourceRule(int Capacity, int RegrowAmount, int RegrowIntervalTicks)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(RegrowAmount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(RegrowIntervalTicks, 1);
    }
}

/// <summary>Placement chances are per-mille per eligible tile. Defaults are provisional balance values.</summary>
public sealed record ResourceConfig(
    int FoodOnGrassPerMille, int FoodOnForestPerMille, int WoodOnForestPerMille, bool WaterOnShore,
    ResourceRule Food, ResourceRule Water, ResourceRule Wood)
{
    public static ResourceConfig Default { get; } = new(40, 60, 120, true,
        new ResourceRule(20, 1, 300), new ResourceRule(1000, 50, 100), new ResourceRule(50, 1, 1200));

    public ResourceRule RuleFor(ResourceKind kind) => kind switch
    {
        ResourceKind.Food => Food,
        ResourceKind.Water => Water,
        ResourceKind.Wood => Wood,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal void Validate()
    {
        foreach (int chance in new[] { FoodOnGrassPerMille, FoodOnForestPerMille, WoodOnForestPerMille })
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)chance, 1000u);
        Food.Validate(); Water.Validate(); Wood.Validate();
    }
}

public readonly record struct ResourceView(int X, int Y, ResourceKind Kind, int Amount, int Capacity);

internal struct ResourceNode
{
    public int Cell;
    public ResourceKind Kind;
    public int Amount;
    public bool RegrowScheduled;
}

/// <summary>Resource nodes sorted by (cell, kind). Lookup by key only; never iterate the dictionary.</summary>
internal sealed class ResourceStore
{
    private readonly ResourceNode[] nodes;
    private readonly Dictionary<long, int> index;
    public ResourceConfig Config { get; }
    public int Count => nodes.Length;

    private ResourceStore(ResourceNode[] nodes, ResourceConfig config)
    {
        this.nodes = nodes;
        Config = config;
        index = new Dictionary<long, int>(nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
            index.Add(Key(nodes[i].Cell, nodes[i].Kind), i);
    }

    private static long Key(int cell, ResourceKind kind) => (long)cell * 8 + (byte)kind;

    public ref ResourceNode this[int i] => ref nodes[i];

    public int Find(int cell, ResourceKind kind) => index.TryGetValue(Key(cell, kind), out int i) ? i : -1;

    public static ResourceStore Generate(Terrain[] cells, int width, int height, ResourceConfig config, RandomStream rng)
    {
        var list = new List<ResourceNode>();
        for (int cell = 0; cell < cells.Length; cell++)
        {
            // Exactly one draw per tile keeps placement stable when chances change.
            int roll = (int)(rng.Draw() % 1000);
            switch (cells[cell])
            {
                case Terrain.Grass:
                    if (roll < config.FoodOnGrassPerMille) Add(list, cell, ResourceKind.Food, config);
                    break;
                case Terrain.Forest:
                    if (roll < config.FoodOnForestPerMille) Add(list, cell, ResourceKind.Food, config);
                    if (roll >= 1000 - config.WoodOnForestPerMille) Add(list, cell, ResourceKind.Wood, config);
                    break;
                case Terrain.Water:
                    if (config.WaterOnShore && IsShore(cells, width, height, cell)) Add(list, cell, ResourceKind.Water, config);
                    break;
            }
        }
        return new ResourceStore(list.ToArray(), config);
    }

    public static ResourceStore FromPlacements(MapOverride map, ResourceConfig config)
    {
        var list = new List<ResourceNode>();
        foreach (var p in map.Resources.OrderBy(p => p.Y * map.Width + p.X).ThenBy(p => (byte)p.Kind))
        {
            if ((uint)p.X >= (uint)map.Width || (uint)p.Y >= (uint)map.Height) throw new ArgumentException($"Resource {p} out of bounds.");
            if (p.Amount < 0 || p.Amount > config.RuleFor(p.Kind).Capacity) throw new ArgumentException($"Resource {p} amount outside 0..capacity.");
            var t = map.Cells[p.Y * map.Width + p.X];
            bool fits = p.Kind == ResourceKind.Water ? t == Terrain.Water : TerrainRules.IsPassable(t);
            if (!fits) throw new ArgumentException($"Resource {p} not allowed on {t}.");
            int cell = p.Y * map.Width + p.X;
            if (list.Count > 0 && list[^1].Cell == cell && list[^1].Kind == p.Kind) throw new ArgumentException($"Duplicate resource {p}.");
            list.Add(new ResourceNode { Cell = cell, Kind = p.Kind, Amount = p.Amount });
        }
        return new ResourceStore(list.ToArray(), config);
    }

    private static void Add(List<ResourceNode> list, int cell, ResourceKind kind, ResourceConfig config) =>
        list.Add(new ResourceNode { Cell = cell, Kind = kind, Amount = config.RuleFor(kind).Capacity });

    private static bool IsShore(Terrain[] cells, int width, int height, int cell)
    {
        int x = cell % width, y = cell / width;
        return (x > 0 && cells[cell - 1] != Terrain.Water) || (x < width - 1 && cells[cell + 1] != Terrain.Water)
            || (y > 0 && cells[cell - width] != Terrain.Water) || (y < height - 1 && cells[cell + width] != Terrain.Water);
    }

    public void WriteCanonical(BinaryWriter writer)
    {
        writer.Write((uint)nodes.Length);
        foreach (var node in nodes)
        {
            writer.Write(node.Cell);
            writer.Write((byte)node.Kind);
            writer.Write(node.Amount);
            writer.Write(node.RegrowScheduled);
        }
    }
}
