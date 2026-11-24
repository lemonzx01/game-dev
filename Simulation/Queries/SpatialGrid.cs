namespace FantasyWorld.Simulation;

/// <summary>
/// Bucketed spatial index of living entities (derived data, rebuilt from entity state; not hashed).
/// Buckets are visited in ascending index and ids in ascending order, so queries are deterministic.
/// </summary>
internal sealed class SpatialGrid
{
    private const int BucketSize = 8;
    private readonly int bucketsWide, bucketsHigh;
    private readonly List<long>?[] buckets;

    public SpatialGrid(int width, int height)
    {
        bucketsWide = (width + BucketSize - 1) / BucketSize;
        bucketsHigh = (height + BucketSize - 1) / BucketSize;
        buckets = new List<long>?[bucketsWide * bucketsHigh];
    }

    private int Bucket(int x, int y) => y / BucketSize * bucketsWide + x / BucketSize;

    public void Add(long id, int x, int y)
    {
        var list = buckets[Bucket(x, y)] ??= new List<long>();
        int i = list.BinarySearch(id);
        if (i >= 0) throw new InvalidOperationException($"Entity {id} already indexed.");
        list.Insert(~i, id);
    }

    public void Remove(long id, int x, int y)
    {
        var list = buckets[Bucket(x, y)];
        int i = list?.BinarySearch(id) ?? -1;
        if (i < 0) throw new InvalidOperationException($"Entity {id} not indexed at {x},{y}.");
        list!.RemoveAt(i);
    }

    public void Move(long id, int fromX, int fromY, int toX, int toY)
    {
        if (Bucket(fromX, fromY) == Bucket(toX, toY)) return;
        Remove(id, fromX, fromY);
        Add(id, toX, toY);
    }

    /// <summary>Visits ids in buckets overlapping the square of the given Chebyshev radius (callers filter exact distance).</summary>
    public void Query(int x, int y, int radius, Action<long> visit)
    {
        int bx0 = Math.Max(0, (x - radius) / BucketSize), bx1 = Math.Min(bucketsWide - 1, (x + radius) / BucketSize);
        int by0 = Math.Max(0, (y - radius) / BucketSize), by1 = Math.Min(bucketsHigh - 1, (y + radius) / BucketSize);
        for (int by = by0; by <= by1; by++)
            for (int bx = bx0; bx <= bx1; bx++)
                if (buckets[by * bucketsWide + bx] is { } list)
                    foreach (long id in list) visit(id);
    }
}
