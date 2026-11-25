using System.Collections.Immutable;
using System.Diagnostics;
using FantasyWorld.Simulation;

// T014 headless benchmark (ARCHITECTURE §10). Usage: dotnet run -c Release --project Tests/Benchmarks [populations...] [--ticks N]
// Each scenario: generated map, all four species, entities spawned on tick 1 through God commands, warm-up, then
// per-tick Step() timing. Reports p50/p95/p99, throughput, managed allocations per tick, GC counts, working set,
// path-queue backlog, live population and save size/time. Seed and config are fixed so runs are comparable.
var populations = new List<int>();
int ticks = 2000, warmup = 300;
bool tuned = false;
bool body = false; // --body: R1 realism (sleep, species bodies) as in the game
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--ticks") ticks = int.Parse(args[++i]);
    else if (args[i] == "--tuned") tuned = true;
    else if (args[i] == "--body") body = true;
    else populations.Add(int.Parse(args[i]));
}
if (populations.Count == 0) populations.AddRange([100, 1000, 5000]);

Console.WriteLine(tuned ? "Tuned budgets" : "Default budgets");
Console.WriteLine($"| entities (start) | map | ticks | p50 ms | p95 ms | p99 ms | max ms | ticks/s | alloc KB/tick | gen0/gen1/gen2 | working set MB | live (end) | path backlog max/end | decisions pending max/end | overdue end | save KB | save ms | load ms |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
foreach (int population in populations)
    Run(population);

void Run(int population)
{
    int size = population <= 1000 ? 128 : 256;
    var world = new SimulationWorld(new SimulationOptions
    {
        WorldId = new WorldId(1), Seed = 20261124, MapWidth = size, MapHeight = size,
        Species = SpeciesCatalog.Default,
        CommandQueueCapacity = 1024, MaxEventsPerTick = 2048, EventCapacity = 8192,
        // --tuned: decision and path budgets scaled to the population (demand ≈ population / ThinkIntervalTicks per tick).
        DiscretionaryWorkBudget = tuned ? Math.Max(256, population / 10 * 5 / 4) : 256,
        Mind = tuned ? MindConfig.Default with { PathNodeBudgetPerTick = Math.Max(4000, population * 4) } : MindConfig.Default,
        Ecology = EcologyConfig.Default with { MaxEntities = population * 3 },
        Body = body ? BodyConfig.Realistic : BodyConfig.Default, // R1 sleep/bodies on (as in the game)
        Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(1, AuthorityGrant.God),
    });
    var map = world.ReadMap();
    int[] mix = [SpeciesCatalog.Deer, SpeciesCatalog.Rabbit, SpeciesCatalog.Sheep, SpeciesCatalog.Rabbit, SpeciesCatalog.Deer, SpeciesCatalog.Rabbit, SpeciesCatalog.Sheep, SpeciesCatalog.Wolf];
    var land = Enumerable.Range(0, map.Cells.Length).Where(c => map.Cells[c] is Terrain.Grass or Terrain.Forest).ToList();
    int stride = Math.Max(1, land.Count / population);
    int placed = 0;
    for (int k = 0; placed < population && k < land.Count; k += stride)
    {
        if (world.SubmitCommand(new SpawnEntityCommand(new WorldId(1), 1, CommandModes.God, land[k] % size, land[k] / size, mix[placed % mix.Length])).Status != "Queued")
        {
            world.Step(); // queue full: let it drain, then continue
            k -= stride;
            continue;
        }
        placed++;
    }
    long cursor = 0;
    void Drain()
    {
        while (true)
        {
            var page = world.ReadEvents(cursor, 256);
            if (page.Status == PageStatus.Gap) { cursor = page.NextCursor; continue; }
            if (page.NextCursor == cursor) return;
            cursor = page.NextCursor; // a host reading every tick, like the Godot bridge
        }
    }
    for (int t = 0; t < warmup; t++) { world.Step(); Drain(); }

    var times = new double[ticks];
    int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
    long allocBefore = GC.GetAllocatedBytesForCurrentThread();
    int maxBacklog = 0, maxDecisions = 0;
    var total = Stopwatch.StartNew();
    for (int t = 0; t < ticks; t++)
    {
        long start = Stopwatch.GetTimestamp();
        world.Step();
        times[t] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Drain();
        maxBacklog = Math.Max(maxBacklog, world.PendingPathRequestsForBenchmark);
        maxDecisions = Math.Max(maxDecisions, world.PendingDecisionsForBenchmark);
    }
    total.Stop();
    long alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
    Array.Sort(times);
    double P(double q) => times[Math.Min(times.Length - 1, (int)(q * times.Length))];
    int live = world.ReadSnapshot().Chunks.Sum(c => c.Length);

    var save = Stopwatch.StartNew();
    using var ms = new MemoryStream();
    world.Save(ms);
    save.Stop();
    var load = Stopwatch.StartNew();
    var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
    load.Stop();
    if (loaded.StateHash() != world.StateHash()) throw new Exception("save/load mismatch in benchmark");

    Console.WriteLine($"| {placed} | {size}x{size} | {ticks} | {P(0.50):F3} | {P(0.95):F3} | {P(0.99):F3} | {times[^1]:F3} | {ticks / total.Elapsed.TotalSeconds:F0} | " +
        $"{alloc / 1024.0 / ticks:F1} | {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} | " +
        $"{Process.GetCurrentProcess().WorkingSet64 / 1048576} | {live} | {maxBacklog}/{world.PendingPathRequestsForBenchmark} | {maxDecisions}/{world.PendingDecisionsForBenchmark} | {world.OverdueDecisionsForBenchmark} | {ms.Length / 1024} | {save.Elapsed.TotalMilliseconds:F0} | {load.Elapsed.TotalMilliseconds:F0} |");
}
