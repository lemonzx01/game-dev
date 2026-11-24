using System.Linq;
using System.Text;
using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Shows the selected entity: identity, life stage, vitals, current action, personality, fear, recent memories and
// bonds. For a dead entity it shows the durable record (death tick, cause, parents) — history is retained after
// death. Reads only; refreshes when the tick or the selection changes.
public partial class InspectorPanel : Label
{
    [Export] public NodePath BridgePath = "../../SimulationBridge";
    [Export] public NodePath ObserverPath = "../../Observer";

    private SimulationBridge bridge = null!;
    private ObserverController observer = null!;
    private long shownTick = -1, shownId = -1;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        observer = GetNode<ObserverController>(ObserverPath);
    }

    public override void _Process(double delta)
    {
        if (shownTick == bridge.Snapshot.Tick && shownId == observer.SelectedId) return;
        shownTick = bridge.Snapshot.Tick;
        shownId = observer.SelectedId;
        Text = Describe(observer.SelectedId);
    }

    public string Describe(long id)
    {
        if (id == 0) return "Click an animal to inspect it. Space: pause  .: step  1-4: speed  F: follow  Esc: clear";
        var d = bridge.Details(id);
        if (d is null) return $"#{id}: unknown";
        var sb = new StringBuilder();
        sb.Append($"#{id} {bridge.SpeciesName(d.SpeciesKey)} {d.Sex} {d.Stage}  age {d.Age} ticks");
        if (d.MotherId != 0) sb.Append($"  mother #{d.MotherId} father #{d.FatherId}");
        sb.AppendLine();
        if (d.LifeState == LifeStates.Dead)
        {
            sb.AppendLine($"DIED tick {d.DeathTick} ({d.DeathCause}) at {d.X},{d.Y}");
            return sb.ToString();
        }
        sb.AppendLine($"at {d.X},{d.Y}  hunger {d.Hunger / 10_000}%  thirst {d.Thirst / 10_000}%  health {d.Health / 10_000}%"
            + (d.PregnantUntil != 0 ? $"  pregnant until {d.PregnantUntil}" : ""));
        if (observer.SelectedRow() is { } row) sb.AppendLine($"doing: {(ActionKind)row.Action}{(observer.Following ? "  [following]" : "")}");
        if (d.SettlementId != 0)
        {
            var town = bridge.Settlements()[d.SettlementId - 1];
            sb.AppendLine($"lives in {town.Name}  job {d.Job}" + (d.CarryAmount > 0 ? $"  carrying {d.CarryAmount} {d.CarryKind}" : ""));
        }
        if (bridge.Mind(id) is { } m)
        {
            sb.AppendLine($"bravery {m.Bravery}  sociability {m.Sociability}  fear {m.Fear}");
            foreach (var mem in m.Memories.OrderByDescending(x => x.LastRelevantTick).Take(3))
                sb.AppendLine($"  remembers {mem.Kind} at {mem.X},{mem.Y} (t{mem.LastRelevantTick})");
            foreach (var rel in m.Relationships.OrderByDescending(x => System.Math.Abs(x.Affinity)).Take(3))
                sb.AppendLine($"  feels {rel.Affinity:+#;-#;0} toward #{rel.Other}");
        }
        return sb.ToString();
    }
}
