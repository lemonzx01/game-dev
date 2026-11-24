using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FantasyWorld.Simulation;

/// <summary>Thrown for any save that cannot be loaded; the caller's current world and save files are untouched.</summary>
public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// T013 versioned checkpoint (ARCHITECTURE §9). Layout: magic "FWSSAVE\0", format version, StateHash schema,
/// options JSON (config identity, so a save is self-contained), binary state payload, SHA-256 of options+payload,
/// and the StateHash at capture. Load validates into a separate world and requires the recomputed StateHash to equal
/// the saved one, so any field missed by the reader is detected. Capture happens between Steps on the owner thread;
/// there is no file I/O inside Step.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly byte[] Magic = "FWSSAVE\0"u8.ToArray();
    public const uint SaveFormatVersion = 1;
    public const uint StateSchema = 10;
    private static readonly JsonSerializerOptions OptionsJson = new() { WriteIndented = false };

    // ---- Save ----------------------------------------------------------------------------------

    public void Save(Stream output)
    {
        CheckAccess();
        byte[] optionsBytes = JsonSerializer.SerializeToUtf8Bytes(options, OptionsJson);
        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) WritePayload(w);
            payload = ms.ToArray();
        }
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(SaveFormatVersion);
        writer.Write(StateSchema);
        writer.Write(optionsBytes.Length);
        writer.Write(optionsBytes);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Write(Checksum(optionsBytes, payload));
        writer.Write(Encoding.ASCII.GetBytes(StateHash()));
    }

    private static byte[] Checksum(byte[] options, byte[] payload)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(options);
        sha.AppendData(payload);
        return sha.GetHashAndReset();
    }

    private void WritePayload(BinaryWriter w)
    {
        w.Write(tick);
        w.Write(nextCommandSequence);
        w.Write(ids.Next);
        w.Write(nextMemoryId);
        w.Write(nextCarcassToken);
        w.Write(scheduler.RunningTick);

        w.Write(streamTags.Length);
        foreach (uint tag in streamTags) { w.Write(tag); w.Write(streams[tag].State); }

        var log = events.Entries().ToList();
        w.Write(events.NextSequence);
        w.Write(log.Count);
        foreach (var e in log) WriteEvent(w, e);

        w.Write(commands.Count);
        foreach (var q in commands) { w.Write(q.Sequence); w.Write(q.TargetTick); WriteCommand(w, q.Command); }

        var pending = scheduler.Pending().ToList();
        w.Write(scheduler.NextOrdinal);
        w.Write(pending.Count);
        foreach (var (item, key, isRequired) in pending)
        {
            w.Write(key.DueTick); w.Write(key.Priority); w.Write(key.EntityId); w.Write(key.Ordinal);
            w.Write((int)item.Kind); w.Write(item.Subject); w.Write(item.Token); w.Write(isRequired);
        }

        w.Write(terrain.Length);
        foreach (var t in terrain) w.Write((byte)t);

        w.Write(resources.Count);
        for (int i = 0; i < resources.Count; i++) { w.Write(resources[i].Amount); w.Write(resources[i].RegrowScheduled); }

        w.Write(entities.Count);
        foreach (var e in entities) WriteEntity(w, e);

        w.Write(deaths.Count);
        foreach (var r in deaths)
        {
            w.Write(r.EntityId); w.Write(r.Species); w.Write(r.X); w.Write(r.Y); w.Write(r.BirthTick); w.Write(r.DeathTick);
            w.Write((int)r.Cause); w.Write(r.Hunger); w.Write(r.Thirst); w.Write(r.EventSequence); w.Write(r.Killer);
            w.Write((int)r.Sex); w.Write(r.MotherId); w.Write(r.FatherId);
        }

        w.Write(milestones.Count);
        foreach (var m in milestones) WriteEvent(w, m);

        w.Write(pathQueue.Count);
        foreach (var p in pathQueue) { w.Write(p.EntityId); w.Write((int)p.Kind); w.Write(p.Token); }

        w.Write(carcasses.Count);
        foreach (var (_, c) in carcasses) { w.Write(c.Cell); w.Write(c.Meat); w.Write(c.DecayTick); w.Write(c.Token); }

        WriteSettlements(w);
    }

    private static void WriteEvent(BinaryWriter w, in WorldEvent e)
    {
        w.Write(e.EventSequence); w.Write(e.Tick); w.Write(e.Kind); w.Write(e.CommandSequence); w.Write(e.Actor);
        w.Write(e.Target); w.Write(e.X); w.Write(e.Y); w.Write(e.Reason); w.Write(e.Amount);
    }

    private static WorldEvent ReadEvent(BinaryReader r) =>
        new(r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64());

    private static void WriteEntity(BinaryWriter w, EntityState e)
    {
        w.Write(e.Id.Value); w.Write(e.X); w.Write(e.Y); w.Write(e.Species); w.Write(e.Action); w.Write(e.LifeState);
        w.Write(e.HomeX); w.Write(e.HomeY); w.Write((int)e.Sex); w.Write(e.MotherId); w.Write(e.FatherId);
        w.Write(e.LifespanTicks); w.Write(e.PregnantUntil); w.Write(e.PregnancyFather); w.Write(e.ReproReadyTick);
        w.Write(e.BirthTick); w.Write(e.Vitals.Tick); w.Write(e.Vitals.Hunger); w.Write(e.Vitals.Thirst); w.Write(e.Vitals.Health);
        w.Write(e.ScheduledDeathTick); w.Write(e.DeathTick); w.Write((int)e.Cause);
        var m = e.Mind;
        w.Write((int)m.Kind);
        w.Write(m.Path is null ? -1 : m.Path.Length);
        if (m.Path is not null) foreach (int c in m.Path) w.Write(c);
        w.Write(m.PathIndex); w.Write(m.TargetEntity); w.Write(m.FleeStepsDone); w.Write(m.CommitUntil);
        w.Write(m.ThinkToken); w.Write(m.StepToken); w.Write(m.FatigueTick); w.Write(m.Fatigue); w.Write(m.Resting);
        w.Write(m.AwaitingPath); w.Write(m.NoFoodUntil); w.Write(m.NoWaterUntil); w.Write(m.Replans);
        w.Write(m.HuntUntil); w.Write(m.NoHuntUntil); w.Write(m.AttackReadyTick);
        var p = e.Psyche;
        w.Write(p.Bravery); w.Write(p.Sociability); w.Write(p.FearTick); w.Write(p.Fear);
        w.Write(p.Memories.Count);
        foreach (var mem in p.Memories)
        {
            w.Write(mem.Id); w.Write((int)mem.Kind); w.Write(mem.Subject); w.Write(mem.X); w.Write(mem.Y);
            w.Write(mem.Tick); w.Write(mem.Importance); w.Write(mem.LastRelevantTick);
        }
        w.Write(p.Relationships.Count);
        foreach (var rel in p.Relationships) { w.Write(rel.Other); w.Write(rel.Affinity); w.Write(rel.Familiarity); w.Write(rel.LastTick); }
        w.Write(m.NoWorkUntil); w.Write(e.SettlementId); w.Write((int)e.Job); w.Write((int)e.WorkGoal); w.Write((byte)e.CarryKind); w.Write(e.CarryAmount);
    }

    // ---- Load ----------------------------------------------------------------------------------

    /// <summary>Loads a checkpoint into a new world owned by the calling thread. Throws <see cref="SaveFormatException"/> on any problem.</summary>
    public static SimulationWorld Load(Stream input)
    {
        try
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic)) throw new SaveFormatException("Not a Fantasy World save.");
            uint version = reader.ReadUInt32();
            if (version != SaveFormatVersion) throw new SaveFormatException($"Unsupported save format version {version}.");
            uint schema = reader.ReadUInt32();
            if (schema != StateSchema) throw new SaveFormatException($"Save state schema {schema} needs a migration to {StateSchema}.");
            byte[] optionsBytes = ReadBlock(reader, 64 * 1024 * 1024);
            byte[] payload = ReadBlock(reader, 1024 * 1024 * 1024);
            byte[] checksum = reader.ReadBytes(32);
            if (checksum.Length != 32 || !checksum.AsSpan().SequenceEqual(Checksum(optionsBytes, payload)))
                throw new SaveFormatException("Save checksum mismatch (corrupt or truncated).");
            byte[] hashBytes = reader.ReadBytes(64);
            if (hashBytes.Length != 64) throw new SaveFormatException("Save is truncated.");
            string savedHash = Encoding.ASCII.GetString(hashBytes);

            var options = JsonSerializer.Deserialize<SimulationOptions>(optionsBytes, OptionsJson)
                ?? throw new SaveFormatException("Save has no options.");
            var world = new SimulationWorld(options); // validates options; built separately from any live world
            using (var r = new BinaryReader(new MemoryStream(payload), Encoding.UTF8))
            {
                world.ReadPayload(r);
                if (r.BaseStream.Position != r.BaseStream.Length) throw new SaveFormatException("Trailing data in save payload.");
            }
            if (world.StateHash() != savedHash) throw new SaveFormatException("Loaded state does not reproduce the saved StateHash.");
            return world;
        }
        catch (SaveFormatException) { throw; }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or ArgumentException or JsonException
                                       or OverflowException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new SaveFormatException($"Save rejected: {ex.Message}", ex);
        }
    }

    private static byte[] ReadBlock(BinaryReader r, int max)
    {
        int length = r.ReadInt32();
        if (length < 0 || length > max) throw new SaveFormatException("Save block length invalid.");
        byte[] block = r.ReadBytes(length);
        if (block.Length != length) throw new SaveFormatException("Save is truncated.");
        return block;
    }

    private static int Count(BinaryReader r, int max)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > max) throw new InvalidDataException($"Collection count {n} out of range.");
        return n;
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidDataException(message);
    }

    private void ReadPayload(BinaryReader r)
    {
        tick = r.ReadInt64();
        nextCommandSequence = r.ReadInt64();
        long nextId = r.ReadInt64();
        nextMemoryId = r.ReadInt64();
        nextCarcassToken = r.ReadInt64();
        long running = r.ReadInt64();
        Require(tick >= 0 && nextCommandSequence >= 1 && nextMemoryId >= 1 && nextCarcassToken >= 1, "Counters out of range.");
        Require(running == -1 || running == tick, "Scheduler position does not match the saved tick.");
        ids.Restore(nextId);

        int streamCount = Count(r, 64);
        Require(streamCount == streamTags.Length, "RNG stream set differs from configuration.");
        for (int i = 0; i < streamCount; i++)
        {
            uint tag = r.ReadUInt32();
            Require(tag == streamTags[i], "RNG stream tags differ.");
            streams[tag].State = r.ReadUInt64();
        }

        long nextEvent = r.ReadInt64();
        int logCount = Count(r, events.Capacity);
        var log = new List<WorldEvent>(logCount);
        for (int i = 0; i < logCount; i++) log.Add(ReadEvent(r));
        events.Restore(log, nextEvent);

        int commandCount = Count(r, options.CommandQueueCapacity);
        long lastSequence = 0;
        for (int i = 0; i < commandCount; i++)
        {
            long seq = r.ReadInt64(), target = r.ReadInt64();
            var command = ReadCommand(r);
            Require(seq > lastSequence && seq < nextCommandSequence && target == tick + 1, "Queued command order or target invalid.");
            Require(command.WorldId == worldId, "Queued command for another world.");
            commands.Enqueue(new QueuedCommand(seq, target, command));
            lastSequence = seq;
        }

        long nextOrdinal = r.ReadInt64();
        int workCount = Count(r, 50_000_000);
        var work = new List<(WorkItem, WorkKey, bool)>(workCount);
        for (int i = 0; i < workCount; i++)
        {
            var key = new WorkKey(r.ReadInt64(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64());
            var item = new WorkItem((WorkKind)r.ReadInt32(), r.ReadInt64(), r.ReadInt64());
            bool isRequired = r.ReadBoolean();
            // Required work always runs at its due tick, so it must lie in the future. Discretionary work (decisions)
            // over the per-tick budget is carried forward on purpose and may already be overdue.
            Require(Enum.IsDefined(item.Kind) && key.DueTick >= 0 && (!isRequired || key.DueTick > running), "Scheduled work invalid.");
            work.Add((item, key, isRequired));
        }
        scheduler.Restore(nextOrdinal, running, work);

        Require(Count(r, terrain.Length) == terrain.Length, "Terrain size differs.");
        for (int i = 0; i < terrain.Length; i++)
        {
            var t = (Terrain)r.ReadByte();
            Require(Enum.IsDefined(t), "Unknown terrain.");
            terrain[i] = t;
        }
        map = new MapView(options.MapWidth, options.MapHeight, System.Collections.Immutable.ImmutableArray.Create(terrain));

        Require(Count(r, resources.Count) == resources.Count, "Resource node set differs.");
        for (int i = 0; i < resources.Count; i++)
        {
            ref var node = ref resources[i];
            node.Amount = r.ReadInt32();
            node.RegrowScheduled = r.ReadBoolean();
            Require(node.Amount >= 0 && node.Amount <= resources.Config.RuleFor(node.Kind).Capacity, "Resource amount outside 0..capacity.");
        }

        int entityCount = Count(r, options.Ecology.MaxEntities + options.CommandQueueCapacity + 1);
        long lastId = 0;
        for (int i = 0; i < entityCount; i++)
        {
            var e = ReadEntity(r);
            Require(e.Id.Value > lastId && e.Id.Value < nextId, "Entity IDs must ascend and stay below NextEntityId.");
            Require(InBounds(e.X, e.Y) && e.LifeState is LifeStates.Alive or LifeStates.Dead, "Entity position or life state invalid.");
            entities.Add(e);
            if (e.LifeState == LifeStates.Alive) grid.Add(e.Id.Value, e.X, e.Y);
            lastId = e.Id.Value;
        }

        int deathCount = Count(r, int.MaxValue / 2);
        for (int i = 0; i < deathCount; i++)
        {
            var d = new DeathRecord(r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64(),
                (DeathCause)r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), (Sex)r.ReadInt32(), r.ReadInt64(), r.ReadInt64());
            Require(d.EntityId > 0 && d.EntityId < nextId && d.EventSequence < nextEvent, "Death record references the future.");
            deathIndex.Add(d.EntityId, deaths.Count); // duplicate IDs throw
            deaths.Add(d);
        }

        int milestoneCount = Count(r, int.MaxValue / 2);
        long lastMilestone = 0;
        for (int i = 0; i < milestoneCount; i++)
        {
            var m = ReadEvent(r);
            // N2: milestones belong to this save's timeline; nothing at or beyond NextEventSequence may exist.
            Require(m.EventSequence > lastMilestone && m.EventSequence < nextEvent, "Milestone outside the saved timeline.");
            milestones.Add(m);
            lastMilestone = m.EventSequence;
        }

        int pathCount = Count(r, entityCount);
        for (int i = 0; i < pathCount; i++) pathQueue.Enqueue(new PathRequest(r.ReadInt64(), (ActionKind)r.ReadInt32(), r.ReadInt64()));

        int carcassCount = Count(r, terrain.Length);
        for (int i = 0; i < carcassCount; i++)
        {
            var c = new Carcass { Cell = r.ReadInt32(), Meat = r.ReadInt32(), DecayTick = r.ReadInt64(), Token = r.ReadInt64() };
            Require(c.Cell >= 0 && c.Cell < terrain.Length && c.Meat > 0 && c.Token < nextCarcassToken, "Carcass invalid.");
            carcasses.Add(c.Cell, c);
        }
        ReadSettlements(r, Count);
        foreach (var e in entities)
            Require(e.SettlementId <= settlements.Count, "Member of an unknown settlement.");
        snapshot = null;
    }

    private EntityState ReadEntity(BinaryReader r)
    {
        var e = new EntityState { Id = new EntityId(r.ReadInt64()) };
        e.X = r.ReadInt32(); e.Y = r.ReadInt32(); e.Species = r.ReadInt32(); e.Action = r.ReadInt32(); e.LifeState = r.ReadInt32();
        e.HomeX = r.ReadInt32(); e.HomeY = r.ReadInt32(); e.Sex = (Sex)r.ReadInt32(); e.MotherId = r.ReadInt64(); e.FatherId = r.ReadInt64();
        e.LifespanTicks = r.ReadInt64(); e.PregnantUntil = r.ReadInt64(); e.PregnancyFather = r.ReadInt64(); e.ReproReadyTick = r.ReadInt64();
        e.BirthTick = r.ReadInt64();
        e.Vitals = new VitalsAnchor(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64());
        Require(e.Vitals.Hunger is >= 0 and <= VitalsConfig.MaxNeed && e.Vitals.Thirst is >= 0 and <= VitalsConfig.MaxNeed
                && e.Vitals.Health is >= 0 and <= VitalsConfig.MaxHealth && e.Vitals.Tick <= tick, "Vitals out of range.");
        e.ScheduledDeathTick = r.ReadInt64(); e.DeathTick = r.ReadInt64(); e.Cause = (DeathCause)r.ReadInt32();
        var m = new MindState { Kind = (ActionKind)r.ReadInt32() };
        int pathLength = r.ReadInt32();
        Require(pathLength >= -1 && pathLength <= terrain.Length, "Path length invalid.");
        if (pathLength >= 0)
        {
            m.Path = new int[pathLength];
            for (int i = 0; i < pathLength; i++)
            {
                m.Path[i] = r.ReadInt32();
                Require(m.Path[i] >= 0 && m.Path[i] < terrain.Length, "Path cell out of bounds.");
            }
        }
        m.PathIndex = r.ReadInt32(); m.TargetEntity = r.ReadInt64(); m.FleeStepsDone = r.ReadInt32(); m.CommitUntil = r.ReadInt64();
        m.ThinkToken = r.ReadInt64(); m.StepToken = r.ReadInt64(); m.FatigueTick = r.ReadInt64(); m.Fatigue = r.ReadInt64(); m.Resting = r.ReadBoolean();
        m.AwaitingPath = r.ReadBoolean(); m.NoFoodUntil = r.ReadInt64(); m.NoWaterUntil = r.ReadInt64(); m.Replans = r.ReadInt32();
        m.HuntUntil = r.ReadInt64(); m.NoHuntUntil = r.ReadInt64(); m.AttackReadyTick = r.ReadInt64();
        e.Mind = m;
        var p = e.Psyche;
        p.Bravery = r.ReadInt32(); p.Sociability = r.ReadInt32(); p.FearTick = r.ReadInt64(); p.Fear = r.ReadInt32();
        int memoryCount = Count(r, PsyCfg.MemoryCap);
        for (int i = 0; i < memoryCount; i++)
            p.Memories.Add(new MemoryRecord(r.ReadInt64(), (MemoryKind)r.ReadInt32(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64(), r.ReadInt32(), r.ReadInt64()));
        int relationCount = Count(r, PsyCfg.RelationshipCap);
        for (int i = 0; i < relationCount; i++)
            p.Relationships.Add(new Relationship(r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64()));
        e.Mind.NoWorkUntil = r.ReadInt64();
        e.SettlementId = r.ReadInt32(); e.Job = (Job)r.ReadInt32(); e.WorkGoal = (WorkGoal)r.ReadInt32();
        e.CarryKind = (ResourceKind)r.ReadByte(); e.CarryAmount = r.ReadInt32();
        Require(e.CarryAmount >= 0 && e.SettlementId >= 0, "Carried amount or settlement invalid.");
        return e;
    }

    private static FoundSettlementCommand ReadFound(BinaryReader r, WorldId world, long issuer, string mode)
    {
        int x = r.ReadInt32(), y = r.ReadInt32();
        var faction = (Faction)r.ReadInt32();
        int length = (int)r.ReadUInt32();
        Require(length is >= 1 and <= 128, "Settlement name length invalid.");
        return new FoundSettlementCommand(world, issuer, mode, x, y, faction, Encoding.UTF8.GetString(r.ReadBytes(length)));
    }

    private static GameCommand ReadCommand(BinaryReader r)
    {
        var world = new WorldId(r.ReadInt64());
        long issuer = r.ReadInt64();
        int modeLength = (int)r.ReadUInt32();
        Require(modeLength is >= 0 and <= 64, "Command mode length invalid.");
        string mode = Encoding.UTF8.GetString(r.ReadBytes(modeLength));
        var kind = (CommandKind)r.ReadInt32();
        return kind switch
        {
            CommandKind.SpawnEntity => new SpawnEntityCommand(world, issuer, mode, r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), (Sex)r.ReadInt32()),
            CommandKind.TakeResource => new TakeResourceCommand(world, issuer, mode, r.ReadInt32(), r.ReadInt32(), (ResourceKind)r.ReadByte(), r.ReadInt32()),
            CommandKind.SetTerrain => new SetTerrainCommand(world, issuer, mode, r.ReadInt32(), r.ReadInt32(), (Terrain)r.ReadByte()),
            CommandKind.FoundSettlement => ReadFound(r, world, issuer, mode),
            _ => throw new InvalidDataException($"Unknown command kind {kind}."),
        };
    }
}

/// <summary>
/// Safe file saving: write a temp file, verify it loads, then replace the target keeping a .bak of the previous save.
/// A failed write or verification leaves the previous save untouched.
/// </summary>
public static class SaveFiles
{
    public static void Write(SimulationWorld world, string path)
    {
        string temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                world.Save(fs);
                fs.Flush(flushToDisk: true);
            }
            using (var check = File.OpenRead(temp)) SimulationWorld.Load(check); // verify before replacing
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    public static SimulationWorld Read(string path)
    {
        using var fs = File.OpenRead(path);
        return SimulationWorld.Load(fs);
    }
}
