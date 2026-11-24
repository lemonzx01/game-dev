namespace FantasyWorld.Simulation;

/// <summary>
/// Bounded breadth-first search over passable tiles (4-neighbour, fixed N/W/E/S order, so ties resolve
/// deterministically). Expands at most <c>nodeBudget</c> cells within a Chebyshev radius; an unreachable or
/// out-of-budget goal is simply "not found". T006 replaces this with a budgeted A* request queue.
/// </summary>
internal sealed class Navigation
{
    private static readonly (int Dx, int Dy)[] Steps = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    // Reused per world (T014): visited stamps instead of a per-search Dictionary, and a fixed ring queue.
    private readonly int[] stamp;
    private readonly int[] parent;
    private readonly int[] queue;
    private int generation;

    public Navigation(int cells)
    {
        stamp = new int[cells];
        parent = new int[cells];
        queue = new int[cells];
    }

    /// <returns>Path of cells (excluding the start, ending at the goal), empty if the start is a goal, or null if not found.</returns>
    public int[]? FindNearest(Terrain[] terrain, int width, int height, int startX, int startY,
        int radius, int nodeBudget, Func<int, bool> isGoal, out int expanded)
    {
        expanded = 0;
        int start = startY * width + startX;
        if (isGoal(start)) return [];
        if (++generation == int.MaxValue) { Array.Clear(stamp); generation = 1; }
        stamp[start] = generation;
        parent[start] = -1;
        int head = 0, tail = 0;
        queue[tail++] = start;
        while (head < tail && expanded < nodeBudget)
        {
            int cell = queue[head++];
            expanded++;
            int cx = cell % width, cy = cell / width;
            foreach (var (dx, dy) in Steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                if (Math.Max(Math.Abs(nx - startX), Math.Abs(ny - startY)) > radius) continue;
                int next = ny * width + nx;
                if (stamp[next] == generation || !TerrainRules.IsPassable(terrain[next])) continue;
                stamp[next] = generation;
                parent[next] = cell;
                if (isGoal(next)) return Unwind(next);
                queue[tail++] = next;
            }
        }
        return null;
    }

    private int[] Unwind(int goal)
    {
        int length = 0;
        for (int c = goal; parent[c] != -1; c = parent[c]) length++;
        var path = new int[length];
        for (int c = goal, i = length - 1; parent[c] != -1; c = parent[c], i--) path[i] = c;
        return path;
    }
}
