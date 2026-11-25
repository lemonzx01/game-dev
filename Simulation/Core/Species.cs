using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyWorld.Simulation;

public enum Diet { Herbivore = 1, Carnivore = 2, Omnivore = 3 }

/// <summary>
/// Data-driven species definition (Data/species.json). Fears: species this one flees. Hunts: species it attacks
/// when hungry and no carcass is reachable. MeatYield: meat units its body leaves as a carcass. Domestic animals
/// wander only within LeashRadius of their spawn (home) tile.
/// </summary>
/// <remarks>Life cycle (T011, ticks): LifespanTicks 0 = no ageing; LitterSize 0 = does not breed. BirthHungerCost is added to the
/// mother's hunger per child. LocalDensityCap: no conception when more same-species animals are within perception.</remarks>
public sealed record SpeciesDef(int Key, string Name, Diet Diet, bool Domestic, int MoveTicksPerTile, int MeatYield,
    long AttackDamage, int LeashRadius, ImmutableArray<int> Fears, ImmutableArray<int> Hunts,
    long MaturityTicks = 0, long LifespanTicks = 0, int GestationTicks = 0, int ReproCooldownTicks = 0,
    int LitterSize = 0, int LocalDensityCap = 0, long BirthHungerCost = 0, Faction Faction = Faction.None,
    int DamageTakenPercent = 100, bool Retaliates = false, int TameTrust = 0,
    int HungerPercent = 100, int ThirstPercent = 100, SleepPattern Sleep = SleepPattern.Diurnal);

/// <summary>Carcass decay and hunting tuning (ticks). Defaults are provisional.</summary>
public sealed record EcologyConfig(int CarcassDecayTicks, int AttackIntervalTicks, int HuntGiveUpTicks, int HuntCooldownTicks,
    int MaxEntities = 100_000, int LifespanJitterPercent = 10, long ReproMaxNeed = 300_000, long ReproMinHealth = 800_000, int MateGiveUpTicks = 40)
{
    public static EcologyConfig Default { get; } = new(1200, 4, 80, 60);

    internal void Validate()
    {
        foreach (int v in new[] { CarcassDecayTicks, AttackIntervalTicks, HuntGiveUpTicks, HuntCooldownTicks, MaxEntities, MateGiveUpTicks })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(LifespanJitterPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(LifespanJitterPercent, 50);
        ArgumentOutOfRangeException.ThrowIfNegative(ReproMaxNeed);
        ArgumentOutOfRangeException.ThrowIfNegative(ReproMinHealth);
    }
}

public static class SpeciesCatalog
{
    public const int Deer = 1, Sheep = 2, Rabbit = 3, Wolf = 4, Human = 5, Demon = 6, Dragon = 7;

    /// <summary>Starter roster: wild herbivores (deer, rabbit), a farm animal (sheep) and a predator (wolf).</summary>
    private const long Day = 2400; // ticks at the default calendar

    /// <summary>Life-cycle numbers are provisional balance values (GDD open decision: reproduction rates).</summary>
    public static ImmutableSortedDictionary<int, SpeciesDef> Default { get; } = Build(
    [
        new SpeciesDef(Deer, "Deer", Diet.Herbivore, false, 1, 8, 0, 0, [Wolf, Dragon], [], 6 * Day, 60 * Day, (int)(3 * Day), (int)(6 * Day), 1, 6, 200_000, TameTrust: 60),
        new SpeciesDef(Sheep, "Sheep", Diet.Herbivore, true, 2, 6, 0, 10, [Wolf, Dragon], [], 6 * Day, 60 * Day, (int)(3 * Day), (int)(6 * Day), 1, 8, 200_000),
        new SpeciesDef(Rabbit, "Rabbit", Diet.Herbivore, false, 1, 2, 0, 0, [Wolf, Dragon], [], 4 * Day, 50 * Day, (int)(1.5 * Day), (int)(7 * Day), 1, 3, 100_000, TameTrust: 40,
            HungerPercent: 120, ThirstPercent: 110), // RB/RP: lives past one breeding season; small, spaced litters, locally capped
        new SpeciesDef(Wolf, "Wolf", Diet.Carnivore, false, 1, 5, 400_000, 0, [Dragon], [Deer, Sheep, Rabbit], 8 * Day, 70 * Day, (int)(3 * Day), (int)(10 * Day), 2, 2, 250_000, TameTrust: 100,
            HungerPercent: 50, ThirstPercent: 90, Sleep: SleepPattern.Nocturnal), // RP: big meals that last, hunts at night, sparse packs
        // Sapient peoples (T015): identical numbers on purpose; neither is good or evil by rule.
        new SpeciesDef(Human, "Human", Diet.Omnivore, false, 2, 10, 0, 0, [Wolf, Dragon], [], 16 * Day, 200 * Day, (int)(6 * Day), (int)(12 * Day), 1, 30, 250_000, Faction.Human),
        new SpeciesDef(Demon, "Demon", Diet.Omnivore, false, 2, 10, 0, 0, [Wolf, Dragon], [], 16 * Day, 200 * Day, (int)(6 * Day), (int)(12 * Day), 1, 30, 250_000, Faction.Demon),
        // A real dragon (T019): rare, long-lived, armoured, takes large game, fights back when hurt; does not breed yet.
        new SpeciesDef(Dragon, "Dragon", Diet.Carnivore, false, 1, 60, 700_000, 0, [], [Deer, Sheep, Wolf], 40 * Day, 2000 * Day,
            DamageTakenPercent: 25, Retaliates: true, TameTrust: 300, HungerPercent: 30, ThirstPercent: 40),
    ]);

    public static ImmutableSortedDictionary<int, SpeciesDef> Build(IEnumerable<SpeciesDef> defs)
    {
        var table = ImmutableSortedDictionary.CreateBuilder<int, SpeciesDef>();
        foreach (var d in defs)
        {
            if (table.ContainsKey(d.Key)) throw new ArgumentException($"Duplicate species key {d.Key}.");
            table.Add(d.Key, d);
        }
        var result = table.ToImmutable();
        Validate(result);
        return result;
    }

    internal static void Validate(ImmutableSortedDictionary<int, SpeciesDef> table)
    {
        foreach (var (key, d) in table)
        {
            if (key != d.Key || key < 1) throw new ArgumentException($"Species key {key} invalid.");
            if (string.IsNullOrWhiteSpace(d.Name)) throw new ArgumentException($"Species {key} needs a name.");
            if (!Enum.IsDefined(d.Diet)) throw new ArgumentException($"Species {key} diet invalid.");
            if (!Enum.IsDefined(d.Faction)) throw new ArgumentException($"Species {key} faction invalid.");
            ArgumentOutOfRangeException.ThrowIfLessThan(d.MoveTicksPerTile, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(d.MeatYield);
            ArgumentOutOfRangeException.ThrowIfNegative(d.AttackDamage);
            if (d.Domestic && d.LeashRadius < 1) throw new ArgumentException($"Domestic species {key} needs LeashRadius ≥ 1.");
            foreach (int other in d.Fears.Concat(d.Hunts))
                if (!table.ContainsKey(other)) throw new ArgumentException($"Species {key} references unknown species {other}.");
            ArgumentOutOfRangeException.ThrowIfNegative(d.MaturityTicks);
            ArgumentOutOfRangeException.ThrowIfNegative(d.LifespanTicks);
            ArgumentOutOfRangeException.ThrowIfNegative(d.LitterSize);
            ArgumentOutOfRangeException.ThrowIfNegative(d.ReproCooldownTicks);
            ArgumentOutOfRangeException.ThrowIfNegative(d.BirthHungerCost);
            ArgumentOutOfRangeException.ThrowIfLessThan(d.DamageTakenPercent, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(d.DamageTakenPercent, 1000);
            ArgumentOutOfRangeException.ThrowIfNegative(d.TameTrust);
            foreach (int pct in new[] { d.HungerPercent, d.ThirstPercent })
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(pct, 10);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(pct, 1000);
            }
            if (!Enum.IsDefined(d.Sleep)) throw new ArgumentException($"Species {key} sleep pattern invalid.");
            if (d.TameTrust > 0 && d.Faction != Faction.None) throw new ArgumentException($"People ({key}) cannot be tamed.");
            if (d.LifespanTicks > 0 && d.LifespanTicks <= d.MaturityTicks) throw new ArgumentException($"Species {key} must live past maturity.");
            if (d.LitterSize > 0 && (d.LifespanTicks == 0 || d.GestationTicks < 1 || d.LocalDensityCap < 1))
                throw new ArgumentException($"Breeding species {key} needs a lifespan, gestation ≥ 1 and a density cap ≥ 1.");
            if (d.Hunts.Length > 0 && (d.Diet == Diet.Herbivore || d.AttackDamage <= 0))
                throw new ArgumentException($"Species {key} hunts but cannot eat meat or attack.");
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads and validates a species file (array of SpeciesDef). Done at construction time, never inside Step.</summary>
    public static ImmutableSortedDictionary<int, SpeciesDef> FromJson(string json) =>
        Build(JsonSerializer.Deserialize<SpeciesDef[]>(json, Json) ?? throw new ArgumentException("Empty species file."));

    internal static void WriteCanonical(BinaryWriter w, ImmutableSortedDictionary<int, SpeciesDef> table)
    {
        w.Write((uint)table.Count);
        foreach (var (_, d) in table)
        {
            w.Write(d.Key);
            byte[] name = System.Text.Encoding.UTF8.GetBytes(d.Name);
            w.Write((uint)name.Length);
            w.Write(name);
            w.Write((int)d.Diet);
            w.Write(d.Domestic);
            w.Write(d.MoveTicksPerTile);
            w.Write(d.MeatYield);
            w.Write(d.AttackDamage);
            w.Write(d.LeashRadius);
            w.Write(d.MaturityTicks);
            w.Write(d.LifespanTicks);
            w.Write(d.GestationTicks);
            w.Write(d.ReproCooldownTicks);
            w.Write(d.LitterSize);
            w.Write(d.LocalDensityCap);
            w.Write(d.BirthHungerCost);
            w.Write((int)d.Faction);
            w.Write(d.DamageTakenPercent);
            w.Write(d.Retaliates);
            w.Write(d.TameTrust);
            w.Write(d.HungerPercent);
            w.Write(d.ThirstPercent);
            w.Write((int)d.Sleep);
            foreach (var list in new[] { d.Fears, d.Hunts })
            {
                var sorted = list.Sort();
                w.Write((uint)sorted.Length);
                foreach (int k in sorted) w.Write(k);
            }
        }
    }
}
