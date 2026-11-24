using Game.Integration;
using Godot;

namespace Game.Presentation;

// Presentation-only light: tints the world by the simulation clock (deep blue night, gold/copper dawn and dusk, warm
// bright noon). UI on CanvasLayers is unaffected. It reads the snapshot time and changes nothing in the simulation.
public partial class DayNightTint : CanvasModulate
{
    [Export] public NodePath BridgePath = "../SimulationBridge";
    private SimulationBridge bridge = null!;

    // Mirrored around noon. Short gold and copper intervals keep the sky changing at the horizon.
    private static readonly (float Phase, Color Tint)[] Palette =
    [
        (0f, new("697cab")), (0.17f, new("7885aa")), (0.22f, new("bc8b91")),
        (0.26f, new("ffb779")), (0.32f, new("ffdbab")), (0.42f, new("fff2dc")),
        (0.5f, new("fff9ed"))
    ];

    public override void _Ready() => bridge = GetNode<SimulationBridge>(BridgePath);

    public override void _Process(double delta)
    {
        float day = (float)bridge.Snapshot.Time.TickOfDay / bridge.TicksPerDay;
        float phase = Mathf.Min(day, 1f - day);
        for (int i = 1; i < Palette.Length; i++)
        {
            var (end, tint) = Palette[i];
            if (phase > end) continue;
            var (start, previous) = Palette[i - 1];
            Color = previous.Lerp(tint, Mathf.SmoothStep(start, end, phase));
            return;
        }
    }

    /// <summary>0 by day, rising through dusk to 1 at night (day = fraction of the day, 0 = midnight).</summary>
    internal static float LampStrength(float day) =>
        1f - Mathf.SmoothStep(0.17f, 0.29f, Mathf.Min(day, 1f - day));
}
