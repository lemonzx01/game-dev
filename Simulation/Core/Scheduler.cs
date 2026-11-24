namespace FantasyWorld.Simulation;

internal enum WorkKind { RegrowResource = 1, EntityDeath = 2, Think = 3, ActionStep = 4, CarcassDecay = 5, OldAge = 6, Birth = 7, CropGrow = 8, TestNoop = 99 }

/// <summary>Ordering key from ARCHITECTURE §4: (due tick, priority, EntityId, enqueue ordinal), all ascending.</summary>
internal readonly record struct WorkKey(long DueTick, int Priority, long EntityId, long Ordinal) : IComparable<WorkKey>
{
    public int CompareTo(WorkKey other)
    {
        int c = DueTick.CompareTo(other.DueTick);
        if (c == 0) c = Priority.CompareTo(other.Priority);
        if (c == 0) c = EntityId.CompareTo(other.EntityId);
        return c != 0 ? c : Ordinal.CompareTo(other.Ordinal);
    }
}

/// <summary>Token lets an entity supersede its own pending item: only the item whose token matches the entity's current one runs.</summary>
internal readonly record struct WorkItem(WorkKind Kind, long Subject, long Token = 0);

/// <summary>
/// Deterministic tick scheduler. Required work (vitals, death, environment) always runs at its due tick;
/// discretionary work (AI) is limited to a per-tick count budget and the ordered remainder carries forward.
/// </summary>
internal sealed class Scheduler
{
    private readonly PriorityQueue<WorkItem, WorkKey> required = new();
    private readonly PriorityQueue<WorkItem, WorkKey> discretionary = new();
    private long runningTick = -1;

    public long NextOrdinal { get; private set; } = 1;
    public int DiscretionaryBudget { get; }
    public int RequiredCount => required.Count;
    public int DiscretionaryCount => discretionary.Count;

    public Scheduler(int discretionaryBudget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(discretionaryBudget, 1);
        DiscretionaryBudget = discretionaryBudget;
    }

    public void Schedule(long dueTick, int priority, long entityId, WorkItem item, bool isRequired)
    {
        // Work created while a tick runs must land in a later tick, so one tick never loops on itself.
        if (dueTick <= runningTick)
            throw new InvalidOperationException($"Work due at {dueTick} scheduled during tick {runningTick}.");
        var key = new WorkKey(dueTick, priority, entityId, NextOrdinal);
        NextOrdinal = checked(NextOrdinal + 1);
        (isRequired ? required : discretionary).Enqueue(item, key);
    }

    /// <summary>
    /// Runs all due required work, then due discretionary work until <see cref="DiscretionaryBudget"/> items have done real
    /// work. <paramref name="execute"/> returns false for a stale (superseded) item: it is dequeued but does not consume
    /// budget, so stale entries cannot starve live decisions (T014). Returns discretionary items that did work.
    /// </summary>
    public int Run(long tick, Func<WorkItem, bool> execute)
    {
        runningTick = tick;
        try
        {
            while (required.TryPeek(out var item, out var key) && key.DueTick <= tick)
            {
                required.Dequeue();
                execute(item);
            }
            int done = 0;
            while (done < DiscretionaryBudget && discretionary.TryPeek(out var item, out var key) && key.DueTick <= tick)
            {
                discretionary.Dequeue();
                if (execute(item)) done++;
            }
            return done;
        }
        finally
        {
            runningTick = tick; // keeps later scheduling strictly in the future of the last completed tick
        }
    }

    internal long RunningTick => runningTick;

    /// <summary>Discretionary items already past due at <paramref name="tick"/> (diagnostics; O(n)).</summary>
    internal int OverdueDiscretionary(long tick) => discretionary.UnorderedItems.Count(e => e.Priority.DueTick < tick);

    /// <summary>All pending work with its key and queue (save/load).</summary>
    internal IEnumerable<(WorkItem Item, WorkKey Key, bool Required)> Pending() =>
        required.UnorderedItems.Select(e => (e.Element, e.Priority, true))
            .Concat(discretionary.UnorderedItems.Select(e => (e.Element, e.Priority, false)))
            .OrderBy(e => e.Priority);

    internal void Restore(long nextOrdinal, long running, IEnumerable<(WorkItem Item, WorkKey Key, bool Required)> items)
    {
        required.Clear();
        discretionary.Clear();
        foreach (var (item, key, isRequired) in items)
        {
            if (key.Ordinal >= nextOrdinal || key.Ordinal < 1) throw new InvalidDataException("Scheduler ordinal out of range.");
            (isRequired ? required : discretionary).Enqueue(item, key);
        }
        NextOrdinal = nextOrdinal;
        runningTick = running;
    }

    public void WriteCanonical(BinaryWriter writer)
    {
        writer.Write(NextOrdinal);
        Write(writer, required);
        Write(writer, discretionary);
    }

    private static void Write(BinaryWriter writer, PriorityQueue<WorkItem, WorkKey> queue)
    {
        writer.Write((uint)queue.Count);
        // Keys are unique (ordinal), so sorting gives the exact execution order independent of heap layout.
        foreach (var (item, key) in queue.UnorderedItems.OrderBy(e => e.Priority))
        {
            writer.Write(key.DueTick);
            writer.Write(key.Priority);
            writer.Write(key.EntityId);
            writer.Write(key.Ordinal);
            writer.Write((int)item.Kind);
            writer.Write(item.Subject);
            writer.Write(item.Token);
        }
    }
}
