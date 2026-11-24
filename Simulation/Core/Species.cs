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
public sealed record SpeciesDef(int Key, string Name, Diet Diet, bool Domestic, int MoveTicksPerTile, int MeatYield,
    long AttackDamage, int LeashRadius, ImmutableArray<int> Fears, ImmutableArray<int> Hunts);

/// <summary>Carcass decay and hunting tuning (ticks). Defaults are provisional.</summary>
public sealed record EcologyConfig(int CarcassDecayTicks, int AttackIntervalTicks, int HuntGiveUpTicks, int HuntCooldownTicks)
{
    public static EcologyConfig Default { get; } = new(1200, 4, 80, 60);

    internal void Validate()
    {
        foreach (int v in new[] { CarcassDecayTicks, AttackIntervalTicks, HuntGiveUpTicks, HuntCooldownTicks })
            ArgumentOutOfRangeException.ThrowIfLessThan(v, 1);
    }
}

public static class SpeciesCatalog
{
    public const int Deer = 1, Sheep = 2, Rabbit = 3, Wolf = 4;

    /// <summary>Starter roster: wild herbivores (deer, rabbit), a farm animal (sheep) and a predator (wolf).</summary>
    public static ImmutableSortedDictionary<int, SpeciesDef> Default { get; } = Build(
    [
        new SpeciesDef(Deer, "Deer", Diet.Herbivore, false, 2, 8, 0, 0, [Wolf], []),
        new SpeciesDef(Sheep, "Sheep", Diet.Herbivore, true, 2, 6, 0, 6, [Wolf], []),
        new SpeciesDef(Rabbit, "Rabbit", Diet.Herbivore, false, 1, 2, 0, 0, [Wolf], []),
        new SpeciesDef(Wolf, "Wolf", Diet.Carnivore, false, 1, 5, 400_000, 0, [], [Deer, Sheep, Rabbit]),
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
            ArgumentOutOfRangeException.ThrowIfLessThan(d.MoveTicksPerTile, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(d.MeatYield);
            ArgumentOutOfRangeException.ThrowIfNegative(d.AttackDamage);
            if (d.Domestic && d.LeashRadius < 1) throw new ArgumentException($"Domestic species {key} needs LeashRadius ≥ 1.");
            foreach (int other in d.Fears.Concat(d.Hunts))
                if (!table.ContainsKey(other)) throw new ArgumentException($"Species {key} references unknown species {other}.");
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
            foreach (var list in new[] { d.Fears, d.Hunts })
            {
                var sorted = list.Sort();
                w.Write((uint)sorted.Length);
                foreach (int k in sorted) w.Write(k);
            }
        }
    }
}
