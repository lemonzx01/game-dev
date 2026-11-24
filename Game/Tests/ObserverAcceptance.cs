using System;
using System.Linq;
using FantasyWorld.Simulation;
using Game.Integration;
using Game.Presentation;
using Godot;

namespace Game.Tests;

// T008 acceptance: inspect and follow any living entity; tracking doesn't affect the simulation; dead history is
// retained; pause / single-step / speed work; the feed reports real deaths. Run headless:
//   godot --headless --path Game res://Tests/ObserverAcceptance.tscn
public partial class ObserverAcceptance : Node
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
            var scene = GD.Load<PackedScene>("res://Presentation/Main.tscn");
            Node Make() { var n = scene.Instantiate(); AddChild(n); return n; }
            var observed = Make();
            var reference = Make();
            var a = observed.GetNode<SimulationBridge>("SimulationBridge");
            var b = reference.GetNode<SimulationBridge>("SimulationBridge");
            var observer = observed.GetNode<ObserverController>("Observer");
            var camera = observed.GetNode<CameraController>("Camera2D");
            var inspector = observed.GetNode<InspectorPanel>("UI/Inspector");
            var feed = observed.GetNode<EventFeed>("UI/Feed");

            void Frame(double dt)
            {
                a._Process(dt); b._Process(dt);
                observer._Process(dt); feed._Process(dt); inspector._Process(dt);
            }

            Frame(1.0); // tick 10: starter herd placed and moving
            int live = a.Snapshot.Chunks.Sum(c => c.Length);
            Check(live >= 100, $"starter population present ({live})");

            // Inspect and follow a living entity.
            var target = a.Snapshot.Chunks.SelectMany(c => c).First(r => r.LifeState == LifeStates.Alive);
            long picked = observer.PickAt(new Vector2(target.X + 0.5f, target.Y + 0.5f) * observer.CellSize);
            Check(picked == target.Id.Value || a.Snapshot.Chunks.SelectMany(c => c).Any(r => r.Id.Value == picked && r.X == target.X && r.Y == target.Y), "click picks the animal on that tile");
            observer.Select(target.Id.Value);
            observer.SetFollow(true);
            camera.Zoom = new Vector2(2, 2);
            for (int i = 0; i < 5; i++)
            {
                Frame(0.5);
                if (observer.SelectedRow() is { } row)
                    Check(camera.Position == new Vector2(row.X + 0.5f, row.Y + 0.5f) * observer.CellSize, "camera follows the selection");
            }
            string text = inspector.Describe(target.Id.Value);
            Check(text.Contains(a.SpeciesName(target.VisualKey)) && text.Contains("hunger") && text.Contains("bravery"), "inspector shows identity, vitals and mind");
            Check(a.StateHash() == b.StateHash(), "selecting, following and zooming did not change the simulation");

            // Pause, single step and speed on the observed instance only, then resync the reference.
            observer.TogglePause();
            long t0 = a.Snapshot.Tick;
            a._Process(1.0); // observed instance only; the reference keeps its own pace and is resynced below
            Check(a.Snapshot.Tick == t0, "paused: no ticks");
            observer.StepOnce();
            a._Process(0.0);
            Check(a.Snapshot.Tick == t0 + 1, "single step advances exactly one tick");
            observer.TogglePause();
            observer.SetSpeed(4);
            a._Process(0.1);
            Check(a.Snapshot.Tick == t0 + 1 + 4, "speed x4");
            observer.SetSpeed(1);
            while (b.Snapshot.Tick < a.Snapshot.Tick) b._Process(0.1);
            Check(b.Snapshot.Tick == a.Snapshot.Tick && a.StateHash() == b.StateHash(), "host pacing never changes outcomes");

            // Run until a death; dead history stays inspectable.
            long deadId = 0;
            for (int i = 0; i < 1500 && deadId == 0; i++)
            {
                Frame(1.0);
                foreach (var e in a.EventsThisFrame)
                    if (e.Kind == EventKinds.EntityDied) { deadId = e.Target; break; }
            }
            Check(deadId != 0, "an animal died within the run");
            Check(feed.Lines.Any(l => l.Contains("died")), "feed reports the death");
            observer.Select(deadId);
            Frame(1.0);
            string dead = inspector.Describe(deadId);
            Check(dead.Contains("DIED") && a.Details(deadId)!.DeathTick is not null, "dead entity's history retained in the inspector");
            Check(a.StateHash() == b.StateHash(), "observer actions over the whole run left the simulation identical");

            GD.Print($"T008_ACCEPTANCE_PASS tick={a.Snapshot.Tick} live={a.Snapshot.Chunks.Sum(c => c.Length)} dead=#{deadId}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
