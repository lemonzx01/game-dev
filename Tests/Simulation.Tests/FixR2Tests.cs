using FantasyWorld.Simulation;
using System.Collections.Immutable;
using System.Numerics;
using static FantasyWorld.Simulation.SpeciesCatalog;

/// <summary>Regression tests for FIX-R2 (Codex T019 review findings 1–9, 14, 15).</summary>
internal static class FixR2Tests
{
    private const long God = 7;
    private static readonly WorldId W = new(1);

    public static (string, Action)[] Cases =>
    [
        ("FIX-R2 #1 hunt rechecks eligibility before movement, damage, fatigue or XP", HuntEligibilityRecheckedAtExecution),
        ("FIX-R2 #2 load rejects absent town, future mana, excessive trust and unallocated tamer, permits dead tamer", LoadRejectsInvalidAdventureAndAcceptsDeceasedTamer),
        ("FIX-R2 #3 two simultaneous feeders preserve second town food, trust, tamer and RNG", TwoSimultaneousFeedersPreserveSecondTownState),
        ("FIX-R2 #4 abandoned refused taming attempt expires while completed bond survives", AbandonedRefusedAttemptExpiresWhileBondSurvives),
        ("FIX-R2 #5 heal charges exact mana and fatigue and leaves ineligible casts unchanged", HealChargesExactFatigueAndSkipsIneligible),
        ("FIX-R2 #6 damage multipliers use wide arithmetic without wrapping and stay monotonic", DamageMultipliersNoOverflowAndMonotonic),
        ("FIX-R2 #7 near-limit mana and trust saturate safely without negative wrap across save/load", ManaAndTrustNearLimitSaturateSafely),
        ("FIX-R2 #8 tamed animals stay within town radius on chase/flee/eat/wander and return home when outside", TamedAnimalStaysLeashedOnEveryPathAndReturnsHome),
        ("FIX-R2 #9 dense perception queries obey candidate budget, short-circuit, cache per tick, and keep ID tie-breaks", DensePerceptionScansObeyBudgetAndTieBreaks),
    ];

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
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

    private static long Spawn(SimulationWorld world, int x, int y, int species, ref long cursor, Sex sex = Sex.Unspecified)
    {
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species, sex));
        return Run(world, 1, ref cursor).First(e => e.Kind == EventKinds.CommandApplied).Target;
    }

    private static byte[] SaveBytes(SimulationWorld world)
    {
        using var ms = new MemoryStream();
        world.Save(ms);
        return ms.ToArray();
    }

    private static void RequireSaveRejected(byte[] bytes, string label)
    {
        try
        {
            SimulationWorld.Load(new MemoryStream(bytes));
        }
        catch (SaveFormatException)
        {
            return;
        }
        throw new Exception($"Expected SaveFormatException for {label}, but load succeeded.");
    }

    private static SimulationWorld FlatWorld(int width = 44, int height = 16, ulong seed = 201,
        VitalsConfig? vitals = null, SettlementConfig? settlements = null, TamingConfig? taming = null,
        MindConfig? mind = null, MagicConfig? magic = null, CombatConfig? combat = null,
        ImmutableSortedDictionary<int, SpeciesDef>? species = null)
    {
        var rows = Enumerable.Range(0, height)
            .Select(y => y == 0 || y == height - 1 ? new string('^', width) : "^" + new string('.', width - 2) + "^")
            .ToList();
        return new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = seed,
            MapWidth = width,
            MapHeight = height,
            Map = MapOverride.FromRows(rows),
            Species = species ?? Default,
            Vitals = vitals ?? new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = mind ?? MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = settlements ?? SettlementConfig.Default with { FarmPlots = 0 },
            Taming = taming ?? TamingConfig.Default,
            Magic = magic ?? MagicConfig.Default,
            Combat = combat ?? CombatConfig.Default,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });
    }

    // ---- Finding 1 ------------------------------------------------------------------------------

    private static void HuntEligibilityRecheckedAtExecution()
    {
        // Part A: two real towns. Town 1's wolf starts hunting a wild deer; Town 2 finishes taming
        // the deer mid-pursuit. Wait until Deer is protected; verify wolf aborts before moving again,
        // deals no damage, spends no fatigue, gains no Combat XP, and still hunts a wild rabbit.
        {
            var rows = Enumerable.Range(0, 16).Select(y =>
            {
                if (y is 0 or 15) return new string('^', 48);
                var line = ("^" + new string('.', 46) + "^").ToCharArray();
                // Wolf starting stall at (13,8) with temporary mountain gate at (14,8)
                if (y == 8) line[12] = '^';
                if (y is 7 or 9) line[13] = '^';
                if (y == 8) line[14] = '^';
                // East pocket around (21..22,8) keeps Deer next to East feeder without escaping East
                if (y is 7 or 9) { line[21] = '^'; line[22] = '^'; }
                if (y == 8) line[23] = '^';
                return new string(line);
            }).ToList();
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W,
                Seed = 301,
                MapWidth = 48,
                MapHeight = 16,
                Map = MapOverride.FromRows(rows),
                Species = Default,
                Vitals = new VitalsConfig(2000, 0, 0, 0, 0, 250_000, 100_000, 4),
                Mind = MindConfig.Default with { FatiguePerTick = 0 },
                Settlements = SettlementConfig.Default with { FarmPlots = 0, Radius = 11 },
                Taming = TamingConfig.Default with { BaseAcceptPercent = 100, TrustPerFeed = 60, CooldownTicks = 10 },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });

            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 8, Faction.Human, "West"));
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 32, 8, Faction.Human, "East"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 8, Human));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 8, Human));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 32, 8, Human)); // East guard
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            world.SetStorageForTest(2, food: 300, wood: 100, coins: 50);
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            // Spawn tamed West wolf in stall (13,8) and advance 126 ticks until hungry (Hunger >= 250k).
            long wolf = Spawn(world, 13, 8, Wolf, ref cursor);
            world.SetTamedForTest(wolf, 1);
            Run(world, 126, ref cursor);

            // Spawn wild deer at (21,8) (distance 8 from wolf) and backup wild rabbit at (17,12) (Manhattan 8+4=12 > 8, Chebyshev <= 8).
            long deer = Spawn(world, 21, 8, Deer, ref cursor);
            long rabbit = Spawn(world, 18, 12, Rabbit, ref cursor);

            bool huntStarted = false;
            int huntStartTick = -1;
            for (int t = 0; t < 30; t++)
            {
                var evs = Run(world, 1, ref cursor);
                if (evs.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf && e.Reason == (int)ActionKind.Hunt && e.Target == deer))
                {
                    huntStarted = true;
                    huntStartTick = (int)evs.First(e => e.Actor == wolf && e.Reason == (int)ActionKind.Hunt).Tick;
                    break;
                }
            }
            Check(huntStarted, "tamed wolf did not select wild deer for Hunt");

            // Spawn East feeder at (22,8) (outside wolf's perception 8 while wolf is held at (13,8));
            // once feeder commits ActionKind.Tame, open gate (14,8) so wolf actively pursues as AnimalTamed lands.
            long feeder = Spawn(world, 22, 8, Human, ref cursor);
            Check(world.ReadEntityDetails(W, new EntityId(feeder))!.SettlementId == 2, "feeder must join East town");
            bool tameStarted = false;
            for (int t = 0; t < 30; t++)
            {
                var evs = Run(world, 1, ref cursor);
                if (evs.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == feeder && e.Reason == (int)ActionKind.Tame && e.Target == deer))
                {
                    tameStarted = true;
                    break;
                }
            }
            Check(tameStarted, "East feeder did not start Tame on deer");
            world.SubmitCommand(new SetTerrainCommand(W, God, CommandModes.God, 14, 8, Terrain.Grass));

            var beforeTame = new List<WorldEvent>();
            bool deerTamed = false;
            int tameTick = -1;
            long xpAtTame = 0, fatigueAtTame = 0;
            (int X, int Y) wolfPosAtTame = default;
            for (int t = 0; t < 30; t++)
            {
                var evs = Run(world, 1, ref cursor);
                beforeTame.AddRange(evs);
                if (evs.Any(e => e.Kind == EventKinds.AnimalTamed && e.Target == deer))
                {
                    deerTamed = true;
                    tameTick = (int)evs.First(e => e.Kind == EventKinds.AnimalTamed && e.Target == deer).Tick;
                    wolfPosAtTame = world.PositionOf(wolf);
                    xpAtTame = world.ReadSkills(W, new EntityId(wolf))!.Combat;
                    fatigueAtTame = world.FatigueOf(wolf);
                    break;
                }
            }
            Check(deerTamed && tameTick >= huntStartTick, $"deer was not tamed during active hunt (hunt={huntStartTick}, tame={tameTick})");
            Check(world.ActionOf(wolf) == ActionKind.Hunt, $"wolf must still be actively hunting deer when tamed, was {world.ActionOf(wolf)}");
            Check(!beforeTame.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == deer && e.Tick >= tameTick),
                "wolf attacked deer on taming tick");

            // Track steps immediately after deer is tamed: wolf must abort Hunt(deer) before moving closer to deer,
            // with zero damage/fatigue/XP on deer, and then hunt the wild rabbit.
            var after = new List<WorldEvent>();
            bool abortedBeforeMove = false;
            for (int t = 0; t < 250; t++)
            {
                var evs = Run(world, 1, ref cursor);
                after.AddRange(evs);
                if (!abortedBeforeMove)
                {
                    var wd = world.ReadEntityDetails(W, new EntityId(wolf))!;
                    if (evs.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf))
                    {
                        Check(wd.X == wolfPosAtTame.X && wd.Y == wolfPosAtTame.Y,
                            $"wolf stepped before aborting protected hunt ({wolfPosAtTame.X},{wolfPosAtTame.Y})->({wd.X},{wd.Y})");
                        Check(world.ReadSkills(W, new EntityId(wolf))!.Combat == xpAtTame, "wolf gained Combat XP from protected hunt");
                        Check(world.FatigueOf(wolf) == fatigueAtTame, "wolf gained fatigue from protected hunt");
                        abortedBeforeMove = true;
                    }
                    else
                    {
                        Check(wd.X == wolfPosAtTame.X && wd.Y == wolfPosAtTame.Y,
                            $"wolf continued chasing protected deer ({wolfPosAtTame.X},{wolfPosAtTame.Y})->({wd.X},{wd.Y})");
                    }
                }
            }
            Check(abortedBeforeMove, "wolf never aborted invalidated deer hunt");
            Check(!after.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == deer),
                "tamed wolf bit protected Town-2 deer after mid-hunt taming");
            Check(after.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf && e.Reason == (int)ActionKind.Hunt && e.Target == rabbit)
                  || after.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == rabbit),
                "wolf did not switch to wild alternative rabbit after aborting protected deer hunt");
        }

        // Part B: wild wolf starts hunting sheep, then gets tamed by Town 1 mid-hunt.
        {
            var world = FlatWorld(
                width: 40, height: 14, seed: 302,
                vitals: new VitalsConfig(8000, 0, 0, 0, 0, 250_000, 100_000, 4),
                settlements: SettlementConfig.Default with { FarmPlots = 0, Radius = 10 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 7, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human));
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            // Place wild wolf outside Guard's perception 8 so Guard doesn't kill wolf before it hunts sheep.
            long wolf = Spawn(world, 20, 7, Wolf, ref cursor);
            long sheep = Spawn(world, 24, 7, Sheep, ref cursor);

            bool startedOnSheep = false;
            for (int t = 0; t < 150; t++)
            {
                var evs = Run(world, 1, ref cursor);
                if (evs.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf && e.Reason == (int)ActionKind.Hunt && e.Target == sheep))
                {
                    startedOnSheep = true;
                    break;
                }
            }
            Check(startedOnSheep, "wild wolf did not start hunting sheep");
            long sheepHealthBefore = world.ReadEntityDetails(W, new EntityId(sheep))!.Health;
            long xpBefore = world.ReadSkills(W, new EntityId(wolf))!.Combat;
            long fatigueBefore = world.FatigueOf(wolf);
            var (wx0, wy0) = world.PositionOf(wolf);

            // Spawn backup wild rabbit inside perception as wolf is tamed to Town 1 mid-hunt.
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, wx0, Math.Min(12, wy0 + 3), Rabbit));
            world.SetTamedForTest(wolf, 1);

            // Next few ticks until wolf aborts Hunt(sheep): wolf must NOT move on the Hunt(sheep) step,
            // must NOT damage sheep, gain Combat XP, or add attack fatigue.
            var evsAfterTame = new List<WorldEvent>();
            long wildRabbit = 0;
            bool abortedCleanly = false;
            for (int t = 0; t < 20; t++)
            {
                var stepEvs = Run(world, 1, ref cursor);
                evsAfterTame.AddRange(stepEvs);
                if (wildRabbit == 0 && stepEvs.FirstOrDefault(e => e.Kind == EventKinds.CommandApplied).Target is long rid and > 0)
                    wildRabbit = rid;
                if (stepEvs.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf))
                {
                    // Before any rabbit attack happens, verify no step happened while still hunting sheep
                    abortedCleanly = true;
                    break;
                }
                var (wx1, wy1) = world.PositionOf(wolf);
                Check(wx1 == wx0 && wy1 == wy0, $"tamed wolf still moved on invalidated sheep hunt ({wx0},{wy0})->({wx1},{wy1})");
                Check(world.ReadSkills(W, new EntityId(wolf))!.Combat == xpBefore, "wolf gained Combat XP from invalidated hunt");
                Check(world.FatigueOf(wolf) == fatigueBefore, "wolf gained fatigue from invalidated hunt");
            }
            var afterMore = Run(world, 200, ref cursor);
            evsAfterTame.AddRange(afterMore);
            Check(abortedCleanly && wildRabbit > 0, "tamed wolf did not leave invalidated sheep hunt promptly");
            Check(!evsAfterTame.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == sheep),
                "tamed wolf attacked domestic sheep after mid-hunt taming");
            Check(world.ReadEntityDetails(W, new EntityId(sheep))!.Health == sheepHealthBefore,
                "sheep health dropped after wolf was tamed");
            Check(evsAfterTame.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == wolf && e.Reason == (int)ActionKind.Hunt && e.Target == wildRabbit),
                "tamed wolf did not switch to wild rabbit after aborting sheep hunt");
        }
    }

    // ---- Finding 2 ------------------------------------------------------------------------------

    private static void LoadRejectsInvalidAdventureAndAcceptsDeceasedTamer()
    {
        static SimulationWorld BaseTown(out long deer, out long villager, out long cursor)
        {
            var world = FlatWorld(
                width: 30, height: 14, seed: 303,
                taming: TamingConfig.Default with { BaseAcceptPercent = 100, TrustPerFeed = 20, CooldownTicks = 1 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 7, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human));
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            cursor = world.ReadEvents(0, 1).LatestSequence;
            villager = world.ReadSnapshot().Chunks.SelectMany(c => c)
                .First(r => r.VisualKey == Human && world.ReadEntityDetails(W, r.Id)!.Job != Job.Guard).Id.Value;
            deer = Spawn(world, 11, 7, Deer, ref cursor);
            return world;
        }

        // 1. Absent town: TamedTo = 2 when only 1 town exists.
        {
            var w = BaseTown(out long deer, out long villager, out _);
            w.SetAdventureForTest(deer, tamedTo: 2, tamerId: villager, trust: Default[Deer].TameTrust);
            byte[] fixture = SaveBytes(w);
            string beforeHash = w.StateHash();
            RequireSaveRejected(fixture, "absent TamedTo settlement");
            Check(w.StateHash() == beforeHash, "failed load mutated caller world state");
        }

        // 2. Future ManaTick anchor: ManaTick > tick (which caused negative ManaAt in ReadAdventure).
        {
            var w = BaseTown(out _, out long villager, out _);
            w.SetAdventureForTest(villager, mana: 50, manaTick: w.NextTickUnsafe + 5000);
            RequireSaveRejected(SaveBytes(w), "future ManaTick anchor");
        }

        // 3. Excessive trust beyond species TameTrust, and non-zero trust on untameable sheep.
        {
            var w = BaseTown(out long deer, out long villager, out _);
            w.SetAdventureForTest(deer, tamedTo: 0, tamerId: villager, trust: Default[Deer].TameTrust + 1);
            RequireSaveRejected(SaveBytes(w), "excessive Trust above species TameTrust");

            var wSheep = BaseTown(out _, out long v2, out long c2);
            long sheep = Spawn(wSheep, 12, 7, Sheep, ref c2);
            wSheep.SetAdventureForTest(sheep, tamedTo: 0, tamerId: v2, trust: 10);
            RequireSaveRejected(SaveBytes(wSheep), "non-zero trust on untameable species");
        }

        // 4. Never-allocated feeder ID (beyond NextEntityId and non-existent in entities/deaths).
        {
            var w = BaseTown(out long deer, out _, out _);
            w.SetAdventureForTest(deer, tamedTo: 1, tamerId: 99999, trust: Default[Deer].TameTrust);
            RequireSaveRejected(SaveBytes(w), "never-allocated TamerId");
        }

        // 5. Positive control: valid durable bond where the real feeder died and entered durable DeathRecord.
        {
            var w = BaseTown(out long deer, out _, out long cursor);
            var events = Run(w, 120, ref cursor);
            var tamed = events.First(e => e.Kind == EventKinds.AnimalTamed && e.Target == deer);
            long originalFeeder = tamed.Actor;
            w.KillForTest(originalFeeder);
            Run(w, 2, ref cursor); // sweeps dead entity from live list while keeping DeathRecord in deaths/deathIndex

            Check(w.ReadEntityDetails(W, new EntityId(originalFeeder))!.LifeState == LifeStates.Dead,
                "original feeder must be dead before saving positive control");
            var loaded = SimulationWorld.Load(new MemoryStream(SaveBytes(w)));
            var adv = loaded.ReadAdventure(W, new EntityId(deer))!;
            Check(adv.TamedTo == 1 && adv.TamerId == originalFeeder && adv.Trust == Default[Deer].TameTrust,
                $"durable bond must survive deceased feeder across save/load (TamedTo={adv.TamedTo}, TamerId={adv.TamerId}, Trust={adv.Trust})");
            for (int t = 0; t < 100; t++) { w.Step(); loaded.Step(); }
            Check(loaded.StateHash() == w.StateHash(), "world with deceased feeder diverged after load");
        }
    }

    // ---- Finding 3 ------------------------------------------------------------------------------

    private static void TwoSimultaneousFeedersPreserveSecondTownState()
    {
        var rows = Enumerable.Range(0, 15).Select(y =>
        {
            if (y is 0 or 14) return new string('^', 46);
            var line = ("^" + new string('.', 44) + "^").ToCharArray();
            if (y is 6 or 8)
                for (int x = 18; x <= 22; x++) line[x] = '^';
            return new string(line);
        }).ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 305,
            MapWidth = 46,
            MapHeight = 15,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0, Radius = 12 },
            Taming = TamingConfig.Default with { BaseAcceptPercent = 100, TrustPerFeed = 20, CooldownTicks = 60, GiveUpTicks = 120 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });

        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 7, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 34, 7, Faction.Human, "East"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human)); // West guard
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 34, 7, Human)); // East guard
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
        world.SetStorageForTest(2, food: 300, wood: 100, coins: 50);
        long cursor = world.ReadEvents(0, 1).LatestSequence;

        long deer = Spawn(world, 20, 7, Deer, ref cursor);
        // Spawn Feeder A (closer: 4 tiles -> ~9 ticks) and Feeder B (farther: 7 tiles -> ~15 ticks)
        // in the same tick so both select the unclaimed deer before the first feed executes.
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 16, 7, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 27, 7, Human));
        var spawnEvents = Run(world, 1, ref cursor).Where(e => e.Kind == EventKinds.CommandApplied).ToList();
        long feederA = spawnEvents[0].Target;
        long feederB = spawnEvents[1].Target;
        Check(world.ReadEntityDetails(W, new EntityId(feederA))!.SettlementId == 1, "feeder A belongs to West");
        Check(world.ReadEntityDetails(W, new EntityId(feederB))!.SettlementId == 2, "feeder B belongs to East");

        bool aStarted = false, bStarted = false;
        long firstFeeder = 0;
        int trustAfterFirst = 0;
        long secondFoodAtFirstFeed = 0;
        ulong rngAtFirstFeed = 0;
        var afterFirstEvents = new List<WorldEvent>();

        for (int t = 0; t < 40; t++)
        {
            var evs = Run(world, 1, ref cursor);
            foreach (var ev in evs)
            {
                if (ev.Kind == EventKinds.ActionStarted && ev.Reason == (int)ActionKind.Tame && ev.Target == deer)
                {
                    if (ev.Actor == feederA) aStarted = true;
                    if (ev.Actor == feederB) bStarted = true;
                }
            }
            var fed = evs.Where(e => e.Kind == EventKinds.TameFed && e.Target == deer).ToList();
            if (fed.Count > 0 && firstFeeder == 0)
            {
                Check(aStarted && bStarted, $"both feeders must have started Tame before first feed (A={aStarted}, B={bStarted})");
                firstFeeder = fed[0].Actor;
                long secondTownIndex = firstFeeder == feederA ? 1 : 0; // 0-based index into ReadSettlements()
                trustAfterFirst = world.ReadAdventure(W, new EntityId(deer))!.Trust;
                secondFoodAtFirstFeed = world.ReadSettlements()[(int)secondTownIndex].Food;
                rngAtFirstFeed = world.PsycheRngStateForTest;
                continue;
            }
            if (firstFeeder != 0)
            {
                afterFirstEvents.AddRange(evs);
                long secondFeeder = firstFeeder == feederA ? feederB : feederA;
                // Wait until second feeder finishes or abandons its concurrent attempt
                if (world.ActionOf(secondFeeder) != ActionKind.Tame && t >= 18) break;
            }
        }

        Check(firstFeeder != 0, "neither feeder completed first feed");
        long otherFeeder = firstFeeder == feederA ? feederB : feederA;
        int otherTownIdx = firstFeeder == feederA ? 1 : 0;
        // Run enough extra ticks to ensure second feeder's stale step would have landed if not rejected,
        // while staying well inside first feeder's CooldownTicks (60) so no second feed occurs from first feeder either.
        afterFirstEvents.AddRange(Run(world, 15, ref cursor));
        Check(!afterFirstEvents.Any(e => e.Kind == EventKinds.TameFed && e.Actor == otherFeeder),
            "second concurrent feeder must not feed or reset first feeder's claim");
        Check(world.ReadSettlements()[otherTownIdx].Food == secondFoodAtFirstFeed,
            $"second town food changed ({secondFoodAtFirstFeed} -> {world.ReadSettlements()[otherTownIdx].Food})");
        var adv = world.ReadAdventure(W, new EntityId(deer))!;
        Check(adv.TamerId == firstFeeder && adv.Trust == trustAfterFirst,
            $"tamer/trust altered by second feeder (Tamer={adv.TamerId} expected {firstFeeder}, Trust={adv.Trust} expected {trustAfterFirst})");
        Check(world.PsycheRngStateForTest == rngAtFirstFeed, "stale second feed drew from Psyche RNG");
    }

    // ---- Finding 4 ------------------------------------------------------------------------------

    private static void AbandonedRefusedAttemptExpiresWhileBondSurvives()
    {
        var rows = Enumerable.Range(0, 15).Select(y =>
        {
            if (y is 0 or 14) return new string('^', 46);
            var line = ("^" + new string('.', 44) + "^").ToCharArray();
            if (y is 6 or 8)
                for (int x = 18; x <= 23; x++) line[x] = '^';
            if (y == 7) { line[18] = '^'; line[23] = '^'; }
            return new string(line);
        }).ToList();
        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 404,
            MapWidth = 46,
            MapHeight = 15,
            Map = MapOverride.FromRows(rows),
            Species = Default,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Psyche = PsycheConfig.Default with { FearDecayPerTick = 50 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0, Radius = 11, FoodTarget = 0, WoodTarget = 0 },
            Taming = TamingConfig.Default with { BaseAcceptPercent = 75, TrustPerFeed = 20, CooldownTicks = 15, GiveUpTicks = 25 },
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });

        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 7, Faction.Human, "West"));
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 33, 7, Faction.Human, "East"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human)); // West guard
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 33, 7, Human)); // East guard
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
        world.SetStorageForTest(2, food: 300, wood: 100, coins: 50);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 15, ref cursor); // let both guards assume Guard job

        long feederA = Spawn(world, 19, 7, Human, ref cursor, Sex.Female); // d(West)=9 < d(East)=14
        Check(world.ReadEntityDetails(W, new EntityId(feederA))!.Job != Job.Guard, "Feeder A must not be Guard");
        long deer = Spawn(world, 20, 7, Deer, ref cursor);
        // Raise deer's fear high enough so Feed #1 refuses deterministically, while FearDecayPerTick=50 clears it after lock expiry.
        world.RaiseFearForTest(deer, 2000);

        bool refused = false;
        for (int t = 0; t < 40; t++)
        {
            var evs = Run(world, 1, ref cursor);
            var f = evs.FirstOrDefault(e => e.Kind == EventKinds.TameFed && e.Actor == feederA && e.Target == deer);
            if (f.Kind == EventKinds.TameFed)
            {
                Check(f.Reason == 0 && f.Amount == 0, $"expected Feed #1 refusal at zero trust, got Reason={f.Reason}, Trust={f.Amount}");
                refused = true;
                break;
            }
        }
        Check(refused, "Feeder A did not execute Feed #1 refusal");

        // Feeder A remains alive as a normal villager, but stops trying because West drops food to FoodReserve.
        world.SetStorageForTest(1, food: TamingConfig.Default.FoodReserve, wood: 100, coins: 50);
        // Spawn Feeder B (same sex -> no Mating distracting from Tame) in East inside the shared pen (x=22: d(East)=11 < d(West)=12) with full spare food.
        long feederB = Spawn(world, 22, 7, Human, ref cursor, Sex.Female);
        Check(world.ReadEntityDetails(W, new EntityId(feederB))!.SettlementId == 2, "Feeder B belongs to East");

        var later = Run(world, 1200, ref cursor);
        Check(world.ReadEntityDetails(W, new EntityId(feederA))!.LifeState == LifeStates.Alive, "Feeder A remained alive");
        var bFirstFeed = later.FirstOrDefault(e => e.Kind == EventKinds.TameFed && e.Actor == feederB && e.Target == deer && e.Reason == 1);
        var bDet = world.ReadEntityDetails(W, new EntityId(feederB))!;
        var dAdv = world.ReadAdventure(W, new EntityId(deer))!;
        string fedLog = string.Join(", ", later.Where(e => e.Kind == EventKinds.TameFed).Select(e => $"a={e.Actor}:r={e.Reason}:t={e.Amount}"));
        Check(bFirstFeed.Kind == EventKinds.TameFed,
            $"Feeder B remained permanently locked out (job={bDet.Job}, act={world.ActionOf(feederB)}, pos=({bDet.X},{bDet.Y}), tamer={dAdv.TamerId}, trust={dAdv.Trust}, fed=[{fedLog}])");
        Check(later.Any(e => e.Kind == EventKinds.AnimalTamed && e.Actor == feederB && e.Target == deer && e.Reason == 2),
            "Feeder B could not complete taming after Feeder A's attempt expired");

        // Part B: durable bond survives original successful feeder dying (and West regaining spare food).
        world.KillForTest(feederB);
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
        var postBond = Run(world, 80, ref cursor);
        Check(world.ReadEntityDetails(W, new EntityId(feederB))!.LifeState == LifeStates.Dead, "Feeder B is dead");
        var finalAdv = world.ReadAdventure(W, new EntityId(deer))!;
        Check(finalAdv.TamedTo == 2 && finalAdv.TamerId == feederB && finalAdv.Trust == Default[Deer].TameTrust,
            $"completed durable bond altered after feeder death (TamedTo={finalAdv.TamedTo}, Tamer={finalAdv.TamerId}, Trust={finalAdv.Trust})");
        Check(!postBond.Any(e => e.Kind == EventKinds.TameFed && e.Target == deer),
            "another town attempted to re-tame an already bonded animal");
    }

    // ---- Finding 5 ------------------------------------------------------------------------------

    private static void HealChargesExactFatigueAndSkipsIneligible()
    {
        var magic = MagicConfig.Default with { ManaRegenTicks = 1000 }; // isolate exact cast cost from passive regen
        var world = FlatWorld(width: 40, height: 14, seed: 505, magic: magic);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 20, 7, Faction.Human, "Keep"));
        for (int k = 0; k < 4; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 19 + k % 2, 7 + k / 2, Human));
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 30, ref cursor);

        var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == SpeciesCatalog.Human).Select(r => r.Id.Value).ToList();
        long healer = people.First(id => world.ReadEntityDetails(W, new EntityId(id))!.Job != Job.Guard);
        long hurt = people.First(id => id != healer && world.ReadEntityDetails(W, new EntityId(id))!.Job != Job.Guard);
        world.SetSkillForTest(healer, Skill.Scholarship, 10_000);
        world.SetHealthForTest(hurt, 300_000);

        bool healedOnce = false;
        for (int t = 0; t < 60; t++)
        {
            long manaBefore = world.ReadAdventure(W, new EntityId(healer))!.Mana;
            long fatigueBefore = world.FatigueOf(healer);
            long healthBefore = world.ReadEntityDetails(W, new EntityId(hurt))!.Health;
            var evs = Run(world, 1, ref cursor);
            var cast = evs.FirstOrDefault(e => e.Kind == EventKinds.SpellCast && e.Actor == healer && e.Reason == (int)Spell.Heal);
            if (cast.Kind == EventKinds.SpellCast)
            {
                long manaAfter = world.ReadAdventure(W, new EntityId(healer))!.Mana;
                long fatigueAfter = world.FatigueOf(healer);
                long healthAfter = world.ReadEntityDetails(W, new EntityId(hurt))!.Health;
                Check(manaAfter == manaBefore - magic.HealCost,
                    $"mana delta mismatch on Heal: {manaBefore} -> {manaAfter} (cost {magic.HealCost})");
                Check(fatigueAfter == fatigueBefore + CombatConfig.Default.FatiguePerAttack,
                    $"fatigue delta mismatch on Heal: {fatigueBefore} -> {fatigueAfter} (expected +{CombatConfig.Default.FatiguePerAttack})");
                Check(healthAfter == healthBefore + magic.HealAmount,
                    $"health delta mismatch on Heal: {healthBefore} -> {healthAfter} (expected +{magic.HealAmount})");
                healedOnce = true;
                break;
            }
        }
        Check(healedOnce, "scholar did not cast Heal on wounded townmate");

        // Now all townmates are at/above HealBelowHealth: no target is eligible -> zero mana/fatigue/health change.
        world.SetHealthForTest(hurt, magic.HealBelowHealth);
        long idleMana = world.ReadAdventure(W, new EntityId(healer))!.Mana;
        long idleFatigue = world.FatigueOf(healer);
        long idleHealth = world.ReadEntityDetails(W, new EntityId(hurt))!.Health;
        var noEvents = Run(world, 50, ref cursor);
        Check(!noEvents.Any(e => e.Kind == EventKinds.SpellCast), "Heal cast with no eligible target");
        Check(world.ReadAdventure(W, new EntityId(healer))!.Mana == idleMana, "mana changed when no target eligible");
        Check(world.FatigueOf(healer) == idleFatigue, "fatigue changed when no target eligible");
        Check(world.ReadEntityDetails(W, new EntityId(hurt))!.Health == idleHealth, "health changed when no target eligible");
    }

    // ---- Finding 6 ------------------------------------------------------------------------------

    private static void DamageMultipliersNoOverflowAndMonotonic()
    {
        static long WideReference(long basePower, int combatLevel, long attackerHealth, int allies, int allyBonusPct, int targetTakenPct)
        {
            BigInteger d = basePower;
            d = d * (100 + (BigInteger)10 * combatLevel) / 100;
            d = d * (50 + (BigInteger)50 * attackerHealth / VitalsConfig.MaxHealth) / 100;
            d = d * (100 + (BigInteger)allyBonusPct * allies) / 100;
            d = d * targetTakenPct / 100;
            return d >= long.MaxValue ? long.MaxValue : (long)d;
        }

        // Test extreme accepted values: GuardDamage = long.MaxValue, FireboltDamage = long.MaxValue,
        // large AllyBonusPercent / MaxAllies, Combat Skill MaxLevel = 1000, wounded attacker, and Dragon toughness (25%).
        var species = Default.SetItem(Wolf, Default[Wolf] with { AttackDamage = long.MaxValue });
        var combat = CombatConfig.Default with { UnarmedDamage = long.MaxValue, AllyBonusPercent = int.MaxValue / 4, MaxAllies = 100 };
        var magic = MagicConfig.Default with { FireboltDamage = long.MaxValue };
        var trade = TradeConfig.Default with { GuardDamage = long.MaxValue };
        var skills = SkillConfig.Default with { MaxLevel = SkillConfig.MaxSupportedLevel };

        var world = new SimulationWorld(new SimulationOptions
        {
            WorldId = W,
            Seed = 606,
            MapWidth = 30,
            MapHeight = 14,
            Map = MapOverride.FromRows(Enumerable.Range(0, 14).Select(y => y is 0 or 13 ? new string('^', 30) : "^" + new string('.', 28) + "^").ToList()),
            Species = species,
            Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
            Mind = MindConfig.Default with { FatiguePerTick = 0 },
            Settlements = SettlementConfig.Default with { FarmPlots = 0 },
            Combat = combat,
            Magic = magic,
            Trade = trade,
            Skills = skills,
            Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
        });

        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 7, Faction.Human, "Keep"));
        for (int k = 0; k < 3; k++)
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 7, Human));
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 20, ref cursor);

        var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == Human).Select(r => r.Id.Value).ToList();
        long guard = people.First(id => world.ReadEntityDetails(W, new EntityId(id))!.Job == Job.Guard);
        long fighter2 = people.First(id => id != guard);
        world.SetBraveryForTest(fighter2, 1000);
        long dragon = Spawn(world, 12, 7, SpeciesCatalog.Dragon, ref cursor);

        // Give guard max combat skill level and wounded 50% health; match ComputeDamageForTest against exact wide-integer reference.
        world.SetSkillForTest(guard, Skill.Combat, (long)skills.MaxLevel * skills.MaxLevel * skills.XpPerLevelSquared);
        world.SetHealthForTest(guard, 500_000);
        long dmgWoundedMaxMelee = world.ComputeDamageForTest(guard, dragon, trade.GuardDamage);
        long dmgWoundedHalfPower = world.ComputeDamageForTest(guard, dragon, trade.GuardDamage / 2);
        long dmgWoundedSpell = world.ComputeDamageForTest(guard, dragon, magic.FireboltDamage);
        long refWoundedMax = WideReference(long.MaxValue, skills.MaxLevel, 500_000, allies: 0, combat.AllyBonusPercent, Default[SpeciesCatalog.Dragon].DamageTakenPercent);
        long refWoundedHalf = WideReference(long.MaxValue / 2, skills.MaxLevel, 500_000, allies: 0, combat.AllyBonusPercent, Default[SpeciesCatalog.Dragon].DamageTakenPercent);

        Check(dmgWoundedMaxMelee == refWoundedMax && dmgWoundedMaxMelee > 1_000_000,
            $"melee damage wrapped or differed from wide reference: got {dmgWoundedMaxMelee}, expected {refWoundedMax}");
        Check(dmgWoundedSpell == refWoundedMax,
            $"spell damage wrapped or differed from wide reference: got {dmgWoundedSpell}, expected {refWoundedMax}");
        Check(dmgWoundedHalfPower == refWoundedHalf && dmgWoundedMaxMelee >= dmgWoundedHalfPower,
            $"monotonicity violated: max={dmgWoundedMaxMelee}, half={dmgWoundedHalfPower}");

        world.SetHealthForTest(guard, 1_000_000);
        long dmgHealthyMax = world.ComputeDamageForTest(guard, dragon, long.MaxValue);
        Check(dmgHealthyMax >= dmgWoundedMaxMelee, $"healthy damage {dmgHealthyMax} < wounded damage {dmgWoundedMaxMelee}");

        // Real combat against Dragon must kill Dragon immediately instead of dealing 1 damage after signed overflow.
        var fightEvents = Run(world, 40, ref cursor);
        var hit = fightEvents.FirstOrDefault(e => e.Kind == EventKinds.Attacked && e.Target == dragon);
        Check(hit.Kind == EventKinds.Attacked && hit.Amount == VitalsConfig.MaxHealth,
            $"expected lethal dragon hit for {VitalsConfig.MaxHealth}, got {(hit.Kind == EventKinds.Attacked ? hit.Amount : -1)}");
        Check(fightEvents.Any(e => e.Kind == EventKinds.EntityDied && e.Target == dragon),
            "dragon survived max-damage hit due to overflow wrapping damage to 1");
    }

    // ---- Finding 7 ------------------------------------------------------------------------------

    private static void ManaAndTrustNearLimitSaturateSafely()
    {
        // Reject BaseMana so near long.MaxValue that adding Aptitude (up to 100) could overflow.
        bool threw = false;
        try
        {
            _ = FlatWorld(magic: MagicConfig.Default with { BaseMana = long.MaxValue - 50 });
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }
        Check(threw, "MagicConfig.Validate must reserve headroom for Aptitude (+100)");

        // Accepted near-limit configuration: BaseMana = long.MaxValue - 100, TameTrust = int.MaxValue.
        var nearLimitMagic = MagicConfig.Default with { BaseMana = long.MaxValue - 100, ManaRegenTicks = 5 };
        var largeTrustSpecies = Default.SetItem(Deer, Default[Deer] with { TameTrust = int.MaxValue });
        var largeTrustTaming = TamingConfig.Default with
        {
            BaseAcceptPercent = 100,
            TrustPerFeed = int.MaxValue / 2 + 1000,
            CooldownTicks = 5,
            GiveUpTicks = 60,
        };

        var world = FlatWorld(
            width: 30, height: 14, seed: 707,
            species: largeTrustSpecies,
            magic: nearLimitMagic,
            taming: largeTrustTaming);
        world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 12, 7, Faction.Human, "Keep"));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 7, Human));
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 13, 7, Human));
        world.Step();
        world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
        long cursor = world.ReadEvents(0, 1).LatestSequence;
        Run(world, 15, ref cursor);

        var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Where(r => r.VisualKey == Human).Select(r => r.Id.Value).ToList();
        long caster = people.First(id => world.ReadEntityDetails(W, new EntityId(id))!.Job != Job.Guard);
        var advCaster = world.ReadAdventure(W, new EntityId(caster))!;
        Check(advCaster.MaxMana > 0 && advCaster.MaxMana <= long.MaxValue, $"MaxMana overflowed: {advCaster.MaxMana}");

        // Set partially filled mana bar just below MaxMana, let excessive regeneration elapse.
        world.SetManaForTest(caster, advCaster.MaxMana - 3);
        Run(world, 60, ref cursor); // +12 regen > remaining capacity 3
        var regenView = world.ReadAdventure(W, new EntityId(caster))!;
        Check(regenView.Mana == advCaster.MaxMana, $"mana regeneration wrapped/diverged: got {regenView.Mana}, expected {advCaster.MaxMana}");

        // Two large accepted feeds below int.MaxValue must saturate at TameTrust without signed overflow.
        long deer = Spawn(world, 14, 7, Deer, ref cursor);
        var tamingEvents = Run(world, 80, ref cursor);
        var feeds = tamingEvents.Where(e => e.Kind == EventKinds.TameFed && e.Target == deer && e.Reason == 1).ToList();
        Check(feeds.Count == 2, $"expected 2 accepted feeds to tame deer, got {feeds.Count}");
        Check(feeds[0].Amount == largeTrustTaming.TrustPerFeed, $"first feed trust {feeds[0].Amount}");
        Check(feeds[1].Amount == int.MaxValue && world.ReadAdventure(W, new EntityId(deer))!.TamedTo == 1,
            $"second feed overflowed or failed to tame: Trust={feeds[1].Amount}, TamedTo={world.ReadAdventure(W, new EntityId(deer))!.TamedTo}");

        // Save/Load round-trip must accept both the near-max caster mana anchor and near-max taming trust.
        byte[] saved = SaveBytes(world);
        var loaded = SimulationWorld.Load(new MemoryStream(saved));
        Check(loaded.StateHash() == world.StateHash(), "near-limit save/load hash mismatch");
        Check(loaded.ReadAdventure(W, new EntityId(caster))!.Mana == advCaster.MaxMana, "loaded caster mana mismatch");
        Check(loaded.ReadAdventure(W, new EntityId(deer))!.Trust == int.MaxValue, "loaded deer trust mismatch");
    }

    // ---- Finding 8 ------------------------------------------------------------------------------

    private static void TamedAnimalStaysLeashedOnEveryPathAndReturnsHome()
    {
        const int radius = 6;

        // 1. Boundary chase: hungry tamed wolf at East edge (21,10) with rabbit outside at (24,10) must never step outside radius 6.
        {
            var world = FlatWorld(
                width: 40, height: 22, seed: 801,
                vitals: new VitalsConfig(30_000, 0, 0, 0, 0, 250_000, 100_000, 4),
                settlements: SettlementConfig.Default with { FarmPlots = 0, Radius = radius });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 15, 10, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 15, 10, Human));
            world.Step();
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            long wolf = Spawn(world, 21, 10, Wolf, ref cursor);
            world.SetTamedForTest(wolf, 1);
            // Keep rabbit penned at (24,10)..(25,10) or just open enough for wolf to want to chase East:
            long rabbit = Spawn(world, 23, 10, Rabbit, ref cursor);

            var events = new List<WorldEvent>();
            for (int t = 0; t < 80; t++)
            {
                events.AddRange(Run(world, 1, ref cursor));
                var (wx, wy) = world.PositionOf(wolf);
                int dist = Math.Max(Math.Abs(wx - 15), Math.Abs(wy - 10));
                Check(dist <= radius, $"tamed wolf left town radius during chase: pos=({wx},{wy}), dist={dist} > {radius}");
            }
            Check(!events.Any(e => e.Kind == EventKinds.Attacked && e.Actor == wolf && e.Target == rabbit),
                "tamed wolf attacked outside rabbit after leaving boundary");
        }

        // 2. Boundary flee: tamed deer at East edge (21,10) with inward threat (wolf at 19,10) must flee along/inside radius, never out.
        {
            var world = FlatWorld(
                width: 40, height: 22, seed: 802,
                settlements: SettlementConfig.Default with { FarmPlots = 0, Radius = radius });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 15, 10, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 15, 10, Human));
            world.Step();
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            long deer = Spawn(world, 21, 10, Deer, ref cursor);
            world.SetTamedForTest(deer, 1);
            Spawn(world, 19, 10, Wolf, ref cursor); // wild inward threat within PerceptionRadius 8 (note: keep Guard off-range if wolf far from (15,10), or wolf scares deer immediately)

            for (int t = 0; t < 60; t++)
            {
                Run(world, 1, ref cursor);
                if (world.ReadEntityDetails(W, new EntityId(deer))!.LifeState != LifeStates.Alive) break;
                var (dx, dy) = world.PositionOf(deer);
                int dist = Math.Max(Math.Abs(dx - 15), Math.Abs(dy - 10));
                Check(dist <= radius, $"tamed deer fled outside town radius: pos=({dx},{dy}), dist={dist} > {radius}");
            }
        }

        // 3. Starting outside (axis AND exact diagonal corner) must monotonically progress homeward and enter town radius;
        //    outside food bush must never pull a boundary-leashed tamed animal outside.
        {
            var rows = Enumerable.Range(0, 26).Select(y => y is 0 or 25 ? new string('^', 40) : "^" + new string('.', 38) + "^").ToList();
            var map = MapOverride.FromRows(rows, new ResourcePlacement(23, 10, ResourceKind.Food, 20));
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W,
                Seed = 803,
                MapWidth = 40,
                MapHeight = 26,
                Map = map,
                Species = Default,
                Vitals = new VitalsConfig(15_000, 0, 0, 0, 0, 250_000, 100_000, 4),
                Mind = MindConfig.Default with { FatiguePerTick = 0 },
                Settlements = SettlementConfig.Default with { FarmPlots = 0, Radius = radius },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 15, 10, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 15, 10, Human));
            world.Step();
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            // 3a: hungry tamed wolf outside East at (26,10) with rabbit further outside at (28,10)
            long wolfOut = Spawn(world, 26, 10, Wolf, ref cursor);
            world.SetTamedForTest(wolfOut, 1);
            Spawn(world, 28, 10, Rabbit, ref cursor);

            // 3b: tamed deer outside diagonal corner at (24,19) -> dx=+9, dy=+9
            long deerDiag = Spawn(world, 24, 19, Deer, ref cursor);
            world.SetTamedForTest(deerDiag, 1);

            // 3c: hungry tamed deer at boundary (21,10) next to outside berry bush at (23,10)
            long deerFood = Spawn(world, 21, 10, Deer, ref cursor);
            world.SetTamedForTest(deerFood, 1);

            int prevWolfDist = 11, prevDiagCheb = 9;
            for (int t = 0; t < 150; t++)
            {
                Run(world, 1, ref cursor);
                var (wx, wy) = world.PositionOf(wolfOut);
                int wDist = Math.Max(Math.Abs(wx - 15), Math.Abs(wy - 10));
                if (prevWolfDist > radius)
                    Check(wDist <= prevWolfDist, $"outside tamed wolf drifted further out ({prevWolfDist} -> {wDist}) at ({wx},{wy})");
                else
                    Check(wDist <= radius, $"re-entered tamed wolf exited radius ({wDist})");
                prevWolfDist = wDist;

                var (dx, dy) = world.PositionOf(deerDiag);
                int dCheb = Math.Max(Math.Abs(dx - 15), Math.Abs(dy - 10));
                if (prevDiagCheb > radius)
                    Check(dCheb <= prevDiagCheb, $"diagonal outside deer drifted further out ({prevDiagCheb} -> {dCheb}) at ({dx},{dy})");
                else
                    Check(dCheb <= radius, $"re-entered diagonal deer exited radius ({dCheb})");
                prevDiagCheb = dCheb;

                var (fx, fy) = world.PositionOf(deerFood);
                int fDist = Math.Max(Math.Abs(fx - 15), Math.Abs(fy - 10));
                Check(fDist <= radius, $"tamed deer crossed radius to reach outside food: ({fx},{fy}), dist={fDist}");
            }
            Check(prevWolfDist <= radius, $"outside tamed wolf did not make it home inside radius {radius} (ended at dist {prevWolfDist})");
            Check(prevDiagCheb <= radius, $"diagonal outside tamed deer did not make it home inside radius {radius} (ended at dist {prevDiagCheb})");
        }
    }

    // ---- Finding 9 ------------------------------------------------------------------------------

    private static void DensePerceptionScansObeyBudgetAndTieBreaks()
    {
        // 1. Single-decision pass-through: picking Tame must inspect the neighbourhood once, not 3x.
        {
            var world = FlatWorld(
                width: 30, height: 14, seed: 901,
                settlements: SettlementConfig.Default with { FarmPlots = 0, FoodTarget = 0, WoodTarget = 0 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 12, 7, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 7, Human, Sex.Female)); // Guard
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            Run(world, 12, ref cursor);

            long feeder = Spawn(world, 12, 7, Human, ref cursor, Sex.Female);
            for (int i = 0; i < 15; i++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 14, 7, Deer, Sex.Female));

            long tameTickVisits = -1;
            for (int t = 0; t < 30; t++)
            {
                var evs = Run(world, 1, ref cursor);
                if (evs.Any(e => e.Kind == EventKinds.ActionStarted && e.Actor == feeder && e.Reason == (int)ActionKind.Tame))
                {
                    tameTickVisits = world.LastTameCandidateVisits;
                    break;
                }
            }
            // Total living entities in perception is 17; one scan must inspect <= 17 (not 3 * 17 = 51).
            Check(tameTickVisits > 0 && tameTickVisits <= 20,
                $"expected <= 1 TameCandidate scan (<= 20 visits) on decision tick, got {tameTickVisits}");
        }

        // 2. Dense Guards: budget enforced per tick + exact nearest and lowest-ID tie-break across slice boundary,
        //    plus Save/Load mid-scan state identity.
        {
            var mind = MindConfig.Default with { FatiguePerTick = 0, CandidateVisitBudgetPerTick = 96, QuerySliceBudget = 32 };
            var trade = TradeConfig.Default with { MembersPerGuard = 1 };
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W,
                Seed = 902,
                MapWidth = 32,
                MapHeight = 16,
                Map = MapOverride.FromRows(Enumerable.Range(0, 16).Select(y => y is 0 or 15 ? new string('^', 32) : "^" + new string('.', 30) + "^").ToList()),
                Species = Default,
                Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
                Mind = mind,
                Trade = trade,
                Settlements = SettlementConfig.Default with { FarmPlots = 0, Radius = 10, FoodTarget = 0, WoodTarget = 0 },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 12, 7, Faction.Human, "Keep"));
            // Co-locate 20 guards + 70 bystanders + late threats all inside bucket (1,0) [x in 8..15, y in 0..7]
            for (int i = 0; i < 20; i++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 5, Human, Sex.Female));
            for (int i = 0; i < 65; i++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 11, 5, Sheep, Sex.Female));
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            // Spawn distant threat early in ID order, and tied closer threats LATE (beyond QuerySliceBudget = 32).
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 15, 5, Wolf)); // dist 5
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 5, Wolf)); // dist 2, lower ID
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 5, Wolf)); // dist 2, higher ID
            var wolfSpawns = Run(world, 1, ref cursor).Where(e => e.Kind == EventKinds.CommandApplied).Select(e => e.Target).ToList();
            long wolfFar = wolfSpawns[0], wolfBest = wolfSpawns[1], wolfTie = wolfSpawns[2];
            _ = wolfFar;

            byte[]? midScanSave = null;
            SimulationWorld? loadedShadow = null;
            long selectedTarget = 0;
            for (int t = 0; t < 100; t++)
            {
                var evs = Run(world, 1, ref cursor);
                loadedShadow?.Step();
                if (loadedShadow is not null)
                    Check(loadedShadow.StateHash() == world.StateHash(), $"mid-scan guard replay diverged at step {t}");

                Check(world.LastCandidateVisits <= mind.CandidateVisitBudgetPerTick,
                    $"dense guards exceeded visit budget at tick {world.LastCompletedTick}: {world.LastCandidateVisits} > {mind.CandidateVisitBudgetPerTick}");
                if (midScanSave is null && world.LastCandidateVisits > 0)
                {
                    midScanSave = SaveBytes(world);
                    loadedShadow = SimulationWorld.Load(new MemoryStream(midScanSave));
                    Check(loadedShadow.StateHash() == world.StateHash(), "mid-scan initial save/load hash mismatch");
                }
                var alert = evs.FirstOrDefault(e => e.Kind == EventKinds.GuardAlert);
                if (alert.Kind == EventKinds.GuardAlert && selectedTarget == 0)
                    selectedTarget = alert.Target;
            }
            Check(loadedShadow is not null, "dense guard save checkpoint was never captured");
            Check(selectedTarget == wolfBest && selectedTarget != wolfTie,
                $"dense guard tie-break / nearest failed across slices: picked {selectedTarget}, expected {wolfBest} (tie {wolfTie}, far {wolfFar})");
        }

        // 3. Dense Healers: budget enforced + most wounded & lowest ID wins even past slice boundary.
        {
            var mind = MindConfig.Default with { FatiguePerTick = 0, CandidateVisitBudgetPerTick = 96, QuerySliceBudget = 32 };
            var world = FlatWorld(width: 32, height: 16, seed: 903, mind: mind,
                settlements: SettlementConfig.Default with { FarmPlots = 0, Radius = 10, FoodTarget = 0, WoodTarget = 0 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 5, Faction.Human, "Keep"));
            for (int i = 0; i < 80; i++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 5, Human, Sex.Female));
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            long cursor = world.ReadEvents(0, 1).LatestSequence;

            var people = world.ReadSnapshot().Chunks.SelectMany(c => c).Select(r => r.Id.Value).ToList();
            for (int i = 0; i < 15; i++) world.SetSkillForTest(people[i], Skill.Scholarship, 10_000);
            long hurtMild = people[5];
            long hurtBest = people[75];
            long hurtTie = people[78];
            world.SetHealthForTest(hurtMild, 500_000);
            world.SetHealthForTest(hurtBest, 200_000);
            world.SetHealthForTest(hurtTie, 200_000);

            long healedTarget = 0;
            for (int t = 0; t < 80; t++)
            {
                var evs = Run(world, 1, ref cursor);
                Check(world.LastCandidateVisits <= mind.CandidateVisitBudgetPerTick,
                    $"dense healers exceeded visit budget at tick {world.LastCompletedTick}: {world.LastCandidateVisits} > {mind.CandidateVisitBudgetPerTick}");
                var spell = evs.FirstOrDefault(e => e.Kind == EventKinds.SpellCast && e.Reason == (int)Spell.Heal);
                if (spell.Kind == EventKinds.SpellCast && healedTarget == 0)
                    healedTarget = spell.Target;
            }
            Check(healedTarget == hurtBest && healedTarget != hurtTie && healedTarget != hurtMild,
                $"dense healer picked wrong target {healedTarget}; expected lowest-HP/lowest-ID {hurtBest} (mild={hurtMild}, tie={hurtTie})");
        }

        // 4. Dense Feeders + AlliesFighting short-circuit: budget enforced + closest/lowest-ID deer wins late in bucket.
        {
            var mind = MindConfig.Default with { FatiguePerTick = 0, CandidateVisitBudgetPerTick = 96, QuerySliceBudget = 32 };
            var taming = TamingConfig.Default with { BaseAcceptPercent = 100, TrustPerFeed = 20, CooldownTicks = 60 };
            var world = FlatWorld(width: 32, height: 16, seed: 904, mind: mind, taming: taming,
                settlements: SettlementConfig.Default with { FarmPlots = 0, Radius = 10, FoodTarget = 0, WoodTarget = 0 });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 10, 5, Faction.Human, "Keep"));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 5, Human, Sex.Female)); // Guard
            world.Step();
            world.SetStorageForTest(1, food: 300, wood: 100, coins: 50);
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            Run(world, 15, ref cursor);

            for (int i = 0; i < 12; i++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 10, 5, Human, Sex.Female));
            var feeders = Run(world, 1, ref cursor).Where(e => e.Kind == EventKinds.CommandApplied).Select(e => e.Target).ToList();
            // 65 farther deer early in bucket (1,0), then two tied closest deer LATE (> QuerySliceBudget = 32)
            for (int i = 0; i < 65; i++) world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 15, 5, Deer, Sex.Female));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 11, 5, Deer, Sex.Female));
            world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 11, 5, Deer, Sex.Female));
            var lateDeer = Run(world, 1, ref cursor).Where(e => e.Kind == EventKinds.CommandApplied).Select(e => e.Target).ToList();
            long deerBest = lateDeer[^2];
            long deerTie = lateDeer[^1];

            long tamedStartTarget = 0;
            for (int t = 0; t < 120; t++)
            {
                var evs = Run(world, 1, ref cursor);
                Check(world.LastCandidateVisits <= mind.CandidateVisitBudgetPerTick,
                    $"dense feeders exceeded visit budget at tick {world.LastCompletedTick}: {world.LastCandidateVisits} > {mind.CandidateVisitBudgetPerTick}");
                var startTame = evs.FirstOrDefault(e => e.Kind == EventKinds.ActionStarted && e.Reason == (int)ActionKind.Tame && feeders.Contains(e.Actor));
                if (startTame.Kind == EventKinds.ActionStarted && tamedStartTarget == 0)
                    tamedStartTarget = startTame.Target;
            }
            Check(tamedStartTarget == deerBest && tamedStartTarget != deerTie,
                $"dense feeder picked wrong target {tamedStartTarget}; expected closest/lowest-ID {deerBest} (tie={deerTie})");
        }

        // 5. Dense Combat short-circuit: AlliesFighting stops at MaxAllies and required hits obey budget.
        {
            var mind = MindConfig.Default with { FatiguePerTick = 0, CandidateVisitBudgetPerTick = 64, QuerySliceBudget = 32 };
            var trade = TradeConfig.Default with { MembersPerGuard = 1 };
            var world = new SimulationWorld(new SimulationOptions
            {
                WorldId = W,
                Seed = 905,
                MapWidth = 30,
                MapHeight = 14,
                Map = MapOverride.FromRows(Enumerable.Range(0, 14).Select(y => y is 0 or 13 ? new string('^', 30) : "^" + new string('.', 28) + "^").ToList()),
                Species = Default,
                Vitals = new VitalsConfig(0, 0, 0, 0, 0, 250_000, 100_000, 4),
                Mind = mind,
                Trade = trade,
                Settlements = SettlementConfig.Default with { FarmPlots = 0, Radius = 10, FoodTarget = 0, WoodTarget = 0 },
                Grants = ImmutableSortedDictionary<long, AuthorityGrant>.Empty.Add(God, AuthorityGrant.God),
            });
            world.SubmitCommand(new FoundSettlementCommand(W, God, CommandModes.God, 12, 7, Faction.Human, "Keep"));
            for (int i = 0; i < 60; i++)
                world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, 12, 7, Human, Sex.Female));
            world.Step();
            long cursor = world.ReadEvents(0, 1).LatestSequence;
            // Let guards recognize adjacent Dragon and attack together in dense tile.
            long dragon = Spawn(world, 13, 7, SpeciesCatalog.Dragon, ref cursor);
            var evs = new List<WorldEvent>();
            for (int t = 0; t < 80; t++)
            {
                evs.AddRange(Run(world, 1, ref cursor));
                Check(world.LastCandidateVisits <= mind.CandidateVisitBudgetPerTick,
                    $"dense combat exceeded visit budget at tick {world.LastCompletedTick}: {world.LastCandidateVisits} > {mind.CandidateVisitBudgetPerTick}");
            }
            // At least one guard hit with ally bonus (> base GuardDamage / 4 on dragon hide) without scanning 60 * attackers.
            Check(evs.Any(e => e.Kind == EventKinds.Attacked && e.Target == dragon && e.Amount > trade.GuardDamage * Default[SpeciesCatalog.Dragon].DamageTakenPercent / 100),
                "dense guards did not receive AlliesFighting short-circuit bonus");
        }
    }
}
