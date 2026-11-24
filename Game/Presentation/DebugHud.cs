using Game.Integration;
using Godot;

namespace Game.Presentation;

// Top-left status: calendar, clock, speed and pause state from the bridge. F3 toggles technical details
// (tick, entity count, event cursor, backlog, gaps). Reads only; never feeds back into the simulation.
public partial class DebugHud : Label
{
    [Export] public NodePath BridgePath = "../../SimulationBridge";

    private SimulationBridge bridge = null!;

    public bool ShowDebug { get; private set; }

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        UiStyle.Panel(this, 16);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            ShowDebug = !ShowDebug;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var time = bridge.Snapshot.Time; // the UI reports the simulation's calendar, never computes outcomes
        int minutes = (int)((long)time.TickOfDay * 24 * 60 / bridge.TicksPerDay);
        string pace = bridge.Clock.Paused ? "PAUSED" : $"x{bridge.Clock.Speed}";
        Text = $"Year {time.Year + 1} · {time.Season} day {time.DayOfSeason + 1} · {minutes / 60:00}:{minutes % 60:00} · {time.Temperature}°C · {pace}";
        if (!ShowDebug) return;
        int entityCount = 0;
        foreach (var chunk in bridge.Snapshot.Chunks)
            entityCount += chunk.Length;
        string gap = bridge.LastGap is { } g ? $"  gap[{g.From}-{g.Through}]" : "";
        Text += $"\ntick {bridge.Snapshot.Tick}  entities {entityCount}  eventCursor {bridge.EventCursor}  backlog {bridge.BacklogSeconds:F2}s{gap}";
    }
}
