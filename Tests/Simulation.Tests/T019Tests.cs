using FantasyWorld.Simulation;
using System.Collections.Immutable;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>T019 acceptance: combat, magic and taming have costs and valid targets; one dragon species; taming builds trust.</summary>
internal static class T019Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("T019 combat damage follows condition and costs fatigue", WoundedFightWeaker),
        ("T019 brave villagers with friends fight back together", FightBack),
        ("T019 dragon: armoured, retaliates, feared", Dragon),
        ("T019 magic: Firebolt and Heal cost mana and need valid targets", Magic),
        ("T019 taming: food cost, trust, refusal, durable bond", Taming),
        ("T019 tamed beasts are not threats and spare livestock", TamedWolf),
        ("T019 save mid-fight and mid-taming continues identically", SaveMidAction),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    // Open field 40×14 inside mountains; berry bushes along the top, trees along the bottom. Needs off unless asked.
    private static SimulationWorld World(SettlementConfig? town = null, VitalsConfig? vitals = null)
    {
        var rows = Enumerable.Range(0, 14).Select(y => y is 0 or 13 ? new string('^', 40) : "^" + new string('.', 38) + "^").ToList();
        var resources = new List<ResourcePlacement>();
        for (int x = 3; x < 37; x += 6) { resources.Add(new ResourcePlacement(x, 1, ResourceKind.Food, 20)); resources.Add(new ResourcePlacement(x, 12, ResourceKind.Wood, 50)); }
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W, Seed = 19, MapWidth = 40, MapHeight = 14, Map = MapOverride.FromRows(rows, [.. resources]), Species = Default,
            Vitals = vitals ?? new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = town ?? SettlementConfig.Default with { FarmPlots = 0 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    private static List<WorldEvent> Run(SimulationWorld world, int ticks, ref long cursor)
    {
        var all = new List<WorldEvent>();
        for (int i = 0; i < ticks; i++)
        {
            world.Step();
            while (true)
            {
                var page = world.ReadEvents(cursor, 256);
                Check(page.Status == PageStatus.Ok, "gap in test");
                all.AddRange(page.Records);
                if (page.NextCursor == cursor) break;
                cursor = page.NextCursor;
            }
        }
        return all;
    }

    private static List<long> Live(SimulationWorld w, int species) =>
        w.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == species && r.LifeState == LifeStates.Alive).Select(r => r.Id.Value).ToList();

    private static long Spawn(SimulationWorld world, int x, int y, int species, ref long cursor)
    {
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species));
        return Run(world, 1, ref cursor).First(e => e.Kind == EventKinds.CommandApplied).Target;
    }

    // A town at (20,6) with `members` people (one becomes guard after the first thinks); storage well stocked.
    private static (SimulationWorld World, long Cursor, long Guard, List<long> Others) Town(int members, int faction = Human, long food = 300, VitalsConfig? vitals = null, SettlementConfig? town = null)
    {
        var world = World(town, vitals);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 6, faction == Human ? Faction.Human : Faction.Demon, "Keep"));
        for (int k = 0; k < members; k++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 18 + k % 5, 6 + k / 5, faction));
        world.Step();
        world.SetStorageForTest(1, food, 300, 100);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 30, ref cursor);
        var people = Live(world, faction);
        long guard = people.Single(id => world.ReadEntityDetails(W, new EntityId(id))!.Job == Job.Guard);
        return (world, cursor, guard, people.Where(id => id != guard).ToList());
    }

    private static void WoundedFightWeaker()
    {
        long FirstHit(long guardHealth, out long fatigueGain)
        {
            var (world, cursor, guard, _) = Town(3);
            world.SetHealthForTest(guard, guardHealth);
            long fatigueBefore = world.FatigueOf(guard);
            long wolf = Spawn(world, 23, 6, Wolf, ref cursor);
            var hit = Run(world, 300, ref cursor).First(e => e.Kind == EventKinds.Attacked && e.Actor == guard && e.Target == wolf);
            fatigueGain = world.FatigueOf(guard) - fatigueBefore;
            return hit.Amount;
        }
        long full = FirstHit(1_000_000, out long gained), half = FirstHit(500_000, out _);
        long guardDamage = TradeConfig.Default.GuardDamage;
        Check(full == guardDamage, $"healthy guard hits for GuardDamage ({full})");
        Check(half == guardDamage * 75 / 100, $"half-health guard hits for 75% ({half})");
        Check(gained >= CombatConfig.Default.FatiguePerAttack, $"attacks cost fatigue ({gained})");
    }

    private static void FightBack()
    {
        var (world, cursor, guard, others) = Town(9);
        foreach (long id in others) world.SetBraveryForTest(id, 1000);
        var (x, y) = world.PositionOf(others[0]);
        long wolf = Spawn(world, Math.Min(38, x + 1), y, Wolf, ref cursor);
        var events = Run(world, 300, ref cursor);
        var fighters = events.Where(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Defend && e.Target == wolf && others.Contains(e.Actor))
            .Select(e => e.Actor).Distinct().ToList();
        Check(fighters.Count >= 2, $"brave villagers fought back ({fighters.Count})");
        Check(events.Where(e => e.Kind == EventKinds.GuardAlert).All(e => e.Actor == guard), "only the guard raises the guard alert");
        var hits = events.Where(e => e.Kind == EventKinds.Attacked && e.Target == wolf && others.Contains(e.Actor)).ToList();
        Check(hits.Any(h => h.Amount > CombatConfig.Default.UnarmedDamage), "fighting together hits harder than alone");
        Check(hits.All(h => h.Amount <= CombatConfig.Default.UnarmedDamage * (100 + 15 * 3) / 100), "ally bonus is capped");
        var death = events.FirstOrDefault(e => e.Kind == EventKinds.EntityDied && e.Target == wolf);
        Check(death.Kind == EventKinds.EntityDied && death.Reason == (int)DeathCause.Combat, "the wolf died in combat");
    }

    private static void Dragon()
    {
        var (world, cursor, guard, others) = Town(9);
        long dragon = Spawn(world, 25, 6, SpeciesCatalog.Dragon, ref cursor);
        var events = Run(world, 400, ref cursor);
        Check(events.Any(e => e.Kind == EventKinds.GuardAlert && e.Actor == guard && e.Target == dragon), "the guard defends against the dragon");
        var onDragon = events.Where(e => e.Kind == EventKinds.Attacked && e.Target == dragon).ToList();
        Check(onDragon.Count > 0 && onDragon.All(h => h.Amount <= TradeConfig.Default.GuardDamage * 145 / 100 / 4), "dragon hide takes a quarter of the damage");
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == dragon && e.Reason == (int)ActionKind.Defend && e.Target == onDragon[0].Actor),
            "the dragon turns on whoever hurt it");
        Check(events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == dragon && e.Target == onDragon[0].Actor), "and strikes back");
        Check(events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Flee && others.Contains(e.Actor)), "people fear the dragon");
        Check(!events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == dragon && e.Target != onDragon[0].Actor && others.Contains(e.Target)
            && !events.Any(h => h.Kind == EventKinds.Attacked && h.Actor == e.Target && h.Target == dragon)), "the dragon attacks only those who attacked it");
    }

    private static void Magic()
    {
        // Firebolt: a scholar guard with 50 mana casts exactly once at a distant wolf (40 mana), then must close in.
        var (world, cursor, guard, others) = Town(3);
        world.SetSkillForTest(guard, Skill.Scholarship, 10_000);
        world.SetManaForTest(guard, 50);
        var (gx, gy) = world.PositionOf(guard);
        long wolf = Spawn(world, Math.Min(38, gx + 4), gy, Wolf, ref cursor);
        var events = Run(world, 200, ref cursor);
        var bolts = events.Where(e => e.Kind == EventKinds.SpellCast && e.Reason == (int)Spell.Firebolt).ToList();
        Check(bolts.Count == 1 && bolts[0].Actor == guard && bolts[0].Target == wolf && bolts[0].Amount == MagicConfig.Default.FireboltCost, $"one Firebolt for 50 mana ({bolts.Count})");
        var bolt = bolts[0];
        Check(events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == guard && e.Target == wolf && e.Tick == bolt.Tick), "the Firebolt hit from range");
        Check(!events.Any(e => e.Kind == EventKinds.SpellCast && e.Actor != guard), "people who never studied cannot cast");

        // Heal: a scholar mends the most wounded townmate below the threshold; the healthy are never targets.
        var (w2, c2, g2, o2) = Town(4);
        long healer = o2[0], hurt = o2[1], scratched = o2[2];
        w2.SetSkillForTest(healer, Skill.Scholarship, 10_000);
        w2.SetHealthForTest(hurt, 300_000);
        w2.SetHealthForTest(scratched, 800_000); // above HealBelowHealth: not a valid target
        long manaBefore = w2.ReadAdventure(W, new EntityId(healer))!.Mana;
        var heals = Run(w2, 120, ref c2).Where(e => e.Kind == EventKinds.SpellCast && e.Reason == (int)Spell.Heal).ToList();
        Check(heals.Count >= 1 && heals.All(h => h.Actor == healer), "only the scholar heals");
        Check(heals[0].Target == hurt && heals[0].Amount == MagicConfig.Default.HealAmount, "the badly wounded townmate is healed first");
        Check(heals.All(h => h.Target != scratched), "a lightly scratched townmate is not healed");
        var after = w2.ReadAdventure(W, new EntityId(healer))!;
        Check(after.Mana <= manaBefore - heals.Count * MagicConfig.Default.HealCost + 120 / MagicConfig.Default.ManaRegenTicks, "each heal spent mana");
        Check(w2.ReadEntityDetails(W, new EntityId(hurt))!.Health >= 300_000 + MagicConfig.Default.HealAmount, "health restored");
    }

    private static void Taming()
    {
        var (world, cursor, guard, others) = Town(2, food: 300);
        long deer = Spawn(world, 25, 9, Deer, ref cursor);
        long foodBefore = world.ReadSettlements()[0].Food;
        var events = Run(world, 6000, ref cursor);
        var feeds = events.Where(e => e.Kind == EventKinds.TameFed && e.Target == deer).ToList();
        var tamed = events.Where(e => e.Kind == EventKinds.AnimalTamed && e.Target == deer).ToList();
        Check(feeds.Count >= 3 && tamed.Count == 1, $"fed {feeds.Count} times, tamed {tamed.Count}");
        int trust = 0;
        foreach (var f in feeds)
        {
            trust = f.Reason == 1 ? Math.Min(60, trust + TamingConfig.Default.TrustPerFeed) : Math.Max(0, trust - TamingConfig.Default.TrustLostOnRefusal);
            Check(f.Amount == trust, $"trust follows accepted (+20) and refused (−10) feeds ({f.Amount} vs {trust})");
        }
        Check(trust >= Default[Deer].TameTrust && tamed[0].Tick == feeds[^1].Tick && tamed[0].Reason == 1, "tamed by the town on reaching TameTrust");
        long eaten = events.Where(e => e.Kind == EventKinds.EntityAte && e.Target == 1).Sum(e => e.Amount);
        long deposited = events.Where(e => e.Kind == EventKinds.Deposited && e.Target == 1 && e.Reason == (int)ResourceKind.Food).Sum(e => e.Amount);
        Check(world.ReadSettlements()[0].Food == foodBefore - feeds.Count - eaten + deposited, "each feed cost one unit of town food");
        Check(world.ReadAdventure(W, new EntityId(deer))!.TamedTo == 1, "the deer belongs to the town");
        Check(world.ReadHistory(0, 256).Records.Any(m => m.Kind == EventKinds.AnimalTamed && m.Target == deer), "the bond is a durable milestone");
        long tamer = tamed[0].Actor;
        var after = Run(world, 1500, ref cursor);
        Check(!after.Any(e => e.Kind == EventKinds.TameFed && e.Target == deer), "a tamed animal is not fed for taming again");
        var (dx, dy) = world.PositionOf(deer);
        Check(Math.Max(Math.Abs(dx - 20), Math.Abs(dy - 6)) <= SettlementConfig.Default.Radius, "the tamed deer stays near town");

        // No food to spare (at the reserve): nobody tries.
        var (w2, c2, _, _) = Town(2, food: TamingConfig.Default.FoodReserve, town: SettlementConfig.Default with { FarmPlots = 0, FoodTarget = 0 });
        Spawn(w2, 25, 9, Deer, ref c2);
        var none = Run(w2, 1500, ref c2);
        Check(!none.Any(e => e.Kind == EventKinds.TameFed || (e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Tame)), "no taming without spare food");
        _ = tamer;
    }

    private static void TamedWolf()
    {
        var (world, cursor, guard, others) = Town(4, vitals: new VitalsConfig(1500, 0, 0, 0, 0, 250_000, 100_000, 4)); // hungry wolf
        long wolf = Spawn(world, 25, 6, Wolf, ref cursor);
        world.SetTamedForTest(wolf, 1);
        long sheep = Spawn(world, 24, 8, Sheep, ref cursor);
        long rabbit = Spawn(world, 30, 3, Rabbit, ref cursor);
        long deer = Spawn(world, 27, 9, Deer, ref cursor);
        var events = Run(world, 1200, ref cursor);
        Check(!events.Any(e => e.Kind == EventKinds.GuardAlert && e.Target == wolf), "the guard does not treat the town's wolf as a threat");
        Check(!events.Any(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Flee && e.Target == wolf && others.Append(guard).Contains(e.Actor)), "its people do not flee the town's wolf");
        Check(!events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == sheep), "the tamed wolf spares livestock");
        Check(!events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && others.Append(guard).Contains(e.Target)), "and never attacks its people");
        Check(events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && (e.Target == rabbit || e.Target == deer)), "it still hunts wild game when hungry");
    }

    private static void SaveMidAction()
    {
        var (world, cursor, _, others) = Town(9);
        foreach (long id in others) world.SetBraveryForTest(id, 1000);
        world.SetSkillForTest(others[0], Skill.Scholarship, 10_000);
        Spawn(world, 25, 9, Deer, ref cursor);
        Spawn(world, 23, 6, SpeciesCatalog.Dragon, ref cursor);
        Run(world, 20, ref cursor);
        using var ms = new MemoryStream();
        world.Save(ms);
        var loaded = SimulationWorld.Load(new MemoryStream(ms.ToArray()));
        for (int t = 0; t < 600; t++) { world.Step(); loaded.Step(); }
        Check(loaded.StateHash() == world.StateHash(), "fight and taming continue identically after load");
    }
}
