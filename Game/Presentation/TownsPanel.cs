using System.Linq;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// T015 settlement summary (top-right): population, storage and how many members work each job. Read-only.
public partial class TownsPanel : Label
{
    [Export] public NodePath BridgePath = "../../SimulationBridge";

    private SimulationBridge bridge = null!;
    private long shownTick = -1;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        UiStyle.Panel(this, 14);
    }

    public override void _Process(double delta)
    {
        if (shownTick == bridge.Snapshot.Tick) return;
        shownTick = bridge.Snapshot.Tick;
        Visible = bridge.Settlements().Count > 0;
        Text = string.Join("\n", bridge.Settlements().Select(t =>
            $"{t.Name} ({t.Faction})  pop {t.Population}  food {t.Food}  wood {t.Wood}  coins {t.Coins}  guards {t.Guards}  traders out {t.TradersOut}"));
    }
}
