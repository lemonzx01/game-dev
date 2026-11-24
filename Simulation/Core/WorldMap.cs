using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

public enum Terrain : byte { Water = 0, Grass = 1, Forest = 2, Mountain = 3, Snow = 4 }

/// <summary>Immutable read view of the tile grid (row-major, index = y * Width + x).</summary>
public sealed record MapView(int Width, int Height, ImmutableArray<Terrain> Cells)
{
    public Terrain At(int x, int y) => Cells[y * Width + x];
}

/// <summary>Explicit resource node for a hand-built map.</summary>
public readonly record struct ResourcePlacement(int X, int Y, ResourceKind Kind, int Amount);

/// <summary>Hand-built map used instead of seeded generation (test scenarios, benchmarks). Dimensions must match the options.</summary>
public sealed record MapOverride(int Width, int Height, ImmutableArray<Terrain> Cells, ImmutableArray<ResourcePlacement> Resources)
{
    /// <summary>Builds a map from text rows: '~' water, '.' grass, 'T' forest, '^' mountain, '*' snow.</summary>
    public static MapOverride FromRows(IReadOnlyList<string> rows, params ResourcePlacement[] resources)
    {
        int h = rows.Count, w = rows[0].Length;
        var cells = ImmutableArray.CreateBuilder<Terrain>(w * h);
        foreach (string row in rows)
        {
            if (row.Length != w) throw new ArgumentException("Ragged map rows.");
            foreach (char ch in row)
                cells.Add(ch switch
                {
                    '~' => Terrain.Water, '.' => Terrain.Grass, 'T' => Terrain.Forest, '^' => Terrain.Mountain, '*' => Terrain.Snow,
                    _ => throw new ArgumentException($"Unknown map char '{ch}'."),
                });
        }
        return new MapOverride(w, h, cells.MoveToImmutable(), [.. resources]);
    }
}

internal static class TerrainRules
{
    public static bool IsPassable(Terrain t) => t is Terrain.Grass or Terrain.Forest or Terrain.Snow;
}

/// <summary>
/// Deterministic terrain from a world-generation RNG stream using integer-only value noise.
/// Thresholds are rank-based so every map of the same size has the same terrain proportions.
/// </summary>
internal static class MapGenerator
{
    private const int Coarse = 16;
    private const int Fine = 4;

    public static Terrain[] Generate(int width, int height, RandomStream rng)
    {
        int n = width * height;
        int[] elevation = Field(width, height, rng, Coarse, Fine);
        int[] moisture = Field(width, height, rng, Coarse, Fine);

        int water = Percentile(elevation, 25);
        int mountain = Percentile(elevation, 91);
        int snow = Percentile(elevation, 97);
        int forest = Percentile(moisture, 62);

        var cells = new Terrain[n];
        for (int i = 0; i < n; i++)
        {
            int e = elevation[i];
            cells[i] = e < water ? Terrain.Water
                : e >= snow ? Terrain.Snow
                : e >= mountain ? Terrain.Mountain
                : moisture[i] >= forest ? Terrain.Forest
                : Terrain.Grass;
        }
        return cells;
    }

    // Two octaves of bilinear value noise, 0..1023, weighted 3:1.
    private static int[] Field(int width, int height, RandomStream rng, int coarse, int fine)
    {
        int[] a = Octave(width, height, rng, coarse);
        int[] b = Octave(width, height, rng, fine);
        for (int i = 0; i < a.Length; i++)
            a[i] = (a[i] * 3 + b[i]) / 4;
        return a;
    }

    private static int[] Octave(int width, int height, RandomStream rng, int step)
    {
        int lw = width / step + 2, lh = height / step + 2;
        var lattice = new int[lw * lh];
        for (int i = 0; i < lattice.Length; i++)
            lattice[i] = (int)(rng.Draw() >> 54); // top 10 bits: 0..1023
        var field = new int[width * height];
        int area = step * step;
        for (int y = 0; y < height; y++)
        {
            int gy = y / step, fy = y % step;
            for (int x = 0; x < width; x++)
            {
                int gx = x / step, fx = x % step;
                int v00 = lattice[gy * lw + gx], v10 = lattice[gy * lw + gx + 1];
                int v01 = lattice[(gy + 1) * lw + gx], v11 = lattice[(gy + 1) * lw + gx + 1];
                field[y * width + x] = (v00 * (step - fx) * (step - fy) + v10 * fx * (step - fy)
                    + v01 * (step - fx) * fy + v11 * fx * fy) / area;
            }
        }
        return field;
    }

    private static int Percentile(int[] values, int percent)
    {
        var sorted = (int[])values.Clone();
        Array.Sort(sorted); // ints: total order, no tie ambiguity
        return sorted[Math.Min(sorted.Length - 1, (int)((long)sorted.Length * percent / 100))];
    }
}
