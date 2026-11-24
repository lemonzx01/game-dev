using Game.Integration;
using Godot;

namespace Game.Presentation;

// Presentation-only light: tints the world by the simulation clock (darker at night, warm at dawn/dusk).
// UI on CanvasLayers is unaffected. It reads the snapshot time and changes nothing in the simulation.
public partial class DayNightTint : CanvasModulate
{
    [Export] public NodePath BridgePath = "../SimulationBridge";

    private SimulationBridge bridge = null!;

    public override void _Ready() => bridge = GetNode<SimulationBridge>(BridgePath);

    public override void _Process(double delta)
    {
        float day = (float)bridge.Snapshot.Time.TickOfDay / bridge.TicksPerDay;      // 0 = midnight, 0.5 = noon
        float light = 0.5f - 0.5f * Mathf.Cos(day * Mathf.Tau);                       // 0 at midnight, 1 at noon
        var night = new Color(0.45f, 0.5f, 0.72f);
        var dusk = new Color(1f, 0.85f, 0.72f);
        Color = light < 0.5f ? night.Lerp(dusk, light * 2f) : dusk.Lerp(Colors.White, (light - 0.5f) * 2f);
    }
}
