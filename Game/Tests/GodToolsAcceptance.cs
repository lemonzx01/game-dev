using System;
using System.IO;
using System.Linq;
using FantasyWorld.Simulation;
using Game.Integration;
using Game.Presentation;
using Godot;

namespace Game.Tests;

// T018 playable-build acceptance: God tools go through validated commands; terrain edits refresh the view; in-game
// save/load restores the exact world and continues identically; a corrupt save leaves the world untouched.
//   godot --headless --path Game res://Tests/GodToolsAcceptance.tscn
public partial class GodToolsAcceptance : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fws-god-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = GD.Load<PackedScene>("res://Presentation/Main.tscn").Instantiate();
            root.GetNode<SimulationBridge>("SimulationBridge").StarterAnimals = 0;
            AddChild(root);
            var bridge = root.GetNode<SimulationBridge>("SimulationBridge");
            var god = root.GetNode<GodTools>("GodTools");
            var feed = root.GetNode<EventFeed>("UI/Feed");
            var renderer = root.GetNode<MapRenderer>("MapRenderer");
            var observer = root.GetNode<ObserverController>("Observer");
            void Frame(double dt) { bridge._Process(dt); feed._Process(dt); renderer._Process(dt); observer._Process(dt); }
            Frame(0.1);

            // Spawn: God mode off does nothing; on, a wolf is placed through a command.
            Check(god.ApplyAt(1, 1) is null, "tools inactive outside God mode");
            god.Toggle();
            god.Select(GodTools.Tool.Spawn);
            while (god.CurrentSpecies != SpeciesCatalog.Wolf) god.CycleOption();
            var map = bridge.Map;
            int grass = Enumerable.Range(0, map.Cells.Length).First(c => map.Cells[c] == Terrain.Grass && !Occupied(bridge, c % map.Width, c / map.Width));
            int gx = grass % map.Width, gy = grass / map.Width;
            Check(god.ApplyAt(gx, gy)!.Status == "Queued", "spawn queued");
            Frame(0.1);
            var wolf = bridge.Snapshot.Chunks.SelectMany(c => c).Single(r => r.VisualKey == SpeciesCatalog.Wolf);
            Check(wolf.X == gx && wolf.Y == gy, "wolf placed on the clicked tile");
            Check(feed.Lines.Any(l => l.Contains("God spawned Wolf")), "feed reports the spawn");

            // Terrain brush: water on a mountain tile (no resources there); the view refreshes.
            god.Select(GodTools.Tool.Terrain);
            while (god.CurrentTerrain != Terrain.Water) god.CycleOption();
            int mountain = Enumerable.Range(0, map.Cells.Length).First(c => map.Cells[c] == Terrain.Mountain);
            int mx = mountain % map.Width, my = mountain / map.Width;
            int revision = bridge.MapRevision;
            god.ApplyAt(mx, my);
            Frame(0.1);
            Check(bridge.Map.At(mx, my) == Terrain.Water && bridge.MapRevision > revision && renderer.BuiltRevision == bridge.MapRevision, "terrain painted and texture rebuilt");

            // A command the world refuses is reported, not applied.
            while (god.CurrentTerrain != Terrain.Mountain) god.CycleOption();
            var row = bridge.Snapshot.Chunks.SelectMany(c => c).First();
            god.ApplyAt(row.X, row.Y);
            Frame(0.1);
            Check(feed.Lines.Any(l => l.Contains("rejected (Occupied)")) && bridge.Map.At(row.X, row.Y) != Terrain.Mountain, "occupied tile refused");

            // Save, continue, load: exact restore and identical continuation.
            Directory.CreateDirectory(dir);
            god.SavePathOverride = Path.Combine(dir, "quick.fws");
            observer.Select(wolf.Id.Value);
            god.QuickSave();
            long savedTick = bridge.Snapshot.Tick;
            string savedHash = bridge.StateHash();
            for (int i = 0; i < 5; i++) Frame(1.0);
            string later = bridge.StateHash();
            long laterTick = bridge.Snapshot.Tick;
            god.QuickLoad();
            Frame(0.0);
            Check(bridge.Snapshot.Tick == savedTick && bridge.StateHash() == savedHash, "load restores the saved world");
            Check(observer.SelectedId == 0 && bridge.EventsThisFrame.IsEmpty, "selection and old events dropped on load");
            Check(bridge.Map.At(mx, my) == Terrain.Water && renderer.BuiltRevision == bridge.MapRevision, "loaded terrain shown");
            while (bridge.Snapshot.Tick < laterTick) Frame(1.0);
            Check(bridge.StateHash() == later, "continuation after load is identical");

            // A corrupt save is refused and the running world is untouched.
            File.WriteAllBytes(Path.Combine(dir, "bad.fws"), [1, 2, 3, 4]);
            god.SavePathOverride = Path.Combine(dir, "bad.fws");
            string before = bridge.StateHash();
            god.QuickLoad();
            Check(bridge.StateHash() == before && feed.Lines.Any(l => l.StartsWith("Load failed")), "corrupt save refused");

            GD.Print($"GODTOOLS_ACCEPTANCE_PASS tick={bridge.Snapshot.Tick}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static bool Occupied(SimulationBridge bridge, int x, int y) =>
        bridge.Snapshot.Chunks.SelectMany(c => c).Any(r => r.X == x && r.Y == y);
}
