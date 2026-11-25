using System.Diagnostics;
using System.Globalization;
using FantasyWorld.Simulation;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    var settings = Settings.Parse(args);
    var world = Opening.Create(settings);
    var report = new Report(settings);
    long cursor = 0;
    for (int day = 1; day <= settings.Days; day++)
    {
        var row = new Day(day);
        var elapsed = new long[CalendarConfig.Default.TicksPerDay];
        for (int t = 0; t < elapsed.Length; t++)
        {
            long start = Stopwatch.GetTimestamp();
            world.Step();
            elapsed[t] = Stopwatch.GetTimestamp() - start;
            EventPage page;
            do
            {
                page = world.ReadEvents(cursor, 256);
                if (page.Status != PageStatus.Ok)
                    throw new InvalidOperationException($"Event cursor {cursor}: {page.Status} at day {day}, tick {t + 1}.");
                foreach (var e in page.Records) row.Record(e, id => world.ReadEntityDetails(new WorldId(1), new EntityId(id))?.SpeciesKey ?? 0);
                cursor = page.NextCursor;
            } while (cursor < page.LatestSequence);
            if (day == 1 && t == 0) report.Opening = Day.Population(world.ReadSnapshot());
        }
        row.Alive = Day.Population(world.ReadSnapshot());
        row.Towns = world.ReadSettlements().ToArray();
        Array.Sort(elapsed);
        row.Mean = elapsed.Average() * 1000 / Stopwatch.Frequency;
        row.P99 = elapsed[(int)Math.Ceiling(elapsed.Length * .99) - 1] * 1000.0 / Stopwatch.Frequency;
        report.Days.Add(row);
    }
    report.Write();
    Console.WriteLine(Path.GetFullPath(settings.Output));
    // RB: one machine-readable summary line per run (for multi-seed balance checks).
    var last = report.Days[^1];
    var speciesDeaths = report.Days.SelectMany(d => d.SpeciesDeaths).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
    var speciesBirths = report.Days.SelectMany(d => d.SpeciesBirths).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
    long deaths = speciesDeaths.Values.Sum(), starved = speciesDeaths.Where(kv => kv.Key.Cause == DeathCause.Starvation).Sum(kv => kv.Value);
    int alive = last.Alive.Values.Sum();
    Console.WriteLine("SUMMARY " + System.Text.Json.JsonSerializer.Serialize(new
    {
        seed = settings.Seed, days = settings.Days, body = settings.Body, alive,
        species = last.Alive.ToDictionary(kv => SpeciesCatalog.Default.TryGetValue(kv.Key, out var d) ? d.Name : kv.Key.ToString(), kv => kv.Value),
        births = speciesBirths.ToDictionary(kv => SpeciesCatalog.Default.TryGetValue(kv.Key, out var d) ? d.Name : kv.Key.ToString(), kv => kv.Value),
        deaths = speciesDeaths.ToDictionary(kv => (SpeciesCatalog.Default.TryGetValue(kv.Key.Species, out var d) ? d.Name : kv.Key.Species.ToString()) + ":" + kv.Key.Cause, kv => kv.Value),
        starvationPct = deaths == 0 ? 0 : 100.0 * starved / deaths,
        maxSharePct = alive == 0 ? 0 : 100.0 * last.Alive.Values.Max() / alive,
        trades = report.Days.Sum(d => d.Events.GetValueOrDefault(EventKinds.TradeSold)),
        food = last.Towns.ToDictionary(t => t.Name, t => t.Food),
    }));
}
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
return 0;

internal sealed record Settings(int Days, ulong Seed, int Size, int Animals, int Settlers, bool Body, string Output, string? Species = null)
{
    public static Settings Parse(string[] args)
    {
        int days = 30, size = 128, animals = 120, settlers = 10;
        ulong seed = 1;
        bool body = true;
        string? output = null, species = null;
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--no-body") { body = false; continue; }
            if (++i == args.Length) throw new ArgumentException($"Missing value for {key}.");
            string value = args[i];
            int Number(int min, int max) => int.TryParse(value, out var n) && n >= min && n <= max
                ? n : throw new ArgumentException($"{key} must be between {min} and {max}.");
            switch (key)
            {
                case "--days": days = Number(1, int.MaxValue - 1); break;
                case "--size": size = Number(2, 4096); break;
                case "--animals": animals = Number(0, 1024); break;
                case "--settlers": settlers = Number(0, 511); break;
                case "--seed" when ulong.TryParse(value, out seed): break;
                case "--out": output = value; break;
                case "--species": species = value; break; // RB: try a species table (JSON) without rebuilding
                default: throw new ArgumentException($"Unknown option or invalid value: {key} {value}");
            }
        }
        if (animals > 0 && animals + (settlers > 0 ? 2 + 2 * settlers : 0) > 1024)
            throw new ArgumentException("Opening commands exceed the game queue (1024).");
        output ??= $"docs/reports/world-{seed}-{days}d.md";
        if (!string.Equals(Path.GetExtension(output), ".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--out must name a Markdown (.md) file.");
        return new(days, seed, size, animals, settlers, body, output, species);
    }
}

internal sealed class Day(int number)
{
    public int Number => number;
    public Dictionary<int, int> Alive = [];
    public SettlementView[] Towns = [];
    public Dictionary<ActionKind, long> Actions = [];
    public Dictionary<DeathCause, long> Deaths = [];
    public Dictionary<int, long> Events = [];
    public Dictionary<(int Species, DeathCause Cause), long> SpeciesDeaths = [];
    public Dictionary<int, long> SpeciesBirths = [];
    public long Births, Dropped, Rejected;
    public double Mean, P99;

    public static Dictionary<int, int> Population(WorldSnapshot snapshot) => snapshot.Chunks.SelectMany(chunk => chunk)
        .Where(e => e.LifeState == LifeStates.Alive).GroupBy(e => e.VisualKey).ToDictionary(g => g.Key, g => g.Count());

    public void Record(WorldEvent e, Func<long, int> speciesOf)
    {
        switch (e.Kind)
        {
            case EventKinds.EntityBorn: Births++; Add(SpeciesBirths, speciesOf(e.Target)); break;
            case EventKinds.EntityDied: Add(Deaths, (DeathCause)e.Reason); Add(SpeciesDeaths, (speciesOf(e.Target), (DeathCause)e.Reason)); break;
            case EventKinds.ActionStarted: Add(Actions, (ActionKind)e.Reason); break;
            case EventKinds.EventsDropped: Dropped += e.Amount; break;
            case EventKinds.CommandRejected: Rejected++; break;
            case EventKinds.AnimalTamed: case EventKinds.SpellCast: case EventKinds.TradeSold:
            case EventKinds.GuardAlert: case EventKinds.WokeUp: Add(Events, e.Kind); break;
        }
    }

    private static void Add<T>(Dictionary<T, long> counts, T key) where T : notnull =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}
