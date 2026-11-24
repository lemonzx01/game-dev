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

    public void Append(long tick, int kind)
    {
        long next = checked(NextSequence + 1);
        int slot = (int)(((long)first + count) % Capacity);
        ring[slot] = new WorldEvent(NextSequence, tick, kind);
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
        }
    }
}
