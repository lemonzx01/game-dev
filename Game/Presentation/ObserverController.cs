using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Observer controls (T008): pause/resume (Space), single step while paused (Period), speed 1/2/4/8 (keys 1-4),
// click to select the nearest entity, F to follow the selection, Escape to clear. Everything here is host-side:
// it only changes the HostClock, the camera and which ID the panels show. It never submits commands.
public partial class ObserverController : Node2D
{
    [Export] public NodePath BridgePath = "../SimulationBridge";
    [Export] public NodePath CameraPath = "../Camera2D";
    [Export] public float CellSize = 16f;
    [Export] public float SelectRadiusCells = 1.5f;

    private SimulationBridge bridge = null!;
    private Camera2D camera = null!;

    public long SelectedId { get; private set; }
    public bool Following { get; private set; }

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        camera = GetNode<Camera2D>(CameraPath);
    }

    public void TogglePause() => bridge.Clock.Paused = !bridge.Clock.Paused;

    public void StepOnce()
    {
        if (bridge.Clock.Paused) bridge.Clock.RequestSingleStep();
    }

    public void SetSpeed(int speed) => bridge.Clock.Speed = speed;

    public void Select(long id)
    {
        SelectedId = id;
        if (id == 0) Following = false;
    }

    public void SetFollow(bool follow) => Following = follow && SelectedId != 0;

    /// <summary>Nearest snapshot row to a world position within the select radius (presentation-side pick).</summary>
    public long PickAt(Vector2 worldPosition)
    {
        long best = 0;
        float bestDistance = SelectRadiusCells * CellSize;
        foreach (var chunk in bridge.Snapshot.Chunks)
            foreach (EntityRow row in chunk)
            {
                var center = new Vector2(row.X + 0.5f, row.Y + 0.5f) * CellSize;
                float d = center.DistanceTo(worldPosition);
                if (d < bestDistance || (d == bestDistance && row.Id.Value < best)) { best = row.Id.Value; bestDistance = d; }
            }
        return best;
    }

    /// <summary>Selected entity's row in the current snapshot, if it is still shown (dead rows show on their death tick only).</summary>
    public EntityRow? SelectedRow()
    {
        if (SelectedId == 0) return null;
        foreach (var chunk in bridge.Snapshot.Chunks)
            foreach (EntityRow row in chunk)
                if (row.Id.Value == SelectedId) return row;
        return null;
    }

    private int seenGeneration;

    public override void _Process(double delta)
    {
        if (seenGeneration != bridge.WorldGeneration) { seenGeneration = bridge.WorldGeneration; Select(0); } // world replaced by a load
        if (Following && SelectedRow() is { } row)
            camera.Position = new Vector2(row.X + 0.5f, row.Y + 0.5f) * CellSize;
        else if (Following)
            Following = false; // the followed animal died or left the view model: stop, keep the selection for history
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.Space: TogglePause(); break;
                    case Key.Period: StepOnce(); break;
                    case Key.Key1: SetSpeed(1); break;
                    case Key.Key2: SetSpeed(2); break;
                    case Key.Key3: SetSpeed(4); break;
                    case Key.Key4: SetSpeed(8); break;
                    case Key.F: SetFollow(!Following); break;
                    case Key.Escape: Select(0); break;
                    default: return;
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Select(PickAt(GetGlobalMousePosition()));
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}
