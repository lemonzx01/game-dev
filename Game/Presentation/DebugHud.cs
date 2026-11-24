using Game.Integration;
using Godot;

namespace Game.Presentation;

// Visible, non-interactive proof that the bridge is live: reads the bridge's
// real snapshot/cursor and never feeds anything back into the simulation.
public partial class DebugHud : Label
{
    [Export] public NodePath BridgePath = "../../SimulationBridge";

    private SimulationBridge bridge = null!;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
    }

    public override void _Process(double delta)
    {
        int entityCount = 0;
        foreach (var chunk in bridge.Snapshot.Chunks)
            entityCount += chunk.Length;
        string gap = bridge.LastGap is { } g ? $"  gap[{g.From}-{g.Through}]" : "";
        Text = $"Tick {bridge.Snapshot.Tick}  entities {entityCount}  eventCursor {bridge.EventCursor}  backlog {bridge.BacklogSeconds:F2}s{gap}";
    }
}
