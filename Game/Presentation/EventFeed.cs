using System.Collections.Generic;
using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Event feed (T008): turns the events the bridge drained this frame into readable lines (births, deaths, attacks,
// God command outcomes, lost ranges). Keeps the last MaxLines. Pure presentation of real events.
public partial class EventFeed : Label
{
    [Export] public NodePath BridgePath = "../../SimulationBridge";
    [Export] public int MaxLines = 12;

    private SimulationBridge bridge = null!;
    private readonly LinkedList<string> lines = new();

    public IReadOnlyCollection<string> Lines => lines;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        UiStyle.Panel(this, 13);
    }

    public override void _Process(double delta)
    {
        bool changed = false;
        if (bridge.LastGap is { } gap) changed |= Add($"(events {gap.From}-{gap.Through} lost; view rebuilt from snapshot)");
        foreach (var e in bridge.EventsThisFrame)
            if (Format(e) is { } line) changed |= Add(line);
        foreach (var (command, outcome) in bridge.PlayerOutcomesThisFrame)
            changed |= Add(FormatPlayer(command, outcome));
        if (changed) Text = string.Join("\n", lines);
    }

    /// <summary>Host messages (save/load results, refused tool use).</summary>
    public void Note(string message)
    {
        Add(message);
        Text = string.Join("\n", lines);
    }

    public string FormatPlayer(GameCommand command, in WorldEvent outcome)
    {
        bool ok = outcome.Kind == EventKinds.CommandApplied;
        string what = command switch
        {
            SpawnEntityCommand s => ok ? $"spawned {bridge.SpeciesName(s.SpeciesKey)} #{outcome.Target} at {s.X},{s.Y}" : $"spawn {bridge.SpeciesName(s.SpeciesKey)} at {s.X},{s.Y}",
            SetTerrainCommand t => $"terrain {t.X},{t.Y} -> {t.Terrain}",
            FoundSettlementCommand f => $"founded {f.Faction} town {f.Name} at {f.X},{f.Y}",
            _ => command.Kind.ToString(),
        };
        return ok ? $"t{outcome.Tick}: God {what}" : $"t{outcome.Tick}: God {what} rejected ({(CommandReason)outcome.Reason})";
    }

    private bool Add(string line)
    {
        lines.AddLast(line);
        while (lines.Count > MaxLines) lines.RemoveFirst();
        return true;
    }

    public string? Format(in WorldEvent e) => e.Kind switch
    {
        EventKinds.EntityBorn => $"t{e.Tick}: {SpeciesOf(e.Target)} #{e.Target} born to #{e.Actor}",
        EventKinds.EntityDied => $"t{e.Tick}: {SpeciesOf(e.Target)} #{e.Target} died ({(DeathCause)e.Reason})" + (e.Actor != 0 ? $" by #{e.Actor}" : ""),
        EventKinds.Attacked => $"t{e.Tick}: #{e.Actor} attacked #{e.Target}",
        EventKinds.SettlementFounded => $"t{e.Tick}: {(Faction)e.Reason} settlement #{e.Target} founded at {e.X},{e.Y}",
        EventKinds.TradeSold => $"t{e.Tick}: trader #{e.Actor} sold {e.Amount} {(ResourceKind)e.Reason} to settlement #{e.Target}",
        EventKinds.TradePaid => $"t{e.Tick}: settlement #{e.Actor} paid {e.Amount} coins to #{e.Target}",
        EventKinds.GuardAlert => $"t{e.Tick}: guard #{e.Actor} defends against #{e.Target}",
        EventKinds.CarryLost => $"t{e.Tick}: #{e.Actor} died carrying {e.Amount} {(ResourceKind)e.Reason}",
        EventKinds.EventsDropped => $"t{e.Tick}: {e.Amount} minor events skipped",
        _ => null, // applied commands, eating, drinking, wandering stay out of the feed (inspector shows them)
    };

    private string SpeciesOf(long id) => bridge.Details(id) is { } d ? bridge.SpeciesName(d.SpeciesKey) : "?";
}
