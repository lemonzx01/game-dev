namespace FantasyWorld.Simulation;

/// <summary>Queued path search for an entity's Eat/Drink action. Token = the entity's StepToken when queued.</summary>
internal readonly record struct PathRequest(long EntityId, ActionKind Kind, long Token);

/// <summary>
/// ARCHITECTURE §6 path budget: searches are queued and served FIFO after the scheduler each tick. A request
/// starts only when the remaining per-tick node budget covers its worst case (SearchNodeBudget), so a tick never
/// exceeds PathNodeBudgetPerTick and the ordered remainder carries forward. Unreachable goals put the entity on a
/// cooldown for that need (no infinite retries); a path blocked mid-walk is re-planned at most MaxReplans times.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly Queue<PathRequest> pathQueue = new();
    private Navigation? navigation; // reusable search buffers, created on first use


    private void RequestPath(EntityState e, ActionKind kind)
    {
        e.Mind.StepToken++; // supersedes any pending step of the previous action
        e.Mind.AwaitingPath = true;
        e.Mind.Path = null;
        e.Mind.PathIndex = 0;
        pathQueue.Enqueue(new PathRequest(e.Id.Value, kind, e.Mind.StepToken));
    }

    private bool OnCooldown(in MindState m, ActionKind kind, long now) =>
        (kind switch { ActionKind.Eat => m.NoFoodUntil, ActionKind.Work or ActionKind.Read => m.NoWorkUntil, _ => m.NoWaterUntil }) > now;

    private void ProcessPaths(long now)
    {
        long remaining = MindCfg.PathNodeBudgetPerTick;
        LastPathExpansions = 0;
        LastPathsServed = 0;
        routeSearchesLeft = TradeCfg.RouteSearchesPerTick;
        while (pathQueue.Count > 0)
        {
            var request = pathQueue.Peek();
            int i = FindLive(request.EntityId);
            var e = i >= 0 ? entities[i] : null;
            // Stale: entity died, switched action (e.g. fled) or already re-requested.
            if (e is null || e.LifeState != LifeStates.Alive || !e.Mind.AwaitingPath || e.Mind.Kind != request.Kind || e.Mind.StepToken != request.Token)
            {
                pathQueue.Dequeue();
                continue;
            }
            // Trade trips search the whole map (T016) under their own per-tick count; the FIFO order is kept either way.
            bool longRoute = request.Kind == ActionKind.Work && IsLongRoute(e.WorkGoal);
            if (longRoute ? routeSearchesLeft == 0 : remaining < MindCfg.SearchNodeBudget) break;
            pathQueue.Dequeue();
            if (longRoute) routeSearchesLeft--;
            navigation ??= new Navigation(terrain.Length);
            var path = navigation.FindNearest(terrain, options.MapWidth, options.MapHeight, e.X, e.Y,
                longRoute ? Math.Max(options.MapWidth, options.MapHeight) : MindCfg.PerceptionRadius,
                longRoute ? terrain.Length : MindCfg.SearchNodeBudget,
                cell => request.Kind switch
                {
                    ActionKind.Eat => CanEatAt(e, cell),
                    ActionKind.Work => IsWorkGoal(e, cell),
                    ActionKind.Read => SettlementOf(e) is { } home && cell == Cell(home.X, home.Y),
                    _ => CanConsumeAt(cell, ResourceKind.Water),
                }, out int expanded);
            if (!longRoute)
            {
                remaining -= expanded;
                LastPathExpansions += expanded;
            }
            LastPathsServed++;
            e.Mind.AwaitingPath = false;
            if (path is null)
            {
                if (request.Kind == ActionKind.Eat) e.Mind.NoFoodUntil = now + MindCfg.UnreachableCooldownTicks;
                else if (request.Kind is ActionKind.Work or ActionKind.Read) e.Mind.NoWorkUntil = now + TownCfg.WorkCooldownTicks;
                else e.Mind.NoWaterUntil = now + MindCfg.UnreachableCooldownTicks;
                Emit(new WorldEvent(0, now, EventKinds.PathFailed, 0, e.Id.Value, 0, e.X, e.Y, (int)request.Kind));
                FinishAction(e, now);
                continue;
            }
            e.Mind.Path = path;
            e.Mind.PathIndex = 0;
            ScheduleStep(e, now + (path.Length == 0 ? 1 : MoveTicks(e)));
        }
    }

    /// <summary>Called when the next path cell became impassable (e.g. God SetTerrain).</summary>
    private void OnPathBlocked(EntityState e, long now)
    {
        if (e.Mind.Kind is ActionKind.Eat or ActionKind.Drink or ActionKind.Work or ActionKind.Read && e.Mind.Replans < MindCfg.MaxReplans)
        {
            e.Mind.Replans++;
            RequestPath(e, e.Mind.Kind);
            return;
        }
        if (e.Mind.Kind == ActionKind.Wander) { StartIdle(e, now); return; }
        FinishAction(e, now);
    }

    private void WritePathQueue(BinaryWriter w)
    {
        w.Write((uint)pathQueue.Count);
        foreach (var r in pathQueue)
        {
            w.Write(r.EntityId);
            w.Write((int)r.Kind);
            w.Write(r.Token);
        }
    }

    // ---- God terrain tool -----------------------------------------------------------------------

    private CommandReason ApplyTerrain(SetTerrainCommand c)
    {
        int cell = Cell(c.X, c.Y);
        foreach (var kind in new[] { ResourceKind.Food, ResourceKind.Water, ResourceKind.Wood })
            if (resources.Find(cell, kind) >= 0) return CommandReason.HasResource;
        if (!TerrainRules.IsPassable(c.Terrain) && NearbyIds(c.X, c.Y, 0).Count > 0) return CommandReason.Occupied;
        terrain[cell] = c.Terrain;
        map = new MapView(options.MapWidth, options.MapHeight, System.Collections.Immutable.ImmutableArray.Create(terrain));
        return CommandReason.None;
    }

    // ---- Queries and test hooks -----------------------------------------------------------------

    /// <summary>Living entity ids within a Chebyshev radius, ascending by id (grid query + exact filter).</summary>
    internal List<long> NearbyIds(int x, int y, int radius)
    {
        var result = new List<long>();
        grid.Query(x, y, radius, id =>
        {
            var e = entities[FindLive(id)];
            if (e.LifeState == LifeStates.Alive && Math.Max(Math.Abs(e.X - x), Math.Abs(e.Y - y)) <= radius) result.Add(id);
        });
        result.Sort();
        return result;
    }

    internal IEnumerable<(long Id, int X, int Y)> LivePositionsForTest() =>
        entities.Where(e => e.LifeState == LifeStates.Alive).Select(e => (e.Id.Value, e.X, e.Y));

    internal long LastPathExpansions { get; private set; }
    internal int LastPathsServed { get; private set; }
    internal int PendingPathRequests => pathQueue.Count;

    /// <summary>Path-queue backlog for the T014 benchmark (read-only diagnostics).</summary>
    public int PendingPathRequestsForBenchmark { get { CheckAccess(); return pathQueue.Count; } }

    /// <summary>Pending discretionary decisions (read-only diagnostics for benchmarks/profiling).</summary>
    public int PendingDecisionsForBenchmark { get { CheckAccess(); return scheduler.DiscretionaryCount; } }

    /// <summary>Decisions already overdue at the next tick, including stale entries (diagnostics, O(n)).</summary>
    public int OverdueDecisionsForBenchmark { get { CheckAccess(); return scheduler.OverdueDiscretionary(tick + 1); } }
}
