namespace FantasyWorld.Simulation;

/// <summary>
/// Bounded breadth-first search over passable tiles (4-neighbour, fixed N/W/E/S order, so ties resolve
/// deterministically). Expands at most <c>nodeBudget</c> cells within a Chebyshev radius; an unreachable or
/// out-of-budget goal is simply "not found". T006 replaces this with a budgeted A* request queue.
/// </summary>
internal static class Navigation
{
    private static readonly (int Dx, int Dy)[] Steps = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    /// <returns>Path of cells (excluding the start, ending at the goal), empty if the start is a goal, or null if not found.</returns>
    public static int[]? FindNearest(Terrain[] terrain, int width, int height, int startX, int startY,
        int radius, int nodeBudget, Func<int, bool> isGoal)
    {
        int start = startY * width + startX;
        if (isGoal(start)) return [];
        var parent = new Dictionary<int, int> { [start] = -1 }; // lookup only, never iterated
        var queue = new Queue<int>();
        queue.Enqueue(start);
        int expanded = 0;
        while (queue.Count > 0 && expanded < nodeBudget)
        {
            int cell = queue.Dequeue();
            expanded++;
            int cx = cell % width, cy = cell / width;
            foreach (var (dx, dy) in Steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                if (Math.Max(Math.Abs(nx - startX), Math.Abs(ny - startY)) > radius) continue;
                int next = ny * width + nx;
                if (parent.ContainsKey(next) || !TerrainRules.IsPassable(terrain[next])) continue;
                parent[next] = cell;
                if (isGoal(next)) return Unwind(parent, next);
                queue.Enqueue(next);
            }
        }
        return null;
    }

    private static int[] Unwind(Dictionary<int, int> parent, int goal)
    {
        var path = new List<int>();
        for (int c = goal; parent[c] != -1; c = parent[c]) path.Add(c);
        path.Reverse();
        return path.ToArray();
    }
}
