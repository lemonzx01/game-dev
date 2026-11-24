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

    private void RequestPath(EntityState e, ActionKind kind)
    {
        e.Mind.StepToken++; // supersedes any pending step of the previous action
        e.Mind.AwaitingPath = true;
        e.Mind.Path = null;
        e.Mind.PathIndex = 0;
        pathQueue.Enqueue(new PathRequest(e.Id.Value, kind, e.Mind.StepToken));
    }

    private bool OnCooldown(in MindState m, ActionKind kind, long now) =>
        (kind == ActionKind.Eat ? m.NoFoodUntil : m.NoWaterUntil) > now;

    private void ProcessPaths(long now)
    {
        long remaining = MindCfg.PathNodeBudgetPerTick;
        LastPathExpansions = 0;
        LastPathsServed = 0;
        while (pathQueue.Count > 0 && remaining >= MindCfg.SearchNodeBudget)
        {
            var request = pathQueue.Dequeue();
            int i = FindLive(request.EntityId);
            if (i < 0) continue;
            var e = entities[i];
            // Stale: entity died, switched action (e.g. fled) or already re-requested.
            if (e.LifeState != LifeStates.Alive || !e.Mind.AwaitingPath || e.Mind.Kind != request.Kind || e.Mind.StepToken != request.Token)
                continue;
            var path = Navigation.FindNearest(terrain, options.MapWidth, options.MapHeight, e.X, e.Y,
                MindCfg.PerceptionRadius, MindCfg.SearchNodeBudget,
                cell => request.Kind == ActionKind.Eat ? CanEatAt(e, cell) : CanConsumeAt(cell, ResourceKind.Water), out int expanded);
            remaining -= expanded;
            LastPathExpansions += expanded;
            LastPathsServed++;
            e.Mind.AwaitingPath = false;
            if (path is null)
            {
                if (request.Kind == ActionKind.Eat) e.Mind.NoFoodUntil = now + MindCfg.UnreachableCooldownTicks;
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
        if (e.Mind.Kind is ActionKind.Eat or ActionKind.Drink && e.Mind.Replans < MindCfg.MaxReplans)
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
}
