using System;
using System.Collections.Generic;
using FantasyWorldSimulator.Sandbox.PresentationTest.MockData;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

public enum SandboxUiMode
{
    Observer,
    God
}

public enum MockGodBrush
{
    SpawnHuman,
    SpawnDemon,
    SpawnDragon,
    PlaceBerryBush
}

/// <summary>
/// Interactive UI/HUD prototype for Observer Mode, God Mode, NPC Inspector,
/// Event Feed, Camera Zoom presets, and Render Optimization switcher.
/// Purely bound to temporary Mock Data.
/// </summary>
public partial class SandboxHud : CanvasLayer
{
    public event Action<RenderOptimizationMode>? RenderModeRequested;
    public event Action<int>? StressCountRequested;
    public event Action<float>? ZoomPresetRequested;
    public event Action<int>? FollowEntityRequested;
    public event Action<Vector2, int?>? FocusEventRequested;
    public event Action<bool, float>? PreviewPlaybackChanged;
    public event Action? StepOnceRequested;
    public event Action<MockSeasonVisual>? SeasonChanged;
    public event Action<MockOverlayMode>? OverlayChanged;
    public event Action<Vector2>? MinimapPanRequested;

    private Label _statusLabel = null!;
    private Label _metricsLabel = null!;
    private Label _gapBannerLabel = null!;
    private Label _minimapInfoLabel = null!;
    private TextureRect _minimapRect = null!;
    private ReferenceRect _minimapViewportBox = null!;
    private VBoxContainer _inspectorContent = null!;
    private VBoxContainer _eventList = null!;
    private HBoxContainer _observerBar = null!;
    private HBoxContainer _godBar = null!;
    private Button _observerBtn = null!;
    private Button _godBtn = null!;
    private bool _backlogPreviewActive;
    private MockSeasonVisual _currentSeason = MockSeasonVisual.Spring;

    public SandboxUiMode CurrentMode { get; private set; } = SandboxUiMode.Observer;
    public MockGodBrush ActiveBrush { get; private set; } = MockGodBrush.SpawnHuman;
    public bool IsPaused { get; private set; }
    public float SpeedScale { get; private set; } = 1.0f;
    public int? SelectedEntityId { get; private set; }

    private readonly HashSet<int> _protagonistBookmarks = new() { 101, 108 };
    private string _eventFilter = "All";
    private List<MockEventItem> _events = new();
    private Dictionary<int, MockNpcDetail> _details = new();

    public void BuildUi(List<MockEventItem> initialEvents, Dictionary<int, MockNpcDetail> details, Texture2D minimapTex)
    {
        _events = initialEvents;
        _details = details;

        BuildTopBar();
        BuildEventFeedPanel();
        BuildMinimapPanel(minimapTex);
        BuildInspectorPanel();
        BuildBottomContextBar();

        SelectNpc(101);
        RefreshEvents();
    }

    public void UpdateMetrics(RenderBenchmarkMetrics metrics, float fps, float zoom, Vector2 camPos, int visChunks, int totalChunks, Rect2 visibleRect)
    {
        string backlogTag = _backlogPreviewActive ? " [BACKLOG DEMO]" : "";
        _metricsLabel.Text =
            $"FPS: {fps:0.0}{backlogTag} | LOD: {metrics.ActiveStrategy} | Visible: {metrics.VisibleEntities}/{metrics.TotalEntities} | " +
            $"SpriteNodes: {metrics.ActiveSpriteNodes} | Chunks: {visChunks}/{totalChunks} | Upd: {metrics.UpdateMs:0.00} ms | Zoom: {zoom:0.00}x";

        if (_minimapRect != null && _minimapViewportBox != null)
        {
            float mapWorld = MockWorldFixture.MapTilesX * MockWorldFixture.TileSize;
            float scale = 128f / mapWorld;
            _minimapViewportBox.Position = new Vector2(
                Mathf.Clamp(visibleRect.Position.X * scale, 0f, 124f),
                Mathf.Clamp(visibleRect.Position.Y * scale, 0f, 124f));
            _minimapViewportBox.Size = new Vector2(
                Mathf.Clamp(visibleRect.Size.X * scale, 6f, 128f),
                Mathf.Clamp(visibleRect.Size.Y * scale, 6f, 128f));
            _minimapInfoLabel.Text = $"Season: {_currentSeason} | Cam ({camPos.X:0},{camPos.Y:0})";
        }
    }

    public void SelectNpc(int entityId)
    {
        SelectedEntityId = entityId;
        RebuildInspector();
    }

    public void AppendMockEvent(MockEventItem item)
    {
        _events.Insert(0, item);
        if (_events.Count > 50)
        {
            _events.RemoveAt(_events.Count - 1);
        }
        RefreshEvents();
    }

    public void SimulateMockEventGapNotice()
    {
        var pending = new List<MockCommandReceiptPreview>
        {
            new(12, 105, "Mock SpawnEntity", MockReceiptState.Queued)
        };
        var recovery = MockWorldFixture.SimulateMockGapUiRecovery(
            currentEventCursor: 0,
            oldestAvailable: 47,
            mockHistoryMilestones: _events,
            pendingReceipts: pending,
            latestCompletedTick: 120);

        _gapBannerLabel.Visible = true;
        _gapBannerLabel.Text =
            $"[MOCK GAP UI] Missing [{recovery.GapStart}..{recovery.GapEnd}] -> Cursor={recovery.NewEventCursor} | " +
            $"Receipts Unknown={recovery.MarkedUnknownReceipts} | Snapshot Re-synced";
        AppendMockEvent(new MockEventItem(
            121, "God", $"[MOCK GAP RECOVERED] Cursor reset to {recovery.NewEventCursor}; pending receipt -> {pending[0].State}",
            new Vector2(33 * 16f, 26 * 16f), null));
    }

    private void BuildTopBar()
    {
        var topPanel = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetBottom = 94f
        };
        AddChild(topPanel);

        var outer = new VBoxContainer();
        topPanel.AddChild(outer);

        var row1 = new HBoxContainer();
        outer.AddChild(row1);

        _statusLabel = new Label
        {
            Text = "[SANDBOX MOCK] Day 12 · Spring · Y1 | "
        };
        row1.AddChild(_statusLabel);

        AddSpeedButton(row1, "Pause", true, 0f);
        AddSpeedButton(row1, "1x", false, 1f);
        AddSpeedButton(row1, "2x", false, 2f);
        AddSpeedButton(row1, "4x", false, 4f);
        var stepBtn = new Button { Text = "+1 Step" };
        stepBtn.Pressed += () =>
        {
            IsPaused = true;
            SpeedScale = 0f;
            PreviewPlaybackChanged?.Invoke(true, 0f);
            StepOnceRequested?.Invoke();
        };
        row1.AddChild(stepBtn);

        row1.AddChild(new VSeparator());

        _observerBtn = new Button { Text = "Observer", ToggleMode = true, ButtonPressed = true };
        _observerBtn.Pressed += () => SetUiMode(SandboxUiMode.Observer);
        row1.AddChild(_observerBtn);

        _godBtn = new Button { Text = "God (Mock)", ToggleMode = true, ButtonPressed = false };
        _godBtn.Pressed += () => SetUiMode(SandboxUiMode.God);
        row1.AddChild(_godBtn);

        row1.AddChild(new VSeparator());
        foreach (MockSeasonVisual s in Enum.GetValues<MockSeasonVisual>())
        {
            var sb = new Button { Text = s.ToString() };
            MockSeasonVisual captured = s;
            sb.Pressed += () =>
            {
                _currentSeason = captured;
                SeasonChanged?.Invoke(captured);
            };
            row1.AddChild(sb);
        }

        var row2 = new HBoxContainer();
        outer.AddChild(row2);

        row2.AddChild(new Label { Text = "LOD: " });
        AddStrategyButton(row2, "AutoLOD", RenderOptimizationMode.AutoLod);
        AddStrategyButton(row2, "BulkBatch", RenderOptimizationMode.MultiMeshBulkBuffer);
        AddStrategyButton(row2, "CulledPool", RenderOptimizationMode.CulledNodePool);
        AddStrategyButton(row2, "Naive", RenderOptimizationMode.NaiveNodes);

        row2.AddChild(new VSeparator());
        row2.AddChild(new Label { Text = "N: " });
        AddCountButton(row2, "100", 100);
        AddCountButton(row2, "1k", 1000);
        AddCountButton(row2, "5k", 5000);

        row2.AddChild(new VSeparator());
        row2.AddChild(new Label { Text = "Overlay: " });
        foreach (MockOverlayMode ov in Enum.GetValues<MockOverlayMode>())
        {
            var ob = new Button { Text = ov.ToString() };
            MockOverlayMode captured = ov;
            ob.Pressed += () => OverlayChanged?.Invoke(captured);
            row2.AddChild(ob);
        }

        var backlogBtn = new Button { Text = "Backlog", ToggleMode = true };
        backlogBtn.Pressed += () => _backlogPreviewActive = backlogBtn.ButtonPressed;
        row2.AddChild(backlogBtn);

        _metricsLabel = new Label { Text = "Initializing metrics..." };
        outer.AddChild(_metricsLabel);
    }

    private void BuildEventFeedPanel()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 0f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 8f, OffsetTop = 102f, OffsetRight = 320f, OffsetBottom = -224f
        };
        AddChild(panel);

        var box = new VBoxContainer();
        panel.AddChild(box);

        box.AddChild(new Label { Text = "Mock Event Feed (Click to Focus)" });

        var filters = new HBoxContainer();
        box.AddChild(filters);
        foreach (string cat in new[] { "All", "Life", "Combat", "God" })
        {
            var btn = new Button { Text = cat };
            string captured = cat;
            btn.Pressed += () =>
            {
                _eventFilter = captured;
                RefreshEvents();
            };
            filters.AddChild(btn);
        }

        var gapBtn = new Button { Text = "Test Gap UI" };
        gapBtn.Pressed += SimulateMockEventGapNotice;
        filters.AddChild(gapBtn);

        _gapBannerLabel = new Label
        {
            Text = string.Empty,
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        box.AddChild(_gapBannerLabel);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(scroll);

        _eventList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_eventList);
    }

    private void BuildMinimapPanel(Texture2D minimapTex)
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 0f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 8f, OffsetTop = -218f, OffsetRight = 320f, OffsetBottom = -60f
        };
        AddChild(panel);

        var row = new HBoxContainer();
        panel.AddChild(row);

        _minimapRect = new TextureRect
        {
            Texture = minimapTex,
            CustomMinimumSize = new Vector2(128, 128),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        };
        _minimapRect.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                float worldSize = MockWorldFixture.MapTilesX * MockWorldFixture.TileSize;
                Vector2 target = new Vector2(
                    Mathf.Clamp(mb.Position.X / 128f, 0f, 1f) * worldSize,
                    Mathf.Clamp(mb.Position.Y / 128f, 0f, 1f) * worldSize);
                MinimapPanRequested?.Invoke(target);
            }
        };
        row.AddChild(_minimapRect);

        _minimapViewportBox = new ReferenceRect
        {
            BorderColor = Colors.White,
            BorderWidth = 1.5f,
            EditorOnly = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _minimapRect.AddChild(_minimapViewportBox);

        var infoCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(infoCol);
        infoCol.AddChild(new Label { Text = "Minimap (Click)" });
        _minimapInfoLabel = new Label
        {
            Text = "Season: Spring",
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        infoCol.AddChild(_minimapInfoLabel);
    }

    private void BuildInspectorPanel()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = -368f, OffsetTop = 102f, OffsetRight = -8f, OffsetBottom = -64f
        };
        AddChild(panel);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panel.AddChild(scroll);

        _inspectorContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_inspectorContent);
    }

    private void BuildBottomContextBar()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetTop = -54f
        };
        AddChild(panel);

        var root = new HBoxContainer();
        panel.AddChild(root);

        _observerBar = new HBoxContainer();
        root.AddChild(_observerBar);
        _observerBar.AddChild(new Label { Text = "Protagonist Bookmarks (No Plot Armor): " });
        AddBookmarkQuickBtn(_observerBar, "Mira (Human)", 101);
        AddBookmarkQuickBtn(_observerBar, "Zarak (Demon)", 103);
        AddBookmarkQuickBtn(_observerBar, "Alden (Dead History)", 104);
        AddBookmarkQuickBtn(_observerBar, "Ignis Rex (Dragon)", 108);

        _godBar = new HBoxContainer { Visible = false };
        root.AddChild(_godBar);
        _godBar.AddChild(new Label { Text = "God Brush (Mock Visual Marker): " });
        AddBrushBtn(_godBar, "Spawn Human", MockGodBrush.SpawnHuman);
        AddBrushBtn(_godBar, "Spawn Demon", MockGodBrush.SpawnDemon);
        AddBrushBtn(_godBar, "Spawn Dragon", MockGodBrush.SpawnDragon);
        AddBrushBtn(_godBar, "Place Berry Bush", MockGodBrush.PlaceBerryBush);

        root.AddChild(new VSeparator());
        root.AddChild(new Label { Text = "Zoom: " });
        foreach (float z in new[] { 0.25f, 0.5f, 1.0f, 2.0f, 4.0f })
        {
            var zb = new Button { Text = $"{z:0.##}x" };
            float captured = z;
            zb.Pressed += () => ZoomPresetRequested?.Invoke(captured);
            root.AddChild(zb);
        }
    }

    private void RebuildInspector()
    {
        foreach (Node child in _inspectorContent.GetChildren())
        {
            child.QueueFree();
        }

        _inspectorContent.AddChild(new Label { Text = "=== NPC / Creature Inspector ===" });

        if (!SelectedEntityId.HasValue || !_details.TryGetValue(SelectedEntityId.Value, out var d))
        {
            _inspectorContent.AddChild(new Label
            {
                Text = $"Selected Mock ID: {SelectedEntityId?.ToString() ?? "None"}\n(Select Mira #101, Zarak #103, Alden #104 Dead, or Ignis #108 for full mock dossier)"
            });
            return;
        }

        _inspectorContent.AddChild(new Label { Text = $"{d.Name} (ID: {d.Id})" });
        _inspectorContent.AddChild(new Label { Text = $"{d.SpeciesLabel} | {d.LifeStageAge} | State: {d.LifeState}" });

        var actionsRow = new HBoxContainer();
        _inspectorContent.AddChild(actionsRow);
        var followBtn = new Button { Text = "Follow Camera" };
        followBtn.Pressed += () => FollowEntityRequested?.Invoke(d.Id);
        actionsRow.AddChild(followBtn);

        bool bookmarked = _protagonistBookmarks.Contains(d.Id);
        var markBtn = new Button { Text = bookmarked ? "Bookmarked (Protagonist)" : "Set Protagonist" };
        markBtn.Pressed += () =>
        {
            if (!_protagonistBookmarks.Add(d.Id))
            {
                _protagonistBookmarks.Remove(d.Id);
            }
            RebuildInspector();
        };
        actionsRow.AddChild(markBtn);

        if (d.DeathSummary != null)
        {
            _inspectorContent.AddChild(new Label
            {
                Text = $"[DEAD HISTORY]\n{d.DeathSummary}",
                AutowrapMode = TextServer.AutowrapMode.Word
            });
        }

        AddRatioBar("HP", d.HpRatio);
        AddRatioBar("Hunger", d.HungerRatio);
        AddRatioBar("Thirst", d.ThirstRatio);
        AddRatioBar("Energy", d.EnergyRatio);

        _inspectorContent.AddChild(new Label
        {
            Text = $"Action: {d.CurrentAction}\n{d.DecisionReason}",
            AutowrapMode = TextServer.AutowrapMode.Word
        });
        _inspectorContent.AddChild(new Label
        {
            Text = $"Traits: {string.Join(", ", d.Traits)}\nEmotions: {string.Join(", ", d.Emotions)}",
            AutowrapMode = TextServer.AutowrapMode.Word
        });
        _inspectorContent.AddChild(new Label
        {
            Text = $"Skills: {string.Join(", ", d.Skills)}\nSocial: {string.Join(" | ", d.Relationships)}",
            AutowrapMode = TextServer.AutowrapMode.Word
        });
        _inspectorContent.AddChild(new Label
        {
            Text = $"Memory:\n- {string.Join("\n- ", d.Memories)}",
            AutowrapMode = TextServer.AutowrapMode.Word
        });
    }

    private void AddRatioBar(string label, float ratio)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = $"{label,-7}", CustomMinimumSize = new Vector2(72, 0) });
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = ratio * 100f,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        row.AddChild(bar);
        _inspectorContent.AddChild(row);
    }

    private void RefreshEvents()
    {
        foreach (Node child in _eventList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var ev in _events)
        {
            if (_eventFilter != "All" && !string.Equals(ev.Category, _eventFilter, StringComparison.Ordinal))
            {
                continue;
            }

            var btn = new Button
            {
                Text = $"[T{ev.TickLabel}][{ev.Category}] {ev.Message}",
                Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.Word,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var captured = ev;
            btn.Pressed += () =>
            {
                if (captured.RelatedEntityId.HasValue)
                {
                    SelectNpc(captured.RelatedEntityId.Value);
                }
                FocusEventRequested?.Invoke(captured.WorldPosition, captured.RelatedEntityId);
            };
            _eventList.AddChild(btn);
        }
    }

    private void SetUiMode(SandboxUiMode mode)
    {
        CurrentMode = mode;
        _observerBtn.ButtonPressed = (mode == SandboxUiMode.Observer);
        _godBtn.ButtonPressed = (mode == SandboxUiMode.God);
        _observerBar.Visible = (mode == SandboxUiMode.Observer);
        _godBar.Visible = (mode == SandboxUiMode.God);
    }

    private void AddSpeedButton(Container parent, string label, bool pause, float speed)
    {
        var btn = new Button { Text = label };
        btn.Pressed += () =>
        {
            IsPaused = pause;
            SpeedScale = pause ? 0f : speed;
            PreviewPlaybackChanged?.Invoke(IsPaused, SpeedScale);
        };
        parent.AddChild(btn);
    }

    private void AddStrategyButton(Container parent, string label, RenderOptimizationMode mode)
    {
        var btn = new Button { Text = label };
        btn.Pressed += () => RenderModeRequested?.Invoke(mode);
        parent.AddChild(btn);
    }

    private void AddCountButton(Container parent, string label, int count)
    {
        var btn = new Button { Text = label };
        btn.Pressed += () => StressCountRequested?.Invoke(count);
        parent.AddChild(btn);
    }

    private void AddBookmarkQuickBtn(Container parent, string label, int entityId)
    {
        var btn = new Button { Text = label };
        btn.Pressed += () =>
        {
            SelectNpc(entityId);
            FollowEntityRequested?.Invoke(entityId);
        };
        parent.AddChild(btn);
    }

    private void AddBrushBtn(Container parent, string label, MockGodBrush brush)
    {
        var btn = new Button { Text = label };
        btn.Pressed += () => ActiveBrush = brush;
        parent.AddChild(btn);
    }
}
