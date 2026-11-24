using FantasyWorld.Simulation;
using System.Collections.Immutable;
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

    private static long Spawn(SimulationWorld world, int x, int y, int species, ref long cursor)
    {
        world.SubmitCommand(new SpawnEntityCommand(W, God, CommandModes.God, x, y, species));
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
}
