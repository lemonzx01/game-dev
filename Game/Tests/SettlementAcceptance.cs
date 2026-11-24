using System;
using System.Linq;
using FantasyWorld.Simulation;
using Game.Integration;
using Game.Presentation;
using Godot;

namespace Game.Tests;

// T015 display acceptance: the opening scene founds a human and a demon town with settlers; after a few thousand
// ticks their storage holds real production; the inspector shows town and job; the God tool can found a new town.
//   godot --headless --path Game res://Tests/SettlementAcceptance.tscn
public partial class SettlementAcceptance : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private void Run()
    {
        try
        {
            var root = GD.Load<PackedScene>("res://Presentation/Main.tscn").Instantiate();
            AddChild(root);
            var bridge = root.GetNode<SimulationBridge>("SimulationBridge");
            var inspector = root.GetNode<InspectorPanel>("UI/Inspector");
            var god = root.GetNode<GodTools>("GodTools");
            var feed = root.GetNode<EventFeed>("UI/Feed");
            void Frame(double dt) { bridge._Process(dt); feed._Process(dt); }

            Frame(0.1);
            var towns = bridge.Settlements();
            Check(towns.Count == 2 && towns[0].Faction == Faction.Human && towns[1].Faction == Faction.Demon, "human and demon towns founded");
            Check(towns.All(t => t.Population == bridge.StarterSettlers), $"settlers joined ({string.Join(",", towns.Select(t => t.Population))})");

            for (int i = 0; i < 300; i++) Frame(1.0);
            towns = bridge.Settlements();
            foreach (var t in towns)
                Check(t.Food + t.Wood > 0 && t.Farmers + t.Gatherers + t.Woodcutters > 0, $"{t.Name} produced and stored ({t})");

            var settler = bridge.Snapshot.Chunks.SelectMany(c => c).First(r => r.VisualKey is SpeciesCatalog.Human or SpeciesCatalog.Demon);
            string text = inspector.Describe(settler.Id.Value);
            Check(text.Contains("lives in") && text.Contains("job"), "inspector shows town and job");

            // Found a third town with the God tool on a grass tile far from both towns.
            var map = bridge.Map;
            int site = Enumerable.Range(0, map.Cells.Length).First(c =>
            {
                int x = c % map.Width, y = c / map.Width;
                return map.Cells[c] == Terrain.Grass && towns.All(t => Math.Max(Math.Abs(t.X - x), Math.Abs(t.Y - y)) >= 2 * t.Radius);
            });
            god.Toggle();
            god.Select(GodTools.Tool.Settlement);
            god.ApplyAt(site % map.Width, site / map.Width);
            Frame(0.1);
            Check(bridge.Settlements().Count == 3 && feed.Lines.Any(l => l.Contains("God founded Human town")), "God tool founded a town");

            GD.Print($"SETTLEMENT_ACCEPTANCE_PASS tick={bridge.Snapshot.Tick} " + string.Join(" ", towns.Select(t => $"{t.Name}:pop{t.Population}/food{t.Food}/wood{t.Wood}")));
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
