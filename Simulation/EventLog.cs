using System.Collections.Immutable;

namespace FantasyWorld.Simulation;

internal sealed class EventLog
{
    private readonly WorldEvent[] ring;
    private int first;
    private int count;
    public long NextSequence { get; private set; } = 1;
    public int Capacity => ring.Length;
    public long Latest => NextSequence - 1;

    public EventLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ring = new WorldEvent[capacity];
    }

    /// <summary>Appends a copy of <paramref name="entry"/> with the next EventSequence; evicts the oldest when full.</summary>
    public void Append(in WorldEvent entry)
    {
        long next = checked(NextSequence + 1);
        int slot = (int)(((long)first + count) % Capacity);
        ring[slot] = entry with { EventSequence = NextSequence };
        if (count == Capacity)
            first = (first + 1) % Capacity;
        else
            count++;
        NextSequence = next;
    }

    public EventPage Read(long after, int limit)
    {
        long oldest = NextSequence - count;
        if (after < 0 || after > Latest || limit is < 1 or > 256)
            return new(PageStatus.InvalidRequest, oldest, Latest, [], after);
        if (after < oldest - 1)
            return new(PageStatus.Gap, oldest, Latest, [], oldest - 1, after + 1, oldest - 1);

        int size = (int)Math.Min(limit, Latest - after);
        var records = ImmutableArray.CreateBuilder<WorldEvent>(size);
        long offset = after - (oldest - 1);
        for (int i = 0; i < size; i++)
            records.Add(ring[(int)((first + offset + i) % Capacity)]);
        return new(PageStatus.Ok, oldest, Latest, records.MoveToImmutable(), after + size);
    }

    /// <summary>Retained entries, oldest first (save/load).</summary>
    internal IEnumerable<WorldEvent> Entries()
    {
        for (int i = 0; i < count; i++) yield return ring[(int)(((long)first + i) % Capacity)];
    }

    /// <summary>Restores retained entries; sequences must be contiguous and end at nextSequence − 1.</summary>
    internal void Restore(IReadOnlyList<WorldEvent> entries, long nextSequence)
    {
        if (entries.Count > Capacity || nextSequence < 1 || nextSequence - 1 < entries.Count)
            throw new InvalidDataException("Event log bounds invalid.");
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].EventSequence != nextSequence - entries.Count + i)
                throw new InvalidDataException("Event log sequence is not contiguous.");
        Array.Clear(ring);
        for (int i = 0; i < entries.Count; i++) ring[i] = entries[i];
        first = 0;
        count = entries.Count;
        NextSequence = nextSequence;
    }

    public void WriteCanonical(BinaryWriter writer)
    {
        writer.Write(NextSequence - count);
        writer.Write(Latest);
        writer.Write((uint)count);
        for (int i = 0; i < count; i++)
        {
            var entry = ring[(int)(((long)first + i) % Capacity)];
            writer.Write(entry.EventSequence);
            writer.Write(entry.Tick);
            writer.Write(entry.Kind);
            writer.Write(entry.CommandSequence);
            writer.Write(entry.Actor);
            writer.Write(entry.Target);
            writer.Write(entry.X);
            writer.Write(entry.Y);
            writer.Write(entry.Reason);
            writer.Write(entry.Amount);
        }
    }
}
