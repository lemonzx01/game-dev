using System.Security.Cryptography;

namespace FantasyWorld.Simulation;

public sealed class SimulationWorld : ISimulation
{
    private readonly Thread owner = Thread.CurrentThread;
    private readonly WorldId worldId;
    private readonly ulong seed;
    private readonly Dictionary<uint, RandomStream> streams = new();
    private readonly EntityIds ids = new();
    private readonly EventLog events;
    private readonly uint[] streamTags;
    private long tick;
    private bool stepping;
    private WorldSnapshot? snapshot;

    // Prewarm is part of the baseline configuration. Different insertion orders
    // of the same set of stream keys must produce the same continuation hash.
    public SimulationWorld(WorldId worldId, ulong seed, IEnumerable<uint>? prewarm = null, int eventCapacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(worldId.Value, 1);
        this.worldId = worldId;
        this.seed = seed;
        events = new EventLog(eventCapacity);
        foreach (uint tag in prewarm ?? Array.Empty<uint>())
            streams.TryAdd(tag, new RandomStream(seed, tag, 0));
        streamTags = streams.Keys.Order().ToArray();
    }

    private void CheckAccess()
    {
        if (Thread.CurrentThread != owner || stepping)
            throw new InvalidOperationException("Simulation API requires its idle owner thread.");
    }

    public void Step() => Advance(null);

    // A phase runs inside the same access guard as the fixed step. T002 has no gameplay phases.
    internal void Advance(Action? phase, int? eventKind = null)
    {
        CheckAccess();
        long next = checked(tick + 1);
        stepping = true;
        try
        {
            phase?.Invoke();
            if (eventKind.HasValue)
                events.Append(next, eventKind.Value);
            tick = next;
            snapshot = null;
        }
        finally
        {
            stepping = false;
        }
    }

    public CommandReceipt SubmitCommand(GameCommand command)
    {
        CheckAccess();
        if (command is null || command.WorldId != worldId)
            return new("Rejected", "InvalidWorld");
        // No command kinds are enabled in the T002 foundation.
        return new("Rejected", "UnsupportedCommand");
    }

    public WorldSnapshot ReadSnapshot()
    {
        CheckAccess();
        return snapshot ??= new(worldId, tick, []);
    }

    public EventPage ReadEvents(long afterSequence, int limit)
    {
        CheckAccess();
        return events.Read(afterSequence, limit);
    }

    public HistoryPage ReadHistory(long afterSequence, int limit)
    {
        CheckAccess();
        if (afterSequence < 0 || afterSequence > events.Latest || limit is < 1 or > 256)
            return new(PageStatus.InvalidRequest, [], afterSequence);
        return new(PageStatus.Ok, [], events.Latest);
    }

    public EntityDetails? ReadEntityDetails(WorldId worldId, EntityId entityId)
    {
        CheckAccess();
        return null; // No entity components or history records exist in the empty foundation world.
    }

    public string StateHash()
    {
        CheckAccess();
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        // Foundation schema 1. Fields follow A007 section 9. Empty subsystems
        // are represented by counts; the ring and RNG states are real data.
        writer.Write(1u); // schema
        writer.Write(1u); // RNG version
        writer.Write(worldId.Value);
        writer.Write(seed);
        writer.Write(events.Capacity);
        writer.Write(tick);
        writer.Write(ids.Next);
        writer.Write(1L); // next command sequence
        writer.Write(events.NextSequence);
        writer.Write(1L); // next scheduler enqueue ordinal
        writer.Write(0u); // queued commands
        writer.Write(0u); // authority grants
        writer.Write(0u); // entities
        writer.Write(0u); // scheduled work
        writer.Write((uint)streamTags.Length);
        foreach (uint tag in streamTags)
        {
            writer.Write(tag);
            writer.Write(0UL); // world scope
            writer.Write(streams[tag].State);
        }
        events.WriteCanonical(writer);
        writer.Write(0u); // durable milestones
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)));
    }
}
