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
        UiStyle.Panel(this, 14);
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
        if (id == 0) return "Click a creature to inspect it\nSpace pause · . step · 1-4 speed · F follow · G god · F3 debug";
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
        if (bridge.Adventure(id) is { } a)
        {
            if (a.KnowsMagic) sb.AppendLine($"mana {a.Mana}/{a.MaxMana}  (talent {a.Aptitude})  spells: Firebolt, Heal");
            if (a.TamedTo != 0) sb.AppendLine($"tamed by {bridge.Settlements()[a.TamedTo - 1].Name}" + (a.TamerId != 0 ? $" (bond with #{a.TamerId})" : ""));
            else if (a.TamerId != 0) sb.AppendLine($"trust {a.Trust} toward #{a.TamerId}");
        }
        if (bridge.Skills(id) is { } k)
        {
            var parts = new System.Collections.Generic.List<string>();
            void Add(string name, int level, long xp) { if (xp > 0) parts.Add($"{name} {level} ({xp}xp)"); }
            Add("farm", k.FarmingLevel, k.Farming); Add("forage", k.ForagingLevel, k.Foraging); Add("wood", k.WoodcuttingLevel, k.Woodcutting);
            Add("combat", k.CombatLevel, k.Combat); Add("trade", k.TradingLevel, k.Trading); Add("lore", k.ScholarshipLevel, k.Scholarship);
            if (parts.Count > 0) sb.AppendLine("skills: " + string.Join("  ", parts));
            if (k.BooksStarted > 0) sb.AppendLine($"books: {k.BooksFinished} finished of {k.BooksStarted} started");
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
