using System.Text;
using FantasyWorld.Simulation;

internal sealed class Report(Settings settings)
{
    public List<Day> Days { get; } = [];
    public Dictionary<int, int> Opening = [];
    private sealed record Column(string Name, Func<Day, string> Value);
    private static Column Count(string name, Func<Day, long> get) => new(name, d => get(d).ToString());

    public void Write()
    {
        var md = new StringBuilder();
        md.AppendLine($"# World report — seed {settings.Seed}, {settings.Days} days").AppendLine();
        md.AppendLine($"Map {settings.Size} × {settings.Size}; requested animals {settings.Animals}, settlers per town {settings.Settlers}; Body {(settings.Body ? "on" : "off")}.").AppendLine();
        md.AppendLine("| Opening alive | Final alive | Births | Deaths | Action starts |")
            .AppendLine("|---:|---:|---:|---:|---:|")
            .AppendLine($"| {Opening.Values.Sum()} | {Days[^1].Alive.Values.Sum()} | {Days.Sum(d => d.Births)} | {Days.Sum(d => d.Deaths.Values.Sum())} | {Days.Sum(d => d.Actions.Values.Sum())} |");
        var columns = new List<Column> { Count("day", d => d.Number) };
        void Table(string title, IEnumerable<Column> fields)
        {
            var group = fields.ToArray();
            if (group.Length == 0) return;
            md.AppendLine().AppendLine($"## {title}").AppendLine();
            md.AppendLine("| day | " + string.Join(" | ", group.Select(c => c.Name)) + " |");
            md.AppendLine("|---:" + string.Concat(group.Select(_ => "|---:")) + "|");
            foreach (var d in Days)
                md.AppendLine($"| {d.Number} | " + string.Join(" | ", group.Select(c => c.Value(d))) + " |");
            columns.AddRange(group);
        }
        Table("Living population", SpeciesCatalog.Default.Values.Select(s => Count($"alive_{s.Name}", d => d.Alive.GetValueOrDefault(s.Key))));
        Table("Births and causes of death", new[] { Count("births", d => d.Births) }.Concat(
            Enum.GetValues<DeathCause>().Where(c => c != DeathCause.None).Select(c => Count($"deaths_{c}", d => d.Deaths.GetValueOrDefault(c)))));
        var towns = Days.SelectMany(d => d.Towns).DistinctBy(t => t.Id).OrderBy(t => t.Id).ToArray();
        foreach (var town in towns)
        {
            Column TownField(string name, Func<SettlementView, long> get) =>
                new($"{town.Name}_{name}", d => d.Towns.FirstOrDefault(t => t.Id == town.Id) is { } t ? get(t).ToString() : "");
            Table(town.Name, [TownField("food", t => t.Food), TownField("wood", t => t.Wood), TownField("coins", t => t.Coins),
                TownField("population", t => t.Population), TownField("guards", t => t.Guards)]);
        }
        Table("Action starts", Enum.GetValues<ActionKind>().Select(k => Count($"starts_{k}", d => d.Actions.GetValueOrDefault(k))));
        var events = new (int Kind, string Name)[] { (EventKinds.AnimalTamed,"AnimalTamed"), (EventKinds.SpellCast,"SpellCast"),
            (EventKinds.TradeSold,"TradeSold"), (EventKinds.GuardAlert,"GuardAlert"), (EventKinds.WokeUp,"WokeUp") };
        Table("Important events and telemetry", events.Select(e => Count(e.Name, d => d.Events.GetValueOrDefault(e.Kind)))
            .Concat([Count("events_dropped", d => d.Dropped), Count("commands_rejected", d => d.Rejected)]));
        md.AppendLine().AppendLine("## Warnings").AppendLine();
        var warnings = Warnings(towns).ToArray();
        foreach (var warning in warnings.Length > 0 ? warnings : ["No thresholds crossed."]) md.AppendLine("- " + warning);
        Table("Timing (milliseconds per simulation step)", [new("mean_ms", d => d.Mean.ToString("F4")), new("p99_ms", d => d.P99.ToString("F4"))]);

        string path = Path.GetFullPath(settings.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, md.ToString());
        using var csv = File.CreateText(Path.ChangeExtension(path, ".csv"));
        csv.WriteLine(string.Join(",", columns.Select(c => c.Name)));
        foreach (var d in Days) csv.WriteLine(string.Join(",", columns.Select(c => c.Value(d))));
    }

    private IEnumerable<string> Warnings(SettlementView[] towns)
    {
        if (settings.Animals > 0 && settings.Settlers > 0 && towns.Length < 2)
            yield return $"Only {towns.Length} of two requested towns founded.";
        if (settings.Animals > 0 && Opening.Values.Sum() < settings.Animals + 2L * settings.Settlers)
            yield return $"Opening census below requested population ({Opening.Values.Sum()} actually alive).";
        foreach (var species in SpeciesCatalog.Default.Values)
        {
            int previous = Opening.GetValueOrDefault(species.Key);
            foreach (var d in Days)
            {
                int current = d.Alive.GetValueOrDefault(species.Key);
                if (previous > 0 && current == 0) yield return $"{species.Name} extinct at day {d.Number} census.";
                previous = current;
            }
        }
        foreach (var town in towns)
        {
            int streak = 0;
            foreach (var d in Days)
            {
                streak = d.Towns.Any(t => t.Id == town.Id && t.Food == 0) ? streak + 1 : 0;
                if (streak == 3) yield return $"{town.Name} had zero food at three consecutive censuses ending day {d.Number}.";
            }
        }
        long deaths = Days.Sum(d => d.Deaths.Values.Sum());
        long starvation = Days.Sum(d => d.Deaths.GetValueOrDefault(DeathCause.Starvation));
        if (starvation > deaths * 0.25) yield return $"Starvation caused {starvation} of {deaths} deaths (above 25%).";
        int prev = Opening.Values.Sum(), rising = 0;
        foreach (var d in Days)
        {
            int total = d.Alive.Values.Sum();
            rising = total > prev ? rising + 1 : 0;
            if (rising == 5) yield return $"Population grew at five consecutive daily censuses ending day {d.Number}.";
            prev = total;
        }
        if (Days.Any(d => d.Dropped > 0)) yield return "Simulation event cap reached; noncritical event counts are lower bounds.";
        if (Days.Any(d => d.Rejected > 0)) yield return "Opening commands were rejected; see the actual census.";
    }
}
