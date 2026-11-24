namespace FantasyWorld.Simulation;

/// <summary>
/// Bounded grid pathfinding:
/// - <see cref="FindNearest"/>: bounded BFS over passable tiles (4-neighbour, fixed N/W/E/S order) for local multi-goal queries.
/// - <see cref="FindPath"/>: bounded deterministic A* (Manhattan heuristic, fixed tie-breaking) for single-target long routes.
/// </summary>
internal sealed class Navigation
{
    private static readonly (int Dx, int Dy)[] Steps = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    private readonly record struct AStarKey(int F, int H, int Order) : IComparable<AStarKey>
    {
        public int CompareTo(AStarKey other)
        {
            int c = F.CompareTo(other.F);
            if (c != 0) return c;
            c = H.CompareTo(other.H);
            return c != 0 ? c : Order.CompareTo(other.Order);
        }
    }

    // Reused per world (T014): visited stamps instead of a per-search Dictionary, and fixed buffers.
    private readonly int[] stamp;
    private readonly int[] parent;
    private readonly int[] queue;
    private int[]? gScore;
    private PriorityQueue<int, AStarKey>? open;
    private int generation;

    public Navigation(int cells)
    {
        stamp = new int[cells];
        parent = new int[cells];
        queue = new int[cells];
    }

    private void NextGeneration(out int openStamp, out int closedStamp)
    {
        if (generation >= int.MaxValue - 3)
        {
            Array.Clear(stamp);
            generation = 1;
        }
        else
        {
            generation += 2;
        }
        openStamp = generation;
        closedStamp = generation + 1;
    }

    /// <returns>Path of cells (excluding the start, ending at the goal), empty if the start is a goal, or null if not found.</returns>
    public int[]? FindNearest(Terrain[] terrain, int width, int height, int startX, int startY,
        int radius, int nodeBudget, Func<int, bool> isGoal, out int expanded, Func<int, bool>? canVisit = null)
    {
        expanded = 0;
        int start = startY * width + startX;
        if (isGoal(start)) return [];
        NextGeneration(out int seen, out _);
        stamp[start] = seen;
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
                if (stamp[next] == seen || !TerrainRules.IsPassable(terrain[next])) continue;
                if (canVisit is not null && !canVisit(next)) continue;
                stamp[next] = seen;
                parent[next] = cell;
                if (isGoal(next)) return Unwind(next);
                queue[tail++] = next;
            }
        }
        return null;
    }

    /// <summary>
    /// Bounded deterministic A* toward a single target tile. Expands at most <paramref name="nodeBudget"/> nodes
    /// and treats paths exceeding the budget as unreachable (null).
    /// </summary>
    public int[]? FindPath(Terrain[] terrain, int width, int height, int startX, int startY,
        int goalX, int goalY, int nodeBudget, out int expanded)
    {
        expanded = 0;
        int start = startY * width + startX;
        int goal = goalY * width + goalX;
        if (start == goal) return [];
        if (!TerrainRules.IsPassable(terrain[goal]) || nodeBudget <= 0) return null;

        NextGeneration(out int openStamp, out int closedStamp);
        gScore ??= new int[stamp.Length];
        open ??= new PriorityQueue<int, AStarKey>();
        open.Clear();

        stamp[start] = openStamp;
        gScore[start] = 0;
        parent[start] = -1;
        int order = 0;
        int h0 = Math.Abs(goalX - startX) + Math.Abs(goalY - startY);
        open.Enqueue(start, new AStarKey(h0, h0, order++));

        while (open.Count > 0 && expanded < nodeBudget)
        {
            int cell = open.Dequeue();
            if (stamp[cell] == closedStamp) continue;
            stamp[cell] = closedStamp;
            expanded++;
            if (cell == goal) return Unwind(goal);

            int cx = cell % width, cy = cell / width;
            int nextG = gScore[cell] + 1;
            foreach (var (dx, dy) in Steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                int next = ny * width + nx;
                int st = stamp[next];
                if (st == closedStamp || !TerrainRules.IsPassable(terrain[next])) continue;
                if (st == openStamp && nextG >= gScore[next]) continue;
                stamp[next] = openStamp;
                gScore[next] = nextG;
                parent[next] = cell;
                int h = Math.Abs(goalX - nx) + Math.Abs(goalY - ny);
                open.Enqueue(next, new AStarKey(nextG + h, h, order++));
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
