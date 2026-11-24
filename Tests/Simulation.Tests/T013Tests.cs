using FantasyWorld.Simulation;
using System.Collections.Immutable;
using System.Security.Cryptography;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T013 acceptance: state/pending/RNG/time roundtrip, malformed saves rejected, replay continuation, N2.</summary>
internal static class T013Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T013 full state roundtrip incl. pending commands, RNG, minds", Roundtrip),
        ("T013 loaded world continues identically to the original", ReplayContinuation),
        ("T013 malformed saves rejected without side effects", Malformed),
        ("T013 N2 history bound to the save timeline", HistoryTimeline),
        ("T013 file save is atomic with backup; failure keeps old save", FileSafety),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    /// <summary>A busy world: generated map, all species with short lives, predation, births, pending commands.</summary>
    private static SimulationWorld BusyWorld(int ticks)
    {
        var species = Default.Values.Select(d => d with
        {
            MaturityTicks = d.MaturityTicks / 60, LifespanTicks = d.LifespanTicks / 60,
            GestationTicks = d.GestationTicks / 60, ReproCooldownTicks = d.ReproCooldownTicks / 60,
        });
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 99, MapWidth = 48, MapHeight = 48, Species = Build(species),
            Vitals = VitalsConfig.Default with { HungerPerTick = 900, ThirstPerTick = 1200 },
            Ecology = EcologyConfig.Default with { MaxEntities = 300 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        var map = world.ReadMap();
        var land = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] is Terrain.Grass or Terrain.Forest).Where((_, k) => k % 8 == 0).Take(100).ToList();
        int[] mix = [Rabbit, Deer, Rabbit, Sheep, Deer, Rabbit, Wolf];
        for (int k = 0; k < land.Count; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land[k] % 48, land[k] / 48, mix[k % mix.Length]));
        for (int t = 0; t < ticks; t++) world.Step();
        return world;
    }

    private static void SubmitPending(SimulationWorld world)
    {
        var map = world.ReadMap();
        int land = Enumerable.Range(0, map.Cells.Length).First(i => map.Cells[i] == Terrain.Grass);
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land % 48, land / 48, Deer, Sex.Female));
        world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 0, 0, Terrain.Mountain));
    }

    private static byte[] SaveBytes(SimulationWorld world)
    {
        using var ms = new MemoryStream();
        world.Save(ms);
        return ms.ToArray();
    }

    private static SimulationWorld LoadBytes(byte[] bytes) => SimulationWorld.Load(new MemoryStream(bytes));

    private static void Roundtrip()
    {
        var a = BusyWorld(1500);
        SubmitPending(a);
        var bytes = SaveBytes(a);
        var b = LoadBytes(bytes);
        Check(b.StateHash() == a.StateHash(), "hash after load");
        Check(b.ReadSnapshot().Chunks.SelectMany(c => c).SequenceEqual(a.ReadSnapshot().Chunks.SelectMany(c => c)) && b.ReadSnapshot().Time == a.ReadSnapshot().Time, "snapshot rows and time");
        var ea = a.ReadEvents(0, 256); var eb = b.ReadEvents(0, 256);
        Check(ea.OldestAvailable == eb.OldestAvailable && ea.LatestSequence == eb.LatestSequence, "event log bounds");
        long cursor = ea.OldestAvailable - 1;
        Check(a.ReadEvents(cursor, 256).Records.SequenceEqual(b.ReadEvents(cursor, 256).Records), "retained events");
        Check(a.ReadHistory(0, 256).Records.SequenceEqual(b.ReadHistory(0, 256).Records), "durable history");
        int live = 0;
        foreach (var row in a.ReadSnapshot().Chunks.SelectMany(c => c))
        {
            Check(a.ReadEntityDetails(W, row.Id) == b.ReadEntityDetails(W, row.Id), $"details of {row.Id}");
            var ma = a.ReadEntityMind(W, row.Id)!; var mb = b.ReadEntityMind(W, row.Id)!;
            Check(ma.Bravery == mb.Bravery && ma.Fear == mb.Fear && ma.Memories.SequenceEqual(mb.Memories) && ma.Relationships.SequenceEqual(mb.Relationships), $"mind of {row.Id}");
            live++;
        }
        Check(live > 20, $"busy world has animals ({live})");
        Check(a.ReadHistory(0, 256).Records.Any(r => r.Kind == EventKinds.EntityBorn) && a.ReadHistory(0, 256).Records.Any(r => r.Kind == EventKinds.EntityDied), "births and deaths captured");
        Check(SaveBytes(b).SequenceEqual(bytes), "save of a loaded world is byte-identical");
    }

    private static void ReplayContinuation()
    {
        var a = BusyWorld(800);
        SubmitPending(a); // accepted, not yet executed
        var b = LoadBytes(SaveBytes(a));
        long ca = a.ReadEvents(0, 1).LatestSequence, cb = ca;
        for (int t = 0; t < 600; t++)
        {
            a.Step();
            b.Step();
            Check(a.StateHash() == b.StateHash(), $"diverged at tick {a.LastCompletedTick}");
            var pa = a.ReadEvents(ca, 256); var pb = b.ReadEvents(cb, 256);
            Check(pa.Records.SequenceEqual(pb.Records), $"events differ at tick {a.LastCompletedTick}");
            ca = pa.NextCursor; cb = pb.NextCursor;
        }
        var applied = a.ReadHistory(0, 1); // touch API on both
        Check(b.ReadHistory(0, 1).Records.SequenceEqual(applied.Records), "history identical after continuation");
    }

    private static byte[] Rebuild(byte[] save, Func<byte[], byte[]>? payloadEdit = null, Func<byte[], byte[]>? optionsEdit = null)
    {
        var r = new BinaryReader(new MemoryStream(save));
        byte[] head = r.ReadBytes(16);
        byte[] options = r.ReadBytes(r.ReadInt32());
        byte[] payload = r.ReadBytes(r.ReadInt32());
        r.ReadBytes(32);
        byte[] hash = r.ReadBytes(64);
        options = optionsEdit?.Invoke(options) ?? options;
        payload = payloadEdit?.Invoke(payload) ?? payload;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(head); w.Write(options.Length); w.Write(options); w.Write(payload.Length); w.Write(payload);
        w.Write(SHA256.HashData([.. options, .. payload])); // valid checksum, so deeper validation must catch it
        w.Write(hash);
        return ms.ToArray();
    }

    private static void Malformed()
    {
        var world = BusyWorld(300);
        var good = SaveBytes(world);
        string before = world.StateHash();
        bool Rejected(byte[] bytes)
        {
            try { LoadBytes(bytes); return false; } catch (SaveFormatException) { return true; }
        }
        Check(!Rejected(good), "good save loads");
        Check(Rejected([.. "NOTASAVE"u8.ToArray(), .. good[8..]]), "wrong magic");
        var version = (byte[])good.Clone(); version[8] = 9;
        Check(Rejected(version), "unknown format version");
        foreach (int cut in new[] { 0, 7, 20, good.Length / 2, good.Length - 70, good.Length - 1 })
            Check(Rejected(good[..cut]), $"truncated at {cut}");
        var flipped = (byte[])good.Clone(); flipped[good.Length / 2] ^= 0xFF;
        Check(Rejected(flipped), "corrupt byte (checksum)");
        Check(Rejected(Rebuild(good, p => { var c = (byte[])p.Clone(); c[0] ^= 1; return c; })), "edited tick (valid checksum)");
        Check(Rejected(Rebuild(good, p => [.. p, 0])), "trailing payload data");
        Check(Rejected(Rebuild(good, p => { var c = (byte[])p.Clone(); c[^5] ^= 0x40; return c; })), "edited carcass/path data (hash mismatch)");
        Check(Rejected(Rebuild(good, null, o => "{\"WorldId\":"u8.ToArray())), "broken options JSON");
        Check(Rejected(Rebuild(good, null, o => System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(o).Replace("\"Seed\":99", "\"Seed\":98")))), "options swapped (different world)");
        world.Step();
        Check(world.StateHash() != before && world.LastCompletedTick == 301, "failed loads never touched the running world");
    }

    private static void HistoryTimeline()
    {
        // Save A → X dies → save B → load A: no death of X; load B: death of X.
        var vitals = new VitalsConfig(100_000, 0, 250_000, 0, 0, 250_000, 100_000, 4); // starves at tick 15 after spawn at 1
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 5, MapWidth = 16, MapHeight = 16, Vitals = vitals, Mind = MindConfig.Disabled,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
        var map = world.ReadMap();
        int land = Enumerable.Range(0, map.Cells.Length).First(i => map.Cells[i] == Terrain.Grass);
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, land % 16, land / 16, 1));
        for (int t = 0; t < 5; t++) world.Step();
        var saveA = SaveBytes(world);
        for (int t = 0; t < 15; t++) world.Step();
        Check(world.ReadHistory(0, 256).Records.Any(r => r.Kind == EventKinds.EntityDied && r.Target == 1), "X died after A");
        var saveB = SaveBytes(world);
        var a = LoadBytes(saveA);
        Check(a.ReadHistory(0, 256).Records.IsEmpty, "A's timeline has no death");
        Check(a.ReadEntityDetails(W, new EntityId(1)) is { LifeState: LifeStates.Alive, DeathTick: null }, "X alive in A");
        var b = LoadBytes(saveB);
        Check(b.ReadEntityDetails(W, new EntityId(1)) is { DeathCause: DeathCause.Starvation }, "B keeps the death");
        for (int t = 0; t < 15; t++) a.Step();
        Check(a.StateHash() == b.StateHash(), "A continued to B's tick reproduces B exactly");
    }

    private static void FileSafety()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fws-t013-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "world.fws");
            var world = BusyWorld(200);
            SaveFiles.Write(world, path);
            byte[] first = File.ReadAllBytes(path);
            Check(SaveFiles.Read(path).StateHash() == world.StateHash(), "file roundtrip");
            for (int t = 0; t < 50; t++) world.Step();
            SaveFiles.Write(world, path);
            Check(File.Exists(path + ".bak") && File.ReadAllBytes(path + ".bak").SequenceEqual(first), "previous save kept as .bak");
            byte[] second = File.ReadAllBytes(path);
            bool threw = false;
            try { Task.Run(() => SaveFiles.Write(world, path)).GetAwaiter().GetResult(); } // wrong thread: Save throws mid-write
            catch (InvalidOperationException) { threw = true; }
            Check(threw && File.ReadAllBytes(path).SequenceEqual(second) && !File.Exists(path + ".tmp"), "failed save leaves the old file and no temp");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
