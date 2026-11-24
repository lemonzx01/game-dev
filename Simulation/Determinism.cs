using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FantasyWorld.Simulation;

public sealed class EntityIds
{
    public long Next { get; private set; }

    public EntityIds(long next = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(next, 1);
        Next = next;
    }

    /// <summary>Save/load: restores the counter; never lowers it below 1.</summary>
    internal void Restore(long next)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(next, 1);
        Next = next;
    }

    public EntityId Allocate()
    {
        long id = Next;
        Next = checked(id + 1);
        return new EntityId(id);
    }
}

public sealed class RandomStream
{
    public ulong State { get; internal set; } // settable for save/load restore

    public RandomStream(ulong seed, uint systemTag, ulong scopeId)
    {
        Span<byte> key = stackalloc byte[30];
        "FWS-RNG-v1"u8.CopyTo(key);
        BinaryPrimitives.WriteUInt64LittleEndian(key[10..], seed);
        BinaryPrimitives.WriteUInt32LittleEndian(key[18..], systemTag);
        BinaryPrimitives.WriteUInt64LittleEndian(key[22..], scopeId);
        State = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(key));
    }

    public ulong Draw()
    {
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            ulong z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
