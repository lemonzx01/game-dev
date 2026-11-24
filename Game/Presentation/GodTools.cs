using System;
using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// God tools (T018 playable build): G toggles God mode; Q = spawn tool, W = terrain brush, Tab cycles the species or
// terrain; right-click applies the tool on the tile under the mouse. F5 quick-saves, F9 quick-loads (any mode).
// Every action is an ordinary validated God command (or a save/load between frames), never a direct world edit.
public partial class GodTools : Node2D
{
    public enum Tool { Spawn, Terrain, Settlement }

    [Export] public NodePath BridgePath = "../SimulationBridge";
    [Export] public NodePath FeedPath = "../UI/Feed";
    [Export] public NodePath LabelPath = "../UI/GodLabel";
    [Export] public float CellSize = 16f;

    private static readonly int[] SpeciesCycle = [SpeciesCatalog.Deer, SpeciesCatalog.Sheep, SpeciesCatalog.Rabbit, SpeciesCatalog.Wolf, SpeciesCatalog.Human, SpeciesCatalog.Demon];
    private static readonly Faction[] FactionCycle = [Faction.Human, Faction.Demon];
    private static readonly Terrain[] TerrainCycle = [Terrain.Grass, Terrain.Forest, Terrain.Water, Terrain.Mountain, Terrain.Snow];

    private SimulationBridge bridge = null!;
    private EventFeed? feed;
    private Label? label;

    public bool Enabled { get; private set; }
    public Tool Current { get; private set; } = Tool.Spawn;
    public int SpeciesIndex { get; private set; }
    public int TerrainIndex { get; private set; } = 2; // water: an obvious first brush
    public int FactionIndex { get; private set; }
    private int townsFounded;
    public string? SavePathOverride { get; set; }      // tests use a temp file; the game uses user://saves

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        feed = GetNodeOrNull<EventFeed>(FeedPath);
        label = GetNodeOrNull<Label>(LabelPath);
        UpdateLabel();
    }

    public void Toggle() { Enabled = !Enabled; UpdateLabel(); }

    public void Select(Tool tool) { Current = tool; UpdateLabel(); }

    public void CycleOption()
    {
        if (Current == Tool.Spawn) SpeciesIndex = (SpeciesIndex + 1) % SpeciesCycle.Length;
        else if (Current == Tool.Terrain) TerrainIndex = (TerrainIndex + 1) % TerrainCycle.Length;
        else FactionIndex = (FactionIndex + 1) % FactionCycle.Length;
        UpdateLabel();
    }

    public int CurrentSpecies => SpeciesCycle[SpeciesIndex];
    public Terrain CurrentTerrain => TerrainCycle[TerrainIndex];
    public Faction CurrentFaction => FactionCycle[FactionIndex];

    /// <summary>Applies the current tool to a tile. Returns the receipt (Queued or Rejected at submission).</summary>
    public CommandReceipt? ApplyAt(int x, int y)
    {
        if (!Enabled) return null;
        var receipt = Current switch
        {
            Tool.Spawn => bridge.SpawnAt(x, y, CurrentSpecies),
            Tool.Terrain => bridge.PaintTerrain(x, y, CurrentTerrain),
            _ => bridge.FoundAt(x, y, CurrentFaction, $"{CurrentFaction} town {++townsFounded}"),
        };
        if (receipt.Status != "Queued") feed?.Note($"God command refused: {receipt.Reason}");
        return receipt;
    }

    public void QuickSave()
    {
        string path = SavePathOverride ?? SimulationBridge.QuickSavePath;
        try
        {
            bridge.SaveTo(path);
            feed?.Note($"Saved tick {bridge.Snapshot.Tick}");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            feed?.Note($"Save failed: {e.Message}");
        }
    }

    public void QuickLoad()
    {
        string path = SavePathOverride ?? SimulationBridge.QuickSavePath;
        try
        {
            bridge.LoadFrom(path);
            feed?.Note($"Loaded tick {bridge.Snapshot.Tick}");
        }
        catch (Exception e) when (e is SaveFormatException or System.IO.IOException or UnauthorizedAccessException)
        {
            feed?.Note($"Load failed (world unchanged): {e.Message}");
        }
    }

    private void UpdateLabel()
    {
        if (label is null) return;
        label.Text = !Enabled
            ? "G: God mode   F5: save   F9: load"
            : Current switch
            {
                Tool.Spawn => $"GOD  spawn {bridge?.SpeciesName(CurrentSpecies)}  (right-click place, Tab species, W terrain, E town)",
                Tool.Terrain => $"GOD  terrain {CurrentTerrain}  (right-click paint, Tab terrain, Q spawn, E town)",
                _ => $"GOD  found {CurrentFaction} town  (right-click found, Tab faction, Q spawn, W terrain)",
            };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.G: Toggle(); break;
                    case Key.F5: QuickSave(); break;
                    case Key.F9: QuickLoad(); break;
                    case Key.Q when Enabled: Select(Tool.Spawn); break;
                    case Key.W when Enabled: Select(Tool.Terrain); break;
                    case Key.E when Enabled: Select(Tool.Settlement); break;
                    case Key.Tab when Enabled: CycleOption(); break;
                    default: return;
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when Enabled:
                var p = GetGlobalMousePosition() / CellSize;
                int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y);
                if (x >= 0 && y >= 0 && x < bridge.Map.Width && y < bridge.Map.Height) ApplyAt(x, y);
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}
