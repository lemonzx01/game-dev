using System;
using System.Collections.Generic;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.MockData;

/// <summary>
/// TEMPORARY MOCK DATA ONLY — strictly isolated to Sandbox/PresentationTest.
/// Does NOT implement Simulation Engine rules, AI decisions, Save/Load, or T001 contracts.
/// </summary>
public enum MockSpeciesKind
{
    Human,
    Demon,
    Sheep,
    Deer,
    Wolf,
    TrueDragon
}

public enum MockProfession
{
    Villager,
    Farmer,
    Guard,
    Mage
}

public enum MockLifeState
{
    Alive,
    Dead
}

public enum MockAnimPose
{
    Idle,
    Walk,
    WorkAttack,
    Sleep,
    Dead
}

public enum MockReceiptState
{
    Queued,
    Applied,
    Rejected,
    OutcomeUnknown
}

public sealed record MockCommandReceiptPreview(long CommandSequence, int TargetTick, string Summary, MockReceiptState State);

public sealed record MockEventGapRecoveryResult(
    long GapStart,
    long GapEnd,
    long NewEventCursor,
    int RecoveredHistoryMilestones,
    int MarkedUnknownReceipts);

public enum MockTileKind
{
    Grass = 0,
    Dirt = 1,
    ShallowWater = 2,
    DeepWater = 3,
    ForestFloor = 4,
    Mountain = 5,
    Snow = 6,
    Farmland = 7
}

public enum MockPropKind
{
    OakTree,
    PineTree,
    BerryBushRipe,
    BerryBushEmpty,
    CropGrowing,
    CropRipe,
    ManaHerb,
    DeadStump,
    House,
    Storage,
    MarketStall,
    GuardPost
}

public sealed record MockEntityVisual(
    int Id,
    string DisplayName,
    MockSpeciesKind Species,
    MockProfession Profession,
    MockLifeState LifeState,
    Vector2 BasePosition,
    uint AppearanceSeed,
    string ActionLabel,
    bool IsOrbitPreview = false,
    bool IsGodPreviewMarker = false);

public sealed record MockNpcDetail(
    int Id,
    string Name,
    string SpeciesLabel,
    string LifeStageAge,
    MockLifeState LifeState,
    float HpRatio,
    float HungerRatio,
    float ThirstRatio,
    float EnergyRatio,
    string CurrentAction,
    string DecisionReason,
    string[] Traits,
    string[] Emotions,
    string[] Memories,
    string[] Skills,
    string[] Relationships,
    string? DeathSummary);

public sealed record MockEventItem(
    int TickLabel,
    string Category,
    string Message,
    Vector2 WorldPosition,
    int? RelatedEntityId);

public sealed record MockPropPlacement(MockPropKind Kind, Vector2 Position);

public static class MockWorldFixture
{
    public const int TileSize = 16;
    public const int MapTilesX = 128;
    public const int MapTilesY = 128;

    public static MockTileKind[,] BuildMockTerrainGrid()
    {
        var grid = new MockTileKind[MapTilesX, MapTilesY];
        for (int y = 0; y < MapTilesY; y++)
        {
            for (int x = 0; x < MapTilesX; x++)
            {
                // Pure geometric test landscape (river, northern snow/mountain, central town dirt/farmland, forest).
                if (x + y < 28)
                {
                    grid[x, y] = (x + y < 16) ? MockTileKind.Snow : MockTileKind.Mountain;
                }
                else if (Math.Abs(y - (48 + (int)(Math.Sin(x * 0.12f) * 4))) <= 2)
                {
                    grid[x, y] = MockTileKind.DeepWater;
                }
                else if (Math.Abs(y - (48 + (int)(Math.Sin(x * 0.12f) * 4))) <= 4)
                {
                    grid[x, y] = MockTileKind.ShallowWater;
                }
                else if (x is >= 26 and <= 44 && y is >= 24 and <= 38)
                {
                    grid[x, y] = (x + y) % 5 == 0 ? MockTileKind.Dirt : MockTileKind.Farmland;
                }
                else if (x is >= 20 and <= 50 && y is >= 18 and <= 42)
                {
                    grid[x, y] = (x == 35 || y == 30) ? MockTileKind.Dirt : MockTileKind.Grass;
                }
                else if (x > 72 && y > 60)
                {
                    grid[x, y] = MockTileKind.ForestFloor;
                }
                else
                {
                    grid[x, y] = MockTileKind.Grass;
                }
            }
        }
        return grid;
    }

    public static List<MockPropPlacement> BuildMockProps()
    {
        var props = new List<MockPropPlacement>
        {
            new(MockPropKind.House, new Vector2(28 * TileSize, 22 * TileSize)),
            new(MockPropKind.House, new Vector2(32 * TileSize, 22 * TileSize)),
            new(MockPropKind.Storage, new Vector2(36 * TileSize, 22 * TileSize)),
            new(MockPropKind.MarketStall, new Vector2(34 * TileSize, 28 * TileSize)),
            new(MockPropKind.GuardPost, new Vector2(45 * TileSize, 26 * TileSize)),
            new(MockPropKind.CropRipe, new Vector2(28 * TileSize, 34 * TileSize)),
            new(MockPropKind.CropGrowing, new Vector2(31 * TileSize, 34 * TileSize)),
            new(MockPropKind.BerryBushRipe, new Vector2(42 * TileSize, 24 * TileSize)),
            new(MockPropKind.BerryBushEmpty, new Vector2(44 * TileSize, 24 * TileSize)),
            new(MockPropKind.ManaHerb, new Vector2(46 * TileSize, 24 * TileSize)),
            new(MockPropKind.OakTree, new Vector2(40 * TileSize, 21 * TileSize)),
            new(MockPropKind.PineTree, new Vector2(43 * TileSize, 21 * TileSize)),
            new(MockPropKind.DeadStump, new Vector2(46 * TileSize, 21 * TileSize))
        };

        // Add decorative forest scatter for zoom/culling preview.
        for (int i = 0; i < 160; i++)
        {
            uint h = Hash((uint)(i + 901));
            int tx = 65 + (int)(h % 56);
            int ty = 12 + (int)((h >> 8) % 108);
            var kind = (i % 6 == 0) ? MockPropKind.BerryBushRipe
                : (i % 5 == 0) ? MockPropKind.PineTree
                : (i % 17 == 0) ? MockPropKind.ManaHerb
                : MockPropKind.OakTree;
            props.Add(new MockPropPlacement(kind, new Vector2(tx * TileSize, ty * TileSize)));
        }

        return props;
    }

    public static List<MockEntityVisual> BuildShowcaseAndStressEntities(int targetCount)
    {
        var list = new List<MockEntityVisual>(Math.Max(16, targetCount))
        {
            // Showcase heroes/creatures near center of mock village (tx=32..48, ty=26..36).
            new(101, "Mira", MockSpeciesKind.Human, MockProfession.Farmer, MockLifeState.Alive, new Vector2(31 * TileSize, 27 * TileSize), 0x1142A0u, "Harvest (Mock)", IsOrbitPreview: true),
            new(102, "Kael", MockSpeciesKind.Human, MockProfession.Guard, MockLifeState.Alive, new Vector2(37 * TileSize, 27 * TileSize), 0x2299B1u, "Patrol (Mock)", IsOrbitPreview: true),
            new(103, "Zarak", MockSpeciesKind.Demon, MockProfession.Mage, MockLifeState.Alive, new Vector2(40 * TileSize, 29 * TileSize), 0x7731D4u, "Study Tome (Mock)"),
            new(104, "Alden (Fallen)", MockSpeciesKind.Human, MockProfession.Villager, MockLifeState.Dead, new Vector2(46 * TileSize, 31 * TileSize), 0x5512C0u, "Dead (Mock)"),
            new(105, "Woolly", MockSpeciesKind.Sheep, MockProfession.Villager, MockLifeState.Alive, new Vector2(30 * TileSize, 32 * TileSize), 0x01u, "Graze (Mock)"),
            new(106, "Forest Stag", MockSpeciesKind.Deer, MockProfession.Villager, MockLifeState.Alive, new Vector2(40 * TileSize, 30 * TileSize), 0x02u, "Forage (Mock)"),
            new(107, "Ashfang", MockSpeciesKind.Wolf, MockProfession.Villager, MockLifeState.Alive, new Vector2(39 * TileSize, 31 * TileSize), 0x03u, "Stalk (Mock)"),
            new(108, "Ignis Rex", MockSpeciesKind.TrueDragon, MockProfession.Villager, MockLifeState.Alive, new Vector2(36 * TileSize, 35 * TileSize), 0x04u, "Perch (Mock)")
        };

        for (int i = list.Count; i < targetCount; i++)
        {
            uint h = Hash((uint)(i + 1));
            float px = 48f + (h % ((MapTilesX - 6) * TileSize));
            float py = 48f + ((h >> 12) % ((MapTilesY - 6) * TileSize));
            MockSpeciesKind species = (i % 10) switch
            {
                0 or 1 or 2 or 3 => MockSpeciesKind.Human,
                4 or 5 => MockSpeciesKind.Demon,
                6 => MockSpeciesKind.Sheep,
                7 => MockSpeciesKind.Deer,
                8 => MockSpeciesKind.Wolf,
                _ => (i % 50 == 9) ? MockSpeciesKind.TrueDragon : MockSpeciesKind.Human
            };
            var prof = (MockProfession)(i % 4);
            list.Add(new MockEntityVisual(
                1000 + i,
                $"{species} #{i}",
                species,
                prof,
                MockLifeState.Alive,
                new Vector2(px, py),
                h,
                "Idle (Mock)"));
        }

        return list;
    }

    public static Dictionary<int, MockNpcDetail> BuildMockNpcDetails()
    {
        return new Dictionary<int, MockNpcDetail>
        {
            [101] = new MockNpcDetail(
                101,
                "Mira [Mock Fixture]",
                "Human",
                "Adult · 24y",
                MockLifeState.Alive,
                0.92f, 0.45f, 0.78f, 0.66f,
                "Harvest Wheat (Mock UI Sample)",
                "Sample Reason: Hunger moderate + Ripe crops nearby (static mock text)",
                new[] { "Diligent (+0.7)", "Cautious (+0.5)", "Kind (+0.6)" },
                new[] { "Calm", "Focused" },
                new[] { "[D11] Stored 4 wheat at Granary", "[D10] Saw Ashfang near East Forest" },
                new[] { "Farming Lv.3", "Foraging Lv.2", "Cooking Lv.1" },
                new[] { "Trusts Kael (0.72)", "Wary of Ashfang (Fear 0.65)" },
                null),
            [103] = new MockNpcDetail(
                103,
                "Zarak [Mock Fixture]",
                "Demon",
                "Adult · 38y",
                MockLifeState.Alive,
                0.88f, 0.72f, 0.69f, 0.81f,
                "Read Arcane Tome (Mock UI Sample)",
                "Sample Reason: Safe indoors + Curious trait (static mock text)",
                new[] { "Ambitious (+0.8)", "Scholarly (+0.9)", "Brave (+0.4)" },
                new[] { "Curious", "Proud" },
                new[] { "[D12] Traded herbs with Human settlement", "[D08] Observed Ignis Rex overhead" },
                new[] { "Arcana Lv.4", "Literacy Lv.3", "Alchemy Lv.2" },
                new[] { "Respects Mira (0.55)", "Rival: Kael (0.30)" },
                null),
            [104] = new MockNpcDetail(
                104,
                "Alden (Fallen) [Mock Fixture]",
                "Human",
                "Adult · 31y (Deceased)",
                MockLifeState.Dead,
                0.0f, 0.15f, 0.30f, 0.0f,
                "Dead — History Retained",
                "Died during mock wolf encounter preview; record preserved in Inspector",
                new[] { "Reckless (+0.6)", "Loyal (+0.7)" },
                new[] { "Fear (Final)" },
                new[] { "[D09] Attacked by Ashfang near East Forest", "[D05] Built Village Gate" },
                new[] { "Woodcutting Lv.2", "Spear Lv.1" },
                new[] { "Friend of Kael (0.80)" },
                "Day 9 · East Forest — Fatal predator attack (Mock Dead History Test)"),
            [108] = new MockNpcDetail(
                108,
                "Ignis Rex [Mock Fixture]",
                "True Dragon (Quadruped Winged)",
                "Elder · 240y",
                MockLifeState.Alive,
                1.00f, 0.85f, 0.90f, 0.95f,
                "Guard Cliff Perch (Mock UI Sample)",
                "Sample Reason: Territory patrol (static mock text; real dragon non-humanoid)",
                new[] { "Territorial (+0.9)", "Ancient Pride (+0.8)" },
                new[] { "Watchful" },
                new[] { "[D02] Claimed East Ridge", "[D11] Ignored village patrol" },
                new[] { "Flight Lv.5", "Fire Breath Lv.4" },
                new[] { "Neutral to Settlements" },
                null)
        };
    }

    public static List<MockEventItem> BuildMockEvents()
    {
        return new List<MockEventItem>
        {
            new(120, "Life", "Mira harvested mock wheat plot at Village Farm", new Vector2(31 * TileSize, 27 * TileSize), 101),
            new(114, "Combat", "Alden fell to Ashfang near East Forest (Dead History Demo)", new Vector2(46 * TileSize, 31 * TileSize), 104),
            new(108, "Life", "Zarak studied Arcane Tome at Demon/Human Market", new Vector2(40 * TileSize, 29 * TileSize), 103),
            new(96, "Combat", "Ignis Rex sighted above East Ridge", new Vector2(66 * TileSize, 24 * TileSize), 108)
        };
    }

    public static MockEventGapRecoveryResult SimulateMockGapUiRecovery(
        long currentEventCursor,
        long oldestAvailable,
        IReadOnlyList<MockEventItem> mockHistoryMilestones,
        List<MockCommandReceiptPreview> pendingReceipts,
        int latestCompletedTick)
    {
        long gapStart = currentEventCursor + 1;
        long gapEnd = oldestAvailable - 1;
        int unknownCount = 0;

        for (int i = 0; i < pendingReceipts.Count; i++)
        {
            var r = pendingReceipts[i];
            if (r.State == MockReceiptState.Queued && r.TargetTick <= latestCompletedTick)
            {
                pendingReceipts[i] = r with { State = MockReceiptState.OutcomeUnknown };
                unknownCount++;
            }
        }

        int recovered = 0;
        for (int i = 0; i < mockHistoryMilestones.Count; i++)
        {
            long seq = mockHistoryMilestones[i].TickLabel;
            if (seq >= gapStart && seq <= gapEnd)
            {
                recovered++;
            }
        }

        return new MockEventGapRecoveryResult(gapStart, gapEnd, oldestAvailable - 1, recovered, unknownCount);
    }

    public static Image BuildMinimapImage(MockTileKind[,] grid)
    {
        var img = Image.CreateEmpty(MapTilesX, MapTilesY, false, Image.Format.Rgba8);
        for (int y = 0; y < MapTilesY; y++)
        {
            for (int x = 0; x < MapTilesX; x++)
            {
                Color col = grid[x, y] switch
                {
                    MockTileKind.Dirt => new Color(0.54f, 0.38f, 0.24f),
                    MockTileKind.ShallowWater => new Color(0.25f, 0.64f, 0.82f),
                    MockTileKind.DeepWater => new Color(0.13f, 0.34f, 0.60f),
                    MockTileKind.ForestFloor => new Color(0.19f, 0.38f, 0.20f),
                    MockTileKind.Mountain => new Color(0.45f, 0.47f, 0.52f),
                    MockTileKind.Snow => new Color(0.91f, 0.94f, 0.97f),
                    MockTileKind.Farmland => new Color(0.42f, 0.29f, 0.17f),
                    _ => new Color(0.30f, 0.58f, 0.26f)
                };
                img.SetPixel(x, y, col);
            }
        }
        return img;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }
}
