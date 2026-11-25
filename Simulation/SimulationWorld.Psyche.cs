namespace FantasyWorld.Simulation;

/// <summary>Stable memory kinds; values are part of the hash schema. Importance defaults are in <see cref="PsycheConfig"/>.</summary>
public enum MemoryKind { FoundFood = 1, SawThreat = 2, WasAttacked = 3, Mated = 4, BecameParent = 5 }

/// <summary>Episodic working memory (ARCHITECTURE §9 M5). Subject = other entity (0 if none); X/Y = place.</summary>
public readonly record struct MemoryRecord(long Id, MemoryKind Kind, long Subject, int X, int Y, long Tick, int Importance, long LastRelevantTick);

/// <summary>Directional relationship toward another entity. Affinity −1000..1000; Familiarity grows with contact.</summary>
public readonly record struct Relationship(long Other, int Affinity, int Familiarity, long LastTick);

/// <summary>Read model for inspectors (not on ISimulation; A007 frozen). Reading it never changes NPC knowledge.</summary>
public sealed record EntityMindView(EntityId Id, int Bravery, int Sociability, int Fear,
    IReadOnlyList<MemoryRecord> Memories, IReadOnlyList<Relationship> Relationships);

/// <summary>Personality/emotion/memory tuning. Caps are the ARCHITECTURE §9 defaults (64 memories, 32 relationships).</summary>
public sealed record PsycheConfig(int MemoryCap = 64, int RelationshipCap = 32, int FearDecayPerTick = 2,
    int TraitJitter = 100, int DangerMemoryTicks = 600, int DangerAvoidRadius = 2, int FearBlocksMatingAbove = 500)
{
    public static PsycheConfig Default { get; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MemoryCap, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(RelationshipCap, 1);
        foreach (int v in new[] { FearDecayPerTick, TraitJitter, DangerMemoryTicks, DangerAvoidRadius })
            ArgumentOutOfRangeException.ThrowIfNegative(v);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TraitJitter, 500);
    }

    internal static int Importance(MemoryKind kind) => kind switch
    {
        MemoryKind.FoundFood => 100,
        MemoryKind.SawThreat => 300,
        MemoryKind.Mated => 500,
        MemoryKind.WasAttacked => 800,
        MemoryKind.BecameParent => 900,
        _ => 0,
    };
}

/// <summary>Per-entity psyche (part of EntityState; hashed).</summary>
internal sealed class Psyche
{
    public int Bravery = 500, Sociability = 500;
    public long FearTick;
    public int Fear;                          // fear at FearTick; decays linearly
    public readonly List<MemoryRecord> Memories = new();
    public readonly List<Relationship> Relationships = new(); // ascending Other id
}

/// <summary>
/// T010: personality traits (inherited with jitter), fear that rises on danger and decays with elapsed time, capped
/// episodic memories and directional relationships with deterministic eviction, and their effect on decisions.
/// </summary>
public sealed partial class SimulationWorld
{
    public const uint PsycheStream = 4;
    private long nextMemoryId = 1;

    private PsycheConfig PsyCfg => options.Psyche;

    private void InitPsyche(EntityState e, long now)
    {
        e.Psyche.FearTick = now;
        if (SpeciesOf(e) is null) return; // uncatalogued species keep neutral traits (500)
        var rng = streams[PsycheStream];
        int Roll() => (int)(rng.Draw() % 1001);
        int Inherit(int mother, int father)
        {
            int jitter = (int)(rng.Draw() % (ulong)(2 * PsyCfg.TraitJitter + 1)) - PsyCfg.TraitJitter;
            return Math.Clamp((mother + father) / 2 + jitter, 0, 1000);
        }
        var m = e.MotherId != 0 && FindLive(e.MotherId) is int mi and >= 0 ? entities[mi].Psyche : null;
        var f = e.FatherId != 0 && FindLive(e.FatherId) is int fi and >= 0 ? entities[fi].Psyche : null;
        if (m is not null && f is not null)
        {
            e.Psyche.Bravery = Inherit(m.Bravery, f.Bravery);
            e.Psyche.Sociability = Inherit(m.Sociability, f.Sociability);
        }
        else
        {
            e.Psyche.Bravery = Roll();
            e.Psyche.Sociability = Roll();
        }
    }

    // ---- Fear ----------------------------------------------------------------------------------

    private int FearAt(EntityState e, long now) =>
        (int)Math.Max(0, e.Psyche.Fear - (long)PsyCfg.FearDecayPerTick * (now - e.Psyche.FearTick));

    private void RaiseFear(EntityState e, int level, long now)
    {
        int current = FearAt(e, now);
        e.Psyche.Fear = Math.Min(1000, Math.Max(current, level));
        e.Psyche.FearTick = now;
    }

    /// <summary>Flee utility: proximity scaled by timidity, plus current fear. Bold animals ignore distant threats.</summary>
    private int FleeScore(EntityState e, int distance, long now)
    {
        int proximity = Math.Max(0, MindCfg.PerceptionRadius * 2 - distance) * 100;
        return proximity * (2000 - e.Psyche.Bravery) / 1000 + FearAt(e, now) / 2;
    }

    private void OnThreatSeen(EntityState e, EntityState threat, int distance, long now)
    {
        RaiseFear(e, 300 + Math.Max(0, MindCfg.PerceptionRadius * 2 - distance) * 40, now);
        Remember(e, MemoryKind.SawThreat, threat.Id.Value, threat.X, threat.Y, now);
    }

    private void OnAttacked(EntityState victim, EntityState attacker, long now)
    {
        RaiseFear(victim, 1000, now);
        Remember(victim, MemoryKind.WasAttacked, attacker.Id.Value, attacker.X, attacker.Y, now);
        Relate(victim, attacker.Id.Value, -600, now); // one-directional: the attacker's view is unchanged
    }

    // ---- Memory --------------------------------------------------------------------------------

    /// <summary>
    /// Adds or refreshes a memory. Same kind + subject + place refreshes LastRelevantTick (no duplicates). At the cap the
    /// lowest (importance, lastRelevantTick, id) is evicted first. Durable milestones live in ReadHistory and are unaffected.
    /// </summary>
    private void Remember(EntityState e, MemoryKind kind, long subject, int x, int y, long now)
    {
        var list = e.Psyche.Memories;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Kind == kind && list[i].Subject == subject && list[i].X == x && list[i].Y == y)
            {
                list[i] = list[i] with { LastRelevantTick = now };
                return;
            }
        if (list.Count >= PsyCfg.MemoryCap)
        {
            int victim = 0;
            for (int i = 1; i < list.Count; i++)
                if (EvictBefore(list[i], list[victim])) victim = i;
            list.RemoveAt(victim);
        }
        list.Add(new MemoryRecord(nextMemoryId++, kind, subject, x, y, now, PsycheConfig.Importance(kind), now));
    }

    private static bool EvictBefore(in MemoryRecord a, in MemoryRecord b) =>
        a.Importance != b.Importance ? a.Importance < b.Importance
        : a.LastRelevantTick != b.LastRelevantTick ? a.LastRelevantTick < b.LastRelevantTick
        : a.Id < b.Id;

    /// <summary>Adjusts this entity's relationship toward another (directional). At the cap the weakest
    /// (|affinity| + familiarity, lastTick, other id) is evicted.</summary>
    private void Relate(EntityState e, long other, int affinityDelta, long now)
    {
        var list = e.Psyche.Relationships;
        int i = list.FindIndex(r => r.Other == other);
        if (i >= 0)
        {
            var r = list[i];
            list[i] = r with { Affinity = Math.Clamp(r.Affinity + affinityDelta, -1000, 1000), Familiarity = Math.Min(1000, r.Familiarity + 50), LastTick = now };
            return;
        }
        if (list.Count >= PsyCfg.RelationshipCap)
        {
            int victim = 0;
            for (int k = 1; k < list.Count; k++)
            {
                var a = list[k]; var b = list[victim];
                int wa = Math.Abs(a.Affinity) + a.Familiarity, wb = Math.Abs(b.Affinity) + b.Familiarity;
                if (wa != wb ? wa < wb : a.LastTick != b.LastTick ? a.LastTick < b.LastTick : a.Other < b.Other) victim = k;
            }
            list.RemoveAt(victim);
        }
        var added = new Relationship(other, Math.Clamp(affinityDelta, -1000, 1000), 50, now);
        int at = list.FindIndex(r => r.Other > other);
        list.Insert(at < 0 ? list.Count : at, added);
    }

    /// <summary>True if the entity has any non-expired threat or attack memory.</summary>
    private bool HasRecentDangerMemory(EntityState e, long now)
    {
        foreach (var m in e.Psyche.Memories)
            if (m.Kind is MemoryKind.SawThreat or MemoryKind.WasAttacked && now - m.LastRelevantTick <= PsyCfg.DangerMemoryTicks)
                return true;
        return false;
    }

    /// <summary>True if (x, y) is within DangerAvoidRadius of a danger remembered in the last DangerMemoryTicks.</summary>
    private bool RemembersDangerNear(EntityState e, int x, int y, long now)
    {
        foreach (var m in e.Psyche.Memories)
            if (m.Kind is MemoryKind.SawThreat or MemoryKind.WasAttacked && now - m.LastRelevantTick <= PsyCfg.DangerMemoryTicks
                && Math.Max(Math.Abs(m.X - x), Math.Abs(m.Y - y)) <= PsyCfg.DangerAvoidRadius)
                return true;
        return false;
    }

    private void WritePsyche(BinaryWriter w, Psyche p)
    {
        w.Write(p.Bravery);
        w.Write(p.Sociability);
        w.Write(p.FearTick);
        w.Write(p.Fear);
        w.Write((uint)p.Memories.Count);
        foreach (var m in p.Memories)
        {
            w.Write(m.Id); w.Write((int)m.Kind); w.Write(m.Subject); w.Write(m.X); w.Write(m.Y);
            w.Write(m.Tick); w.Write(m.Importance); w.Write(m.LastRelevantTick);
        }
        w.Write((uint)p.Relationships.Count);
        foreach (var r in p.Relationships)
        {
            w.Write(r.Other); w.Write(r.Affinity); w.Write(r.Familiarity); w.Write(r.LastTick);
        }
    }

    // ---- Reads and test hooks ------------------------------------------------------------------

    public EntityMindView? ReadEntityMind(WorldId worldId, EntityId entityId)
    {
        CheckAccess();
        if (worldId != this.worldId || FindLive(entityId.Value) is not (int i and >= 0)) return null;
        var e = entities[i];
        return new EntityMindView(entityId, e.Psyche.Bravery, e.Psyche.Sociability, FearAt(e, tick),
            e.Psyche.Memories.ToArray(), e.Psyche.Relationships.ToArray());
    }

    internal void SetTraitsForTest(long id, int bravery, int sociability)
    {
        var p = entities[FindLive(id)].Psyche;
        p.Bravery = bravery;
        p.Sociability = sociability;
    }

    internal void RememberForTest(long id, MemoryKind kind, long subject, int x, int y) =>
        Remember(entities[FindLive(id)], kind, subject, x, y, tick);

    internal void RaiseFearForTest(long id, int level) => RaiseFear(entities[FindLive(id)], level, tick);

    internal void RelateForTest(long id, long other, int delta) => Relate(entities[FindLive(id)], other, delta, tick);
}
