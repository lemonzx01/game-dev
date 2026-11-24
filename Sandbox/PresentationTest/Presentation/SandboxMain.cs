using System;
using System.Collections.Generic;
using System.Linq;
using FantasyWorldSimulator.Sandbox.PresentationTest.MockData;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

/// <summary>
/// Entry point for the isolated PresentationTest Sandbox.
/// Hosts mock map rendering, modular character showcase, camera/zoom,
/// observer/god HUD, sprite batching benchmark, and headless CLI verification.
/// </summary>
public partial class SandboxMain : Node2D
{
    private TextureLoader _textures = null!;
    private Shader _paletteShader = null!;
    private Shader _multiMeshShader = null!;

    private SandboxCamera2D _camera = null!;
    private SandboxHud _hud = null!;
    private SpriteRenderBenchmark _benchmark = null!;
    private Node2D _showcaseRoot = null!;
    private Sprite2D _godGhostPreview = null!;

    private List<MockEntityVisual> _entities = new();
    private List<MockPropPlacement> _props = new();
    private readonly List<ModularCharacterPreview> _showcaseRigs = new();

    private float _elapsedSec;
    private bool _orbitPaused;
    private float _speedScale = 1.0f;
    private int _nextMockGodId = 90000;
    private string? _screenshotPath;
    private int _screenshotWarmupFrames = 4;
    private bool _vulkanDrawCallBench;
    private int _vulkanBenchStage;
    private int _vulkanBenchFrames;

    public override void _Ready()
    {
        _textures = new TextureLoader();
        _paletteShader = GD.Load<Shader>("res://Shaders/palette_swap.gdshader");
        _multiMeshShader = GD.Load<Shader>("res://Shaders/multimesh_atlas.gdshader");

        Texture2D terrainAtlas = _textures.LoadPlaceholder("terrain_tiles_16x16_placeholder.png");
        Texture2D humanoidAtlas = _textures.LoadPlaceholder("humanoid_modular_32x32_placeholder.png");
        Texture2D creatureAtlas = _textures.LoadPlaceholder("creatures_buildings_placeholder.png");
        Texture2D plantsAtlas = _textures.LoadPlaceholder("plants_flora_placeholder.png");
        Texture2D uiIconsAtlas = _textures.LoadPlaceholder("vfx_ui_icons_placeholder.png");
        Texture2D vfxEffectsAtlas = _textures.LoadPlaceholder("vfx_effects_16x16_placeholder.png");

        BuildTerrainBatch(terrainAtlas);
        BuildPropsBatch(plantsAtlas, creatureAtlas);
        BuildModularShowcase(humanoidAtlas, vfxEffectsAtlas);

        _benchmark = new SpriteRenderBenchmark { Name = "SpriteRenderBenchmark", ZIndex = 2 };
        AddChild(_benchmark);
        _benchmark.Initialize(humanoidAtlas, creatureAtlas, _multiMeshShader);

        _entities = MockWorldFixture.BuildShowcaseAndStressEntities(100);
        _benchmark.SetEntities(_entities);

        _camera = new SandboxCamera2D { Name = "SandboxCamera2D" };
        AddChild(_camera);

        _godGhostPreview = new Sprite2D
        {
            Name = "GodGhostPreview",
            Texture = uiIconsAtlas,
            RegionEnabled = true,
            RegionRect = new Rect2(0, 16, 16, 16),
            Modulate = new Color(1f, 1f, 1f, 0.65f),
            Visible = false,
            ZIndex = 20
        };
        AddChild(_godGhostPreview);

        _hud = new SandboxHud { Name = "SandboxHud" };
        AddChild(_hud);
        _hud.BuildUi(MockWorldFixture.BuildMockEvents(), MockWorldFixture.BuildMockNpcDetails());
        WireHudEvents();

        if (ShouldRunAutomatedTests())
        {
            int exitCode = RunAutomatedSandboxTests(
                terrainAtlas, humanoidAtlas, creatureAtlas, plantsAtlas, uiIconsAtlas, vfxEffectsAtlas);
            GetTree().Quit(exitCode);
            return;
        }

        _screenshotPath = GetScreenshotArg();
        _vulkanDrawCallBench = OS.GetCmdlineUserArgs().Contains("--bench-vulkan")
            || OS.GetCmdlineArgs().Contains("--bench-vulkan");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!_orbitPaused)
        {
            _elapsedSec += dt * _speedScale;
        }

        if (_camera.FollowedEntityId.HasValue)
        {
            var target = _entities.FirstOrDefault(e => e.Id == _camera.FollowedEntityId.Value);
            if (target != null)
            {
                Vector2 pos = SpriteRenderBenchmark.ResolvePreviewPos(target, _elapsedSec, _orbitPaused, _speedScale);
                _camera.UpdateFollow(pos, dt);
            }
        }

        if (_hud.CurrentMode == SandboxUiMode.God)
        {
            Vector2 viewportSize = GetViewportRect().Size;
            Vector2 worldMouse = _camera.ScreenToWorld(GetViewport().GetMousePosition(), viewportSize);
            Vector2 snapped = new Vector2(
                MathF.Floor(worldMouse.X / 16f) * 16f + 8f,
                MathF.Floor(worldMouse.Y / 16f) * 16f + 8f);
            _godGhostPreview.Visible = true;
            _godGhostPreview.Position = snapped;
        }
        else
        {
            _godGhostPreview.Visible = false;
        }

        Rect2 visibleRect = _camera.GetVisibleWorldRect(GetViewportRect().Size, 96f);
        var metrics = _benchmark.UpdateRender(visibleRect, _camera.Zoom.X, _elapsedSec, _orbitPaused, _speedScale);
        _hud.UpdateMetrics(metrics, (float)Engine.GetFramesPerSecond(), _camera.Zoom.X, _camera.Position);

        if (_vulkanDrawCallBench)
        {
            StepVulkanDrawCallBenchmark();
            return;
        }

        if (_screenshotPath != null)
        {
            _screenshotWarmupFrames--;
            if (_screenshotWarmupFrames <= 0)
            {
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(_screenshotPath);
                ulong drawCalls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                ulong objectsInFrame = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
                GD.Print($"[SCREENSHOT] Saved={_screenshotPath} | DrawCalls={drawCalls} | ObjectsInFrame={objectsInFrame} | FPS={Engine.GetFramesPerSecond():0.0}");
                _screenshotPath = null;
                GetTree().Quit(0);
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Vector2 worldPos = _camera.ScreenToWorld(mb.Position, GetViewportRect().Size);
            if (_hud.CurrentMode == SandboxUiMode.God)
            {
                PlaceMockGodPreviewMarker(worldPos);
            }
            else
            {
                SelectNearestMockEntity(worldPos);
            }
        }
    }

    private void WireHudEvents()
    {
        _hud.RenderModeRequested += mode => _benchmark.RequestedMode = mode;
        _hud.StressCountRequested += count =>
        {
            _entities = MockWorldFixture.BuildShowcaseAndStressEntities(count);
            _benchmark.SetEntities(_entities);
        };
        _hud.ZoomPresetRequested += zoom =>
        {
            _camera.ZoomAtScreenPoint(zoom, GetViewportRect().Size * 0.5f, GetViewportRect().Size);
        };
        _hud.FollowEntityRequested += id => _camera.StartFollow(id);
        _hud.FocusEventRequested += (pos, entityId) =>
        {
            _camera.StopFollow();
            _camera.Position = pos;
            if (entityId.HasValue)
            {
                _camera.StartFollow(entityId.Value);
            }
        };
        _hud.PreviewPlaybackChanged += (paused, speed) =>
        {
            _orbitPaused = paused;
            _speedScale = speed;
        };
    }

    private void SelectNearestMockEntity(Vector2 worldPos)
    {
        MockEntityVisual? best = null;
        float bestDistSq = 28f * 28f;
        for (int i = 0; i < _entities.Count; i++)
        {
            Vector2 pos = SpriteRenderBenchmark.ResolvePreviewPos(_entities[i], _elapsedSec, _orbitPaused, _speedScale);
            float d2 = pos.DistanceSquaredTo(worldPos);
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                best = _entities[i];
            }
        }

        if (best != null)
        {
            _hud.SelectNpc(best.Id);
        }
    }

    private void PlaceMockGodPreviewMarker(Vector2 worldPos)
    {
        Vector2 snapped = new Vector2(
            MathF.Floor(worldPos.X / 16f) * 16f + 8f,
            MathF.Floor(worldPos.Y / 16f) * 16f + 8f);

        int id = _nextMockGodId++;
        MockSpeciesKind species = _hud.ActiveBrush switch
        {
            MockGodBrush.SpawnDemon => MockSpeciesKind.Demon,
            MockGodBrush.SpawnDragon => MockSpeciesKind.TrueDragon,
            _ => MockSpeciesKind.Human
        };

        if (_hud.ActiveBrush == MockGodBrush.PlaceBerryBush)
        {
            _hud.AppendMockEvent(new MockEventItem(
                999, "God", $"[MOCK PREVIEW] Placed Berry Bush visual at ({snapped.X:0},{snapped.Y:0})", snapped, null));
            return;
        }

        _entities.Add(new MockEntityVisual(
            id,
            $"GodPreview {species} #{id}",
            species,
            MockProfession.Villager,
            MockLifeState.Alive,
            snapped,
            (uint)id,
            "Mock God Preview",
            IsGodPreviewMarker: true));
        _benchmark.SetEntities(_entities);
        _hud.AppendMockEvent(new MockEventItem(
            999, "God", $"[MOCK PREVIEW] Dropped {species} visual marker at ({snapped.X:0},{snapped.Y:0})", snapped, id));
    }

    private void BuildTerrainBatch(Texture2D terrainAtlas)
    {
        var grid = MockWorldFixture.BuildMockTerrainGrid();
        int totalTiles = MockWorldFixture.MapTilesX * MockWorldFixture.MapTilesY;

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = SpriteRenderBenchmark.CreateTopDownQuadMesh(16f, 16f),
            InstanceCount = totalTiles,
            VisibleInstanceCount = totalTiles
        };

        int idx = 0;
        for (int y = 0; y < MockWorldFixture.MapTilesY; y++)
        {
            for (int x = 0; x < MockWorldFixture.MapTilesX; x++)
            {
                int tile = (int)grid[x, y];
                mm.SetInstanceTransform2D(idx, new Transform2D(0f, new Vector2(x * 16f + 8f, y * 16f + 8f)));
                mm.SetInstanceColor(idx, Colors.White);
                mm.SetInstanceCustomData(idx, new Color(tile / 8f, 0f, 1f / 8f, 1f));
                idx++;
            }
        }

        AddChild(new MultiMeshInstance2D
        {
            Name = "TerrainTileBatch",
            Texture = terrainAtlas,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = new ShaderMaterial { Shader = _multiMeshShader },
            Multimesh = mm,
            ZIndex = -10
        });
    }

    private void BuildPropsBatch(Texture2D plantsAtlas, Texture2D buildingsAtlas)
    {
        _props = MockWorldFixture.BuildMockProps();
        var plantProps = _props.Where(p => p.Kind <= MockPropKind.DeadStump).ToList();
        var bldgProps = _props.Where(p => p.Kind > MockPropKind.DeadStump).ToList();

        var pMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = SpriteRenderBenchmark.CreateTopDownQuadMesh(32f, 32f),
            InstanceCount = Math.Max(1, plantProps.Count),
            VisibleInstanceCount = plantProps.Count
        };
        for (int i = 0; i < plantProps.Count; i++)
        {
            int slot = (int)plantProps[i].Kind;
            int col = slot % 4;
            int row = slot / 4;
            pMesh.SetInstanceTransform2D(i, new Transform2D(0f, plantProps[i].Position));
            pMesh.SetInstanceColor(i, Colors.White);
            pMesh.SetInstanceCustomData(i, new Color(col / 4f, row / 2f, 0.25f, 0.5f));
        }

        AddChild(new MultiMeshInstance2D
        {
            Name = "PlantsMultiMesh",
            Texture = plantsAtlas,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = new ShaderMaterial { Shader = _multiMeshShader },
            Multimesh = pMesh,
            ZIndex = -1
        });

        var bMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = SpriteRenderBenchmark.CreateTopDownQuadMesh(32f, 32f),
            InstanceCount = Math.Max(1, bldgProps.Count),
            VisibleInstanceCount = bldgProps.Count
        };
        for (int i = 0; i < bldgProps.Count; i++)
        {
            (int col, int row) = bldgProps[i].Kind switch
            {
                MockPropKind.House => (3, 0),
                MockPropKind.Storage => (1, 1),
                MockPropKind.MarketStall => (2, 1),
                _ => (3, 1)
            };
            bMesh.SetInstanceTransform2D(i, new Transform2D(0f, bldgProps[i].Position));
            bMesh.SetInstanceColor(i, Colors.White);
            bMesh.SetInstanceCustomData(i, new Color(col / 8f, row / 2f, 32f / 256f, 0.5f));
        }

        AddChild(new MultiMeshInstance2D
        {
            Name = "BuildingsMultiMesh",
            Texture = buildingsAtlas,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = new ShaderMaterial { Shader = _multiMeshShader },
            Multimesh = bMesh,
            ZIndex = -1
        });
    }

    private void BuildModularShowcase(Texture2D humanoidAtlas, Texture2D vfxEffectsAtlas)
    {
        _showcaseRoot = new Node2D { Name = "ModularShowcaseRow", ZIndex = 4 };
        AddChild(_showcaseRoot);

        var samples = new[]
        {
            new MockEntityVisual(201, "Sample Human Villager", MockSpeciesKind.Human, MockProfession.Villager, MockLifeState.Alive, new Vector2(26 * 16f, 20 * 16f), 0x01u, "Showcase"),
            new MockEntityVisual(202, "Sample Human Farmer", MockSpeciesKind.Human, MockProfession.Farmer, MockLifeState.Alive, new Vector2(28 * 16f, 20 * 16f), 0x02u, "Showcase"),
            new MockEntityVisual(203, "Sample Human Guard", MockSpeciesKind.Human, MockProfession.Guard, MockLifeState.Alive, new Vector2(30 * 16f, 20 * 16f), 0x03u, "Showcase"),
            new MockEntityVisual(204, "Sample Demon Mage", MockSpeciesKind.Demon, MockProfession.Mage, MockLifeState.Alive, new Vector2(32 * 16f, 20 * 16f), 0x04u, "Showcase"),
            new MockEntityVisual(205, "Sample Dead Corpse", MockSpeciesKind.Human, MockProfession.Villager, MockLifeState.Dead, new Vector2(34 * 16f, 20 * 16f), 0x05u, "Showcase")
        };

        for (int i = 0; i < samples.Length; i++)
        {
            var rig = new ModularCharacterPreview
            {
                Name = $"ShowcaseRig_{i}",
                Position = samples[i].BasePosition
            };
            rig.Initialize(humanoidAtlas, _paletteShader);
            rig.ApplyMockVisual(samples[i], isSelected: i == 0, pose: i == 2 ? MockAnimPose.WorkAttack : MockAnimPose.Idle);
            _showcaseRoot.AddChild(rig);
            _showcaseRigs.Add(rig);
        }

        // Visual Combat/Magic VFX sample sprites using vfx_effects_16x16_placeholder.png.
        _showcaseRoot.AddChild(new Sprite2D
        {
            Name = "SampleMagicAuraVfx",
            Texture = vfxEffectsAtlas,
            RegionEnabled = true,
            RegionRect = new Rect2(96, 16, 16, 16),
            TextureFilter = TextureFilterEnum.Nearest,
            Position = new Vector2(32 * 16f, 20 * 16f - 4f),
            ZIndex = 5
        });
        _showcaseRoot.AddChild(new Sprite2D
        {
            Name = "SampleArcaneBoltVfx",
            Texture = vfxEffectsAtlas,
            RegionEnabled = true,
            RegionRect = new Rect2(80, 0, 16, 16),
            TextureFilter = TextureFilterEnum.Nearest,
            Position = new Vector2(32 * 16f + 12f, 20 * 16f - 14f),
            ZIndex = 6
        });
    }

    private void StepVulkanDrawCallBenchmark()
    {
        if (_vulkanBenchFrames == 0)
        {
            _camera.Position = new Vector2(1024f, 1024f);
            _camera.SetZoomScalar(0.5f);
            if (_entities.Count != 5000)
            {
                _entities = MockWorldFixture.BuildShowcaseAndStressEntities(5000);
                _benchmark.SetEntities(_entities);
            }
            _benchmark.RequestedMode = _vulkanBenchStage == 0
                ? RenderOptimizationMode.MultiMeshBulkBuffer
                : RenderOptimizationMode.NaiveNodes;
        }

        _vulkanBenchFrames++;
        if (_vulkanBenchFrames >= 10)
        {
            ulong drawCalls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            ulong objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
            var m = _benchmark.LastMetrics;
            GD.Print($"[VULKAN_BENCH] N={m.TotalEntities} | Mode={m.ActiveStrategy} | Visible={m.VisibleEntities} | ActiveNodes={m.ActiveSpriteNodes} | UpdateMs={m.UpdateMs:0.000} | DrawCalls={drawCalls} | ObjectsInFrame={objects} | FPS={Engine.GetFramesPerSecond():0.0}");
            _vulkanBenchStage++;
            _vulkanBenchFrames = 0;
            if (_vulkanBenchStage >= 2)
            {
                GetTree().Quit(0);
            }
        }
    }

    private static bool ShouldRunAutomatedTests()
    {
        return OS.GetCmdlineUserArgs().Contains("--run-sandbox-tests")
            || OS.GetCmdlineArgs().Contains("--run-sandbox-tests");
    }

    private static string? GetScreenshotArg()
    {
        foreach (string[] args in new[] { OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs() })
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--capture-screenshot")
                {
                    return args[i + 1];
                }
            }
        }
        return null;
    }

    private int RunAutomatedSandboxTests(
        Texture2D terrain,
        Texture2D humanoid,
        Texture2D creatures,
        Texture2D plants,
        Texture2D uiIcons,
        Texture2D vfxEffects)
    {
        GD.Print("=== PresentationTest Automated Verification ===");

        // 1. Verify all 6 placeholder atlas dimensions.
        AssertTrue(terrain.GetWidth() == 128 && terrain.GetHeight() == 16, "Terrain atlas size 128x16");
        AssertTrue(humanoid.GetWidth() == 256 && humanoid.GetHeight() == 64, "Humanoid atlas size 256x64");
        AssertTrue(creatures.GetWidth() == 256 && creatures.GetHeight() == 64, "Creatures/Buildings atlas size 256x64");
        AssertTrue(plants.GetWidth() == 128 && plants.GetHeight() == 64, "Plants atlas size 128x64");
        AssertTrue(uiIcons.GetWidth() == 128 && uiIcons.GetHeight() == 32, "UI Icons atlas size 128x32");
        AssertTrue(vfxEffects.GetWidth() == 128 && vfxEffects.GetHeight() == 32, "VFX Effects atlas size 128x32");
        GD.Print("[PASS] All 6 placeholder atlases loaded with expected pixel dimensions.");

        // 2. Verify Mock Event Gap UI recovery & Receipt Unknown semantics (N3 preview).
        var mockPending = new List<MockCommandReceiptPreview>
        {
            new(1, 10, "Mock God Spawn", MockReceiptState.Queued),
            new(2, 500, "Future Mock Command", MockReceiptState.Queued)
        };
        var mockMilestones = new List<MockEventItem>
        {
            new(25, "Combat", "Sample milestone inside gap", Vector2.Zero, 104),
            new(49, "Life", "Sample milestone after gap", Vector2.Zero, 101)
        };
        var gapResult = MockWorldFixture.SimulateMockGapUiRecovery(0, 47, mockMilestones, mockPending, latestCompletedTick: 50);
        AssertTrue(gapResult.GapStart == 1 && gapResult.GapEnd == 46, "Gap interval [1,46]");
        AssertTrue(gapResult.NewEventCursor == 46, "Gap resets cursor to OldestAvailable - 1");
        AssertTrue(gapResult.RecoveredHistoryMilestones == 1, "History catch-up filters to lost range");
        AssertTrue(mockPending[0].State == MockReceiptState.OutcomeUnknown, "Stale queued receipt becomes OutcomeUnknown");
        AssertTrue(mockPending[1].State == MockReceiptState.Queued, "Future queued receipt stays Queued");
        _hud.SimulateMockEventGapNotice();
        GD.Print("[PASS] Mock Event Gap UI recovery, ReadHistory catch-up filter, and Receipt Unknown verified.");

        // 2. Verify Camera cursor-centered zoom invariant & frustum culling.
        var viewport = new Vector2(1280f, 720f);
        var cursor = new Vector2(320f, 180f);
        _camera.Position = new Vector2(512f, 512f);
        _camera.SetZoomScalar(1.0f);
        Vector2 worldBefore = _camera.ScreenToWorld(cursor, viewport);
        _camera.ZoomAtScreenPoint(2.5f, cursor, viewport);
        Vector2 worldAfterZoomIn = _camera.ScreenToWorld(cursor, viewport);
        _camera.ZoomAtScreenPoint(0.25f, cursor, viewport);
        Vector2 worldAfterZoomOut = _camera.ScreenToWorld(cursor, viewport);

        AssertTrue(worldBefore.DistanceTo(worldAfterZoomIn) < 0.01f, "Zoom-in preserves world point under cursor");
        AssertTrue(worldBefore.DistanceTo(worldAfterZoomOut) < 0.01f, "Zoom-out preserves world point under cursor");
        Rect2 rect = _camera.GetVisibleWorldRect(viewport, 0f);
        AssertTrue(rect.HasPoint(_camera.Position), "Visible rect contains camera center");
        AssertTrue(!rect.HasPoint(_camera.Position + new Vector2(10000f, 10000f)), "Visible rect excludes far off-screen");
        GD.Print($"[PASS] Camera zoom-to-cursor drift < 0.01px; zoom=0.25x visible rect={rect}.");

        // 3. Verify Modular Character layers & pose transitions.
        AssertTrue(_showcaseRigs.Count == 5, "5 modular character showcase rigs created");
        AssertTrue(_showcaseRigs[0].GetChildCount() == 7, "Modular rig has 7 ordered layers");
        AssertTrue(!_showcaseRigs[0].GetNode<Sprite2D>("RaceFeatures").Visible, "Human rig hides Demon features");
        AssertTrue(_showcaseRigs[3].GetNode<Sprite2D>("RaceFeatures").Visible, "Demon rig shows horns/tail features");
        AssertTrue(!_showcaseRigs[4].GetNode<Sprite2D>("Shadow").Visible, "Dead pose hides ground shadow");
        var sleepTestEntity = new MockEntityVisual(299, "SleepPoseCheck", MockSpeciesKind.Human, MockProfession.Villager, MockLifeState.Alive, Vector2.Zero, 1u, "Sleep");
        _showcaseRigs[0].ApplyMockVisual(sleepTestEntity, isSelected: false, pose: MockAnimPose.Sleep);
        AssertTrue(_showcaseRigs[0].GetNode<Sprite2D>("BodyBase").RegionRect.Position == new Vector2(64, 32), "Sleep pose switches to sleep atlas frame");
        _showcaseRigs[0].ApplyMockVisual(sleepTestEntity, isSelected: true, pose: MockAnimPose.Walk);
        AssertTrue(_showcaseRigs[0].GetNode<Sprite2D>("BodyBase").RegionRect.Position == new Vector2(0, 32), "Walk pose switches to walk atlas frame");
        GD.Print("[PASS] Modular Character layer rules & Sleep/Walk/Dead poses verified.");

        // 4. Sprite batching & optimization benchmark across 100, 1000, 5000 mock sprites.
        _camera.Position = new Vector2(1024f, 1024f);
        _camera.SetZoomScalar(0.5f);
        Rect2 benchRect = _camera.GetVisibleWorldRect(viewport, 64f);

        foreach (int count in new[] { 100, 1000, 5000 })
        {
            var stress = MockWorldFixture.BuildShowcaseAndStressEntities(count);
            _benchmark.SetEntities(stress);

            foreach (var mode in new[]
                     {
                         RenderOptimizationMode.MultiMeshBulkBuffer,
                         RenderOptimizationMode.MultiMeshBatch,
                         RenderOptimizationMode.CulledNodePool,
                         RenderOptimizationMode.NaiveNodes
                     })
            {
                _benchmark.RequestedMode = mode;
                // Warmup + 10 measured frames.
                _benchmark.UpdateRender(benchRect, _camera.Zoom.X, 0f, true, 1f);
                double sumMs = 0;
                double maxMs = 0;
                RenderBenchmarkMetrics last = default;
                for (int iter = 0; iter < 10; iter++)
                {
                    last = _benchmark.UpdateRender(benchRect, _camera.Zoom.X, iter * 0.016f, false, 1f);
                    sumMs += last.UpdateMs;
                    if (last.UpdateMs > maxMs) maxMs = last.UpdateMs;
                }
                GD.Print($"[BENCH] N={count,-4} | Mode={mode,-14} | Visible={last.VisibleEntities,-4} | ActiveNodes={last.ActiveSpriteNodes,-4} | AvgMs={sumMs / 10.0:0.000} | MaxMs={maxMs:0.000} | ManagedMB={last.ManagedBytes / (1024.0 * 1024.0):0.00}");
            }
        }

        // Verify MultiMeshBatch cleans up naive nodes.
        _benchmark.RequestedMode = RenderOptimizationMode.MultiMeshBatch;
        var cleanMetrics = _benchmark.UpdateRender(benchRect, _camera.Zoom.X, 0f, true, 1f);
        AssertTrue(cleanMetrics.ActiveSpriteNodes == 2, "MultiMeshBatch uses only 2 batch nodes regardless of entity count");

        GD.Print("[SANDBOX_TESTS_PASSED]");
        return 0;
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Sandbox assertion failed: {label}");
        }
    }
}
