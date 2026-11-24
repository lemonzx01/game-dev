namespace FantasyWorld.Simulation;

internal interface IGridVisitor
{
    bool Visit(long id);
}

/// <summary>
/// Bucketed spatial index of living entities (derived data, rebuilt from entity state; not hashed).
/// Buckets are visited in ascending index and ids in ascending order, so queries are deterministic.
/// </summary>
internal sealed class SpatialGrid
{
    private const int BucketSize = 8;
    private readonly int bucketsWide, bucketsHigh;
    private readonly List<long>?[] buckets;

    public int Version { get; private set; }

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
        Version++;
    }

    public void Remove(long id, int x, int y)
    {
        var list = buckets[Bucket(x, y)];
        int i = list?.BinarySearch(id) ?? -1;
        if (i < 0) throw new InvalidOperationException($"Entity {id} not indexed at {x},{y}.");
        list!.RemoveAt(i);
        Version++;
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

    /// <summary>Visits ids in overlapping buckets; stops early if <paramref name="visit"/> returns false.</summary>
    public void Query(int x, int y, int radius, Func<long, bool> visit)
    {
        int bx0 = Math.Max(0, (x - radius) / BucketSize), bx1 = Math.Min(bucketsWide - 1, (x + radius) / BucketSize);
        int by0 = Math.Max(0, (y - radius) / BucketSize), by1 = Math.Min(bucketsHigh - 1, (y + radius) / BucketSize);
        for (int by = by0; by <= by1; by++)
            for (int bx = bx0; bx <= bx1; bx++)
                if (buckets[by * bucketsWide + bx] is { } list)
                    foreach (long id in list)
                        if (!visit(id)) return;
    }

    /// <summary>
    /// Visits up to <paramref name="maxVisits"/> candidates across overlapping buckets starting after
    /// (<paramref name="startBucketOffset"/>, <paramref name="minIdExclusive"/>). Returns true if the
    /// scan finished (all overlapping buckets scanned or <paramref name="visit"/> returned false); false
    /// if paused at the budget limit with exact deterministic continuation cursor written to ref parameters.
    /// </summary>
    public bool QueryRange(int x, int y, int radius, int startBucketOffset, long minIdExclusive, int maxVisits,
        ref int nextBucketOffset, ref long nextMinIdExclusive, Func<long, bool> visit, out int visited)
    {
        var adapter = new FuncVisitor(visit);
        return QueryRange(x, y, radius, startBucketOffset, minIdExclusive, maxVisits, ref nextBucketOffset, ref nextMinIdExclusive, ref adapter, out visited);
    }

    private readonly struct FuncVisitor(Func<long, bool> fn) : IGridVisitor
    {
        public bool Visit(long id) => fn(id);
    }

    public bool QueryRange<TVisitor>(int x, int y, int radius, int startBucketOffset, long minIdExclusive, int maxVisits,
        ref int nextBucketOffset, ref long nextMinIdExclusive, ref TVisitor visitor, out int visited)
        where TVisitor : struct, IGridVisitor
    {
        visited = 0;
        int bx0 = Math.Max(0, (x - radius) / BucketSize), bx1 = Math.Min(bucketsWide - 1, (x + radius) / BucketSize);
        int by0 = Math.Max(0, (y - radius) / BucketSize), by1 = Math.Min(bucketsHigh - 1, (y + radius) / BucketSize);
        int cols = bx1 - bx0 + 1;
        int totalBuckets = (by1 - by0 + 1) * cols;
        int offset = Math.Max(0, startBucketOffset);
        long cursor = minIdExclusive;
        while (offset < totalBuckets)
        {
            int by = by0 + offset / cols;
            int bx = bx0 + offset % cols;
            if (buckets[by * bucketsWide + bx] is { } list && list.Count > 0)
            {
                int startIdx = 0;
                if (cursor > 0)
                {
                    int found = list.BinarySearch(cursor + 1);
                    startIdx = found >= 0 ? found : ~found;
                }
                for (int i = startIdx; i < list.Count; i++)
                {
                    if (visited >= maxVisits)
                    {
                        nextBucketOffset = offset;
                        nextMinIdExclusive = cursor;
                        return false;
                    }
                    long id = list[i];
                    visited++;
                    cursor = id;
                    if (!visitor.Visit(id))
                    {
                        nextBucketOffset = totalBuckets;
                        nextMinIdExclusive = long.MaxValue;
                        return true;
                    }
                }
            }
            offset++;
            cursor = 0;
        }
        nextBucketOffset = totalBuckets;
        nextMinIdExclusive = long.MaxValue;
        return true;
    }
}
