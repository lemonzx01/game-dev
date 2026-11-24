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

    public override void _Ready() => bridge = GetNode<SimulationBridge>(BridgePath);

    public override void _Process(double delta)
    {
        bool changed = false;
        if (bridge.LastGap is { } gap) changed |= Add($"(events {gap.From}-{gap.Through} lost; view rebuilt from snapshot)");
        foreach (var e in bridge.EventsThisFrame)
            if (Format(e) is { } line) changed |= Add(line);
        if (changed) Text = string.Join("\n", lines);
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
        EventKinds.CommandRejected => $"t{e.Tick}: God command {e.CommandSequence} rejected ({(CommandReason)e.Reason})",
        EventKinds.EventsDropped => $"t{e.Tick}: {e.Amount} minor events skipped",
        _ => null, // applied commands, eating, drinking, wandering stay out of the feed (inspector shows them)
    };

    private string SpeciesOf(long id) => bridge.Details(id) is { } d ? bridge.SpeciesName(d.SpeciesKey) : "?";
}
