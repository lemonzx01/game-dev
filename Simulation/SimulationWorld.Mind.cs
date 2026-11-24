namespace FantasyWorld.Simulation;

/// <summary>Per-entity mind state (part of EntityState; hashed).</summary>
internal struct MindState
{
    public ActionKind Kind;
    public int[]? Path;          // remaining cells for movement actions
    public int PathIndex;
    public long TargetEntity;    // Flee: threat id
    public int FleeStepsDone;
    public long CommitUntil;     // no voluntary switch before this tick (danger still interrupts)
    public long ThinkToken;      // only the scheduled Think with this token runs
    public long StepToken;       // only the scheduled ActionStep with this token runs
    public long FatigueTick;     // fatigue anchor
    public long Fatigue;
    public bool Resting;
    public bool AwaitingPath;     // Eat/Drink waiting in the path queue
    public long NoFoodUntil;      // cooldown after an unreachable food search
    public long NoWaterUntil;
    public int Replans;           // re-plans after a blocked path in the current action
    public long HuntUntil;        // give up the current hunt at this tick
    public long NoHuntUntil;      // cooldown after a failed hunt
    public long AttackReadyTick;  // next tick an attack may land
    public long NoWorkUntil;      // cooldown after an unreachable work target
}

public sealed partial class SimulationWorld
{
    public const uint AiStream = 2;
    private const int ThinkPriority = 20, StepPriority = 5;
    private static readonly (int Dx, int Dy)[] Neighbours = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    private MindConfig MindCfg => options.Mind;

    // ---- Fatigue (elapsed-time anchor, like vitals) ---------------------------------------------

    private long FatigueAt(in MindState m, long now)
    {
        long elapsed = now - m.FatigueTick;
        return m.Resting
            ? Math.Max(0, m.Fatigue - MindCfg.RestRecoveryPerTick * elapsed)
            : Math.Min(MindConfig.MaxFatigue, m.Fatigue + MindCfg.FatiguePerTick * elapsed);
    }

    private void SetResting(EntityState e, bool resting, long now)
    {
        if (e.Mind.Resting == resting) return;
        e.Mind.Fatigue = FatigueAt(e.Mind, now);
        e.Mind.FatigueTick = now;
        e.Mind.Resting = resting;
    }

    // ---- Scheduling helpers --------------------------------------------------------------------

    private void InitMind(EntityState e, long now)
    {
        e.Mind.FatigueTick = now;
        if (!MindCfg.Enabled) return;
        // Stagger first decisions by stable ID so spawns in one tick do not all think together.
        ScheduleThink(e, now + 1 + e.Id.Value % MindCfg.ThinkIntervalTicks);
    }

    private void ScheduleThink(EntityState e, long due)
    {
        e.Mind.ThinkToken++;
        scheduler.Schedule(due, ThinkPriority, e.Id.Value, new WorkItem(WorkKind.Think, e.Id.Value, e.Mind.ThinkToken), isRequired: false);
    }

    private void ScheduleStep(EntityState e, long due)
    {
        e.Mind.StepToken++;
        scheduler.Schedule(due, StepPriority, e.Id.Value, new WorkItem(WorkKind.ActionStep, e.Id.Value, e.Mind.StepToken), isRequired: true);
    }

    private EntityState? LiveFor(WorkItem item, bool think)
    {
        int i = FindLive(item.Subject);
        if (i < 0) return null;
        var e = entities[i];
        if (e.LifeState != LifeStates.Alive) return null;
        return item.Token == (think ? e.Mind.ThinkToken : e.Mind.StepToken) ? e : null;
    }

    // ---- Perception ----------------------------------------------------------------------------

    /// <summary>Nearest living feared entity within the perception radius (Manhattan distance, then lower ID).</summary>
    private (EntityState? Threat, int Distance) NearestThreat(EntityState e)
    {
        if (!HasFears(e)) return (null, 0);
        EntityState? best = null;
        int bestDistance = int.MaxValue;
        int r = MindCfg.PerceptionRadius;
        grid.Query(e.X, e.Y, r, id =>
        {
            if (id == e.Id.Value) return;
            var other = entities[FindLive(id)];
            if (other.LifeState != LifeStates.Alive || !Fears(e, other.Species)) return;
            if (Math.Max(Math.Abs(other.X - e.X), Math.Abs(other.Y - e.Y)) > r) return;
            int d = Math.Abs(other.X - e.X) + Math.Abs(other.Y - e.Y);
            if (d < bestDistance || (d == bestDistance && other.Id.Value < best!.Id.Value)) { best = other; bestDistance = d; }
        });
        return (best, best is null ? 0 : bestDistance);
    }

    // Goal test for an Eat path: plants for herbivores, carcass meat for carnivores, either for omnivores.
    // Settlement members may also eat at their town centre while its storage holds food (T015).
    private bool CanEatAt(EntityState e, int cell)
    {
        if (SettlementOf(e) is { Food: > 0 } town && cell == Cell(town.X, town.Y)) return true;
        return DietOf(e) switch
        {
            Diet.Herbivore => CanConsumeAt(cell, ResourceKind.Food),
            Diet.Carnivore => CanConsumeAt(cell, ResourceKind.Meat),
            _ => CanConsumeAt(cell, ResourceKind.Food) || CanConsumeAt(cell, ResourceKind.Meat),
        };
    }

    // Food: a stocked node on the tile. Water: a stocked water node on a 4-neighbour (water tiles are impassable).
    // Meat: a carcass on the tile or a 4-neighbour.
    private bool CanConsumeAt(int cell, ResourceKind kind)
    {
        if (kind == ResourceKind.Meat)
        {
            int mx = cell % options.MapWidth, my = cell / options.MapWidth;
            if (MeatAt(cell) > 0) return true;
            foreach (var (dx, dy) in Neighbours)
                if (InBounds(mx + dx, my + dy) && MeatAt(Cell(mx + dx, my + dy)) > 0) return true;
            return false;
        }
        if (kind == ResourceKind.Food)
        {
            int n = resources.Find(cell, ResourceKind.Food);
            return n >= 0 && resources[n].Amount > 0;
        }
        int x = cell % options.MapWidth, y = cell / options.MapWidth;
        foreach (var (dx, dy) in Neighbours)
        {
            if (!InBounds(x + dx, y + dy)) continue;
            int n = resources.Find(Cell(x + dx, y + dy), ResourceKind.Water);
            if (n >= 0 && resources[n].Amount > 0) return true;
        }
        return false;
    }

    // ---- Decision ------------------------------------------------------------------------------

    /// <summary>
    /// Utility decision: perceive → score → pick the best feasible candidate (score desc, then ActionKind asc).
    /// Commitment keeps a non-idle action until CommitUntil unless danger appears; the current action also
    /// gets an inertia bonus. Both prevent rapid oscillation between close scores.
    /// </summary>
    private void Think(EntityState e, long now)
    {
        var cfg = MindCfg;
        var (threat, distance) = NearestThreat(e);
        if (threat is not null) OnThreatSeen(e, threat, distance, now); // danger raises fear and is remembered
        ScheduleThink(e, now + (threat is null ? cfg.ThinkIntervalTicks : cfg.DangerThinkIntervalTicks));

        var current = e.Mind.Kind;
        if (threat is null && current is not (ActionKind.Idle or ActionKind.Wander) && now < e.Mind.CommitUntil)
            return; // committed
        if (threat is not null && current == ActionKind.Flee && e.Mind.TargetEntity == threat.Id.Value)
            return; // already fleeing this threat

        var v = e.Vitals.At(now, options.Vitals);
        long fatigue = FatigueAt(e.Mind, now);
        Span<(int Score, ActionKind Kind)> candidates = stackalloc (int, ActionKind)[8];
        int n = 0;
        if (threat is not null)
            candidates[n++] = (FleeScore(e, distance, now), ActionKind.Flee); // personality and fear decide how close is too close
        if (v.Thirst >= cfg.DrinkStartNeed || current == ActionKind.Drink)
            candidates[n++] = ((int)(v.Thirst * 12 / 10_000), ActionKind.Drink);
        if (v.Hunger >= cfg.EatStartNeed || current == ActionKind.Eat)
            candidates[n++] = ((int)(v.Hunger / 1_000), ActionKind.Eat);
        // Predators hunt only when hungry; a reachable carcass (Eat) is preferred by one point.
        if ((SpeciesOf(e)?.Hunts.Length ?? 0) > 0 && (v.Hunger >= cfg.EatStartNeed || current == ActionKind.Hunt))
            candidates[n++] = ((int)(v.Hunger / 1_000) - 1, ActionKind.Hunt);
        // Reproduction only when safe and needs are low (MateFor checks vitals, stage, caps and a mate in sight).
        if (threat is null && (current == ActionKind.Mate || MateFor(e, now) is not null))
            candidates[n++] = (250 + e.Psyche.Sociability / 4, ActionKind.Mate);
        if (threat is null && (fatigue >= cfg.RestStartFatigue || current == ActionKind.Rest))
            candidates[n++] = ((int)(fatigue * 9 / 10_000), ActionKind.Rest);
        // Settlement work when nothing urgent: what the town needs (deliver, harvest, gather food or wood).
        if (threat is null && e.SettlementId != 0 && (current == ActionKind.Work || PlanWork(e) != WorkGoal.None))
            candidates[n++] = (TownCfg.WorkScore, ActionKind.Work);
        candidates[n++] = (50, ActionKind.Wander);
        for (int i = 0; i < n; i++)
            if (candidates[i].Kind == current) candidates[i].Score += cfg.InertiaBonus;
        // Insertion sort by (score desc, kind asc): at most 7 candidates, no allocation (T014).
        for (int i = 1; i < n; i++)
            for (int j = i; j > 0 && (candidates[j].Score > candidates[j - 1].Score
                     || (candidates[j].Score == candidates[j - 1].Score && candidates[j].Kind < candidates[j - 1].Kind)); j--)
                (candidates[j], candidates[j - 1]) = (candidates[j - 1], candidates[j]);
        var ordered = candidates[..n];

        foreach (var (_, kind) in ordered)
        {
            if (kind == current && current != ActionKind.Wander) return; // keep the running action, no restart
            if (TryStart(e, kind, threat, now)) return;
        }
        StartIdle(e, now);
    }

    private bool TryStart(EntityState e, ActionKind kind, EntityState? threat, long now)
    {
        int[]? path = null;
        int wanderCell = -1;
        long huntTarget = 0;
        switch (kind)
        {
            case ActionKind.Eat:
            case ActionKind.Drink:
                if (OnCooldown(e.Mind, kind, now)) return false; // recently unreachable: infeasible for now
                break;
            case ActionKind.Flee:
                if (threat is null || FleeStep(e, threat) < 0) return false;
                break;
            case ActionKind.Hunt:
                if (e.Mind.NoHuntUntil > now || StageOf(e, now) == LifeStage.Juvenile || NearestPrey(e) is not { } prey) return false;
                huntTarget = prey.Id.Value;
                break;
            case ActionKind.Mate:
                if (MateFor(e, now) is not { } mate) return false;
                huntTarget = mate.Id.Value;
                break;
            case ActionKind.Work:
                if (OnCooldown(e.Mind, kind, now) || PlanWork(e) is not (var goal and not WorkGoal.None)) return false;
                e.WorkGoal = goal;
                e.Job = JobFor(goal, e.Job);
                break;
            case ActionKind.Wander:
                wanderCell = WanderStep(e);
                if (wanderCell < 0) return false;
                break;
        }

        SetResting(e, kind == ActionKind.Rest, now);
        e.Mind.Kind = kind;
        e.Action = (int)kind;
        e.Mind.Path = kind == ActionKind.Wander ? [wanderCell] : path;
        e.Mind.PathIndex = 0;
        e.Mind.TargetEntity = kind is ActionKind.Hunt or ActionKind.Mate ? huntTarget : threat?.Id.Value ?? 0;
        e.Mind.HuntUntil = now + (kind == ActionKind.Mate ? options.Ecology.MateGiveUpTicks : options.Ecology.HuntGiveUpTicks);
        e.Mind.AttackReadyTick = now;
        e.Mind.FleeStepsDone = 0;
        e.Mind.Replans = 0;
        e.Mind.AwaitingPath = false;
        e.Mind.CommitUntil = now + MindCfg.CommitmentTicks;
        Emit(new WorldEvent(0, now, EventKinds.ActionStarted, 0, e.Id.Value, e.Mind.TargetEntity, e.X, e.Y, (int)kind));

        if (kind == ActionKind.Rest)
        {
            long fatigue = FatigueAt(e.Mind, now);
            long ticks = Math.Max(1, (fatigue - MindCfg.RestEndFatigue + MindCfg.RestRecoveryPerTick - 1) / MindCfg.RestRecoveryPerTick);
            ScheduleStep(e, now + ticks);
        }
        else if (kind is ActionKind.Eat or ActionKind.Drink or ActionKind.Work)
        {
            RequestPath(e, kind); // searched later within the per-tick path budget
        }
        else
        {
            ScheduleStep(e, now + MoveTicks(e));
        }
        return true;
    }

    private void StartIdle(EntityState e, long now)
    {
        SetResting(e, false, now);
        e.Mind.Kind = ActionKind.Idle;
        e.Action = (int)ActionKind.Idle;
        e.Mind.Path = null;
        e.Mind.AwaitingPath = false;
        e.Mind.StepToken++; // cancel any pending step or path request
    }

    private void FinishAction(EntityState e, long now)
    {
        StartIdle(e, now);
        e.Mind.CommitUntil = now;
        ScheduleThink(e, now + 1); // decide again promptly
    }

    // ---- Execution -----------------------------------------------------------------------------

    private void Step(EntityState e, long now)
    {
        switch (e.Mind.Kind)
        {
            case ActionKind.Rest:
                FinishAction(e, now); // recovery reached the target
                return;
            case ActionKind.Hunt:
                HuntStep(e, now);
                return;
            case ActionKind.Mate:
                MateStep(e, now);
                return;
            case ActionKind.Flee:
                var threat = FindLive(e.Mind.TargetEntity) is int ti and >= 0 ? entities[ti] : null;
                int dest = threat is { LifeState: LifeStates.Alive } ? FleeStep(e, threat) : -1;
                if (dest >= 0) MoveTo(e, dest);
                e.Mind.FleeStepsDone++;
                bool safe = threat is null || threat.LifeState != LifeStates.Alive
                    || Math.Max(Math.Abs(threat.X - e.X), Math.Abs(threat.Y - e.Y)) > MindCfg.PerceptionRadius;
                if (dest < 0 || safe || e.Mind.FleeStepsDone >= MindCfg.FleeSteps) FinishAction(e, now);
                else ScheduleStep(e, now + MoveTicks(e));
                return;
            case ActionKind.Eat:
            case ActionKind.Drink:
            case ActionKind.Work:
            case ActionKind.Wander:
                var path = e.Mind.Path!;
                if (e.Mind.PathIndex < path.Length)
                {
                    int next = path[e.Mind.PathIndex++];
                    if (!TerrainRules.IsPassable(terrain[next])) { OnPathBlocked(e, now); return; }
                    MoveTo(e, next);
                    if (e.Mind.PathIndex < path.Length) { ScheduleStep(e, now + MoveTicks(e)); return; }
                }
                if (e.Mind.Kind == ActionKind.Wander)
                {
                    StartIdle(e, now); // the regular think cadence continues; no early re-think
                    return;
                }
                if (e.Mind.Kind == ActionKind.Work) { DoWork(e, now); FinishAction(e, now); return; }
                if (e.Mind.Kind == ActionKind.Eat && EatFromStorage(e, now)) { FinishAction(e, now); return; }
                if (e.Mind.Kind == ActionKind.Drink) TryConsume(e.Id, ResourceKind.Water, now);
                else if (DietOf(e) == Diet.Carnivore || TryConsume(e.Id, ResourceKind.Food, now) != CommandReason.None)
                    TryConsume(e.Id, ResourceKind.Meat, now); // omnivores fall back to meat
                FinishAction(e, now); // consumed, or the stock vanished meanwhile: re-plan
                return;
        }
    }

    private void MoveTo(EntityState e, int cell)
    {
        int x = cell % options.MapWidth, y = cell / options.MapWidth;
        grid.Move(e.Id.Value, e.X, e.Y, x, y);
        e.X = x;
        e.Y = y;
    }

    // Neighbour that maximises Manhattan distance from the threat (ties: N, W, E, S order); -1 if none improves.
    private int FleeStep(EntityState e, EntityState threat)
    {
        int best = -1, bestDistance = Math.Abs(e.X - threat.X) + Math.Abs(e.Y - threat.Y);
        foreach (var (dx, dy) in Neighbours)
        {
            int x = e.X + dx, y = e.Y + dy;
            if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)])) continue;
            int d = Math.Abs(x - threat.X) + Math.Abs(y - threat.Y);
            if (d > bestDistance) { best = Cell(x, y); bestDistance = d; }
        }
        return best;
    }

    // Random passable neighbour from the AI stream (draws happen in scheduler order, so replay is stable).
    // First pass avoids tiles near remembered danger (T010); the second accepts any allowed tile.
    private int WanderStep(EntityState e)
    {
        int start = (int)(streams[AiStream].Draw() % 4);
        long now = tick + 1; // called while the next tick is being processed
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < 4; k++)
            {
                var (dx, dy) = Neighbours[(start + k) % 4];
                int x = e.X + dx, y = e.Y + dy;
                if (!InBounds(x, y) || !TerrainRules.IsPassable(terrain[Cell(x, y)]) || !WithinLeash(e, x, y)) continue;
                if (pass == 0 && RemembersDangerNear(e, x, y, now)) continue;
                return Cell(x, y);
            }
        return -1;
    }

    private bool RunMindWork(WorkItem item, long now)
    {
        bool think = item.Kind == WorkKind.Think;
        if (LiveFor(item, think) is not { } e) return false; // stale, dead or superseded: no budget used
        if (think) Think(e, now); else Step(e, now);
        return true;
    }

    private void WriteMind(BinaryWriter w, in MindState m)
    {
        w.Write((int)m.Kind);
        w.Write((uint)(m.Path?.Length ?? 0));
        if (m.Path is not null) foreach (int c in m.Path) w.Write(c);
        w.Write(m.PathIndex);
        w.Write(m.TargetEntity);
        w.Write(m.FleeStepsDone);
        w.Write(m.CommitUntil);
        w.Write(m.ThinkToken);
        w.Write(m.StepToken);
        w.Write(m.FatigueTick);
        w.Write(m.Fatigue);
        w.Write(m.Resting);
        w.Write(m.AwaitingPath);
        w.Write(m.NoFoodUntil);
        w.Write(m.NoWaterUntil);
        w.Write(m.Replans);
        w.Write(m.HuntUntil);
        w.Write(m.NoHuntUntil);
        w.Write(m.AttackReadyTick);
        w.Write(m.NoWorkUntil);
    }

    internal int LastDiscretionaryRun { get; private set; }
    internal int PendingDiscretionary => scheduler.DiscretionaryCount;

    // ---- Test hooks ----------------------------------------------------------------------------
    internal ActionKind ActionOf(long id) => FindLive(id) is int i and >= 0 ? entities[i].Mind.Kind : ActionKind.Idle;
    internal long FatigueOf(long id) => FatigueAt(entities[FindLive(id)].Mind, tick);
}
