using FantasyWorld.Simulation;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

var cases = new (string Name, Action Run)[]
{
    ("Pure simulation assembly", PureAssembly),
    ("A007 RNG vector", RngVector),
    ("Monotonic identity and checked exhaustion", Identity),
    ("Stable canonical hash and process replay", Replay),
    ("Paged events and explicit gap", Events),
    ("Owner-thread and re-entry guards", Guards),
    ("Snapshot lifetime", Snapshots)
};
int failures = 0;
foreach (var test in cases)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
    }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} T002 test groups passed");
return failures == 0 ? 0 : 1;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

static void PureAssembly()
{
    using var file = File.OpenRead(typeof(SimulationWorld).Assembly.Location);
    using var pe = new PEReader(file);
    var metadata = pe.GetMetadataReader();
    foreach (var handle in metadata.AssemblyReferences)
    {
        string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
        Check(!name.Contains("Godot", StringComparison.OrdinalIgnoreCase), name);
    }
}

static void RngVector()
{
    var rng = new RandomStream(42, 2, 17);
    Check(rng.State == 0xa95b939b9ebb349aUL, "initial state");
    Check(rng.Draw() == 0x5fb88fd6df7259e5UL, "draw 1");
    Check(rng.Draw() == 0xb8b82154685001e7UL, "draw 2");
}

static void Identity()
{
    var ids = new EntityIds();
    var living = new HashSet<EntityId> { ids.Allocate() };
    Check(living.Remove(new EntityId(1)), "first ID must be 1");
    Check(ids.Allocate().Value == 2 && ids.Allocate().Value == 3, "retired ID reused");
    var nearEnd = new EntityIds(long.MaxValue - 1);
    Check(nearEnd.Allocate().Value == long.MaxValue - 1, "last usable ID");
    Throws<OverflowException>(() => nearEnd.Allocate());
    Check(nearEnd.Next == long.MaxValue, "overflow mutated allocator");
}

static string[] RunHost(string seed, bool perturb)
{
    var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    info.ArgumentList.Add("exec");
    info.ArgumentList.Add("--runtimeconfig");
    info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Simulation.Tests.runtimeconfig.json"));
    info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Headless.dll"));
    info.ArgumentList.Add(seed);
    info.ArgumentList.Add("100");
    if (perturb) info.ArgumentList.Add("--perturb");
    using var process = Process.Start(info) ?? throw new Exception("Headless process failed to start.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30000))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException("Headless process exceeded 30 seconds.");
    }
    Check(process.ExitCode == 0, error.GetAwaiter().GetResult());
    return output.GetAwaiter().GetResult().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
}

static void Replay()
{
    var baseline = RunHost("42", false);
    Check(baseline.Length == 100 && baseline.SequenceEqual(RunHost("42", true)), "per-tick replay mismatch");
    Check(!baseline.SequenceEqual(RunHost("43", false)), "seed had no effect");
    Check(baseline[0] == "1 480C1B5668F5FF4B9A67A359E6AD9235F00C688E7CBC86E16A7FC94C09805D4B", "schema 1 canonical byte order");
}

static void Events()
{
    foreach (int capacity in new[] { 50, 4 })
    {
        var world = new SimulationWorld(new WorldId(1), 42, eventCapacity: capacity);
        Check(world.ReadEvents(0, 7).Records.IsEmpty, "empty log");
        for (int tick = 1; tick <= 50; tick++)
            world.Advance(null, eventKind: 1);

        long cursor = 0;
        if (capacity == 4)
        {
            var gap = world.ReadEvents(cursor, 7);
            Check(gap.Status == PageStatus.Gap && gap.MissingFrom == 1 && gap.MissingThrough == 46 &&
                gap.Records.IsEmpty && gap.NextCursor == 46, "overflow gap");
            cursor = gap.NextCursor;
        }
        while (cursor < 50)
        {
            var page = world.ReadEvents(cursor, 7);
            Check(page.Status == PageStatus.Ok && page.Records.Length == Math.Min(7L, 50 - cursor), "page size");
            foreach (var entry in page.Records)
            {
                cursor++;
                Check(entry.EventSequence == cursor && entry.Tick == cursor, "event ordering");
            }
            Check(page.NextCursor == cursor, "cursor did not advance");
        }
        Check(world.ReadEvents(50, 7).Records.IsEmpty && world.ReadEvents(51, 7).Status == PageStatus.InvalidRequest, "end cursor");
        Check(world.ReadEvents(-1, 7).Status == PageStatus.InvalidRequest && world.ReadEvents(50, 257).Status == PageStatus.InvalidRequest, "bad input");
    }
}

static void Guards()
{
    var world = new SimulationWorld(new WorldId(1), 42);
    string before = world.StateHash();
    Action[] api = [world.Step, () => world.ReadSnapshot(), () => world.ReadEvents(0, 1),
        () => world.ReadHistory(0, 1), () => world.ReadEntityDetails(new(1), new(1)),
        () => world.SubmitCommand(null!), () => world.StateHash()];
    foreach (Action call in api)
    {
        Task.Run(() => Throws<InvalidOperationException>(call)).GetAwaiter().GetResult();
        Throws<InvalidOperationException>(() => world.Advance(call));
    }
    Check(world.StateHash() == before, "guard failure mutated world");
    world.Step();
    Check(world.ReadSnapshot().Tick == 1, "guard was not released");
}

static void Snapshots()
{
    var world = new SimulationWorld(new WorldId(1), 42);
    var first = world.ReadSnapshot();
    Check(ReferenceEquals(first, world.ReadSnapshot()), "snapshot cache");
    for (int i = 0; i < 100; i++) world.Step();
    Check(first.Tick == 0 && world.ReadSnapshot().Tick == 100 && first.Chunks.IsEmpty, "snapshot lifetime");
}
