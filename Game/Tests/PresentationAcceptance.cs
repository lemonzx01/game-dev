using System;
using Game.Integration;
using Game.Presentation;
using Godot;

namespace Game.Tests;

public partial class PresentationAcceptance : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private void Run()
    {
        try
        {
            var scene = GD.Load<PackedScene>("res://Presentation/Main.tscn");
            var world = scene.Instantiate();
            world.GetNode<SimulationBridge>("SimulationBridge").StarterAnimals = 0; // T007 checks one entity
            AddChild(world);
            var bridge = world.GetNode<SimulationBridge>("SimulationBridge");
            bridge._Process(0.1);
            var firstHash = bridge.StateHash();
            if (bridge.Snapshot.Tick != 1 || bridge.PreviousSnapshot.Tick != 0 ||
                bridge.Snapshot.Chunks[0].Length != 1 || bridge.EventCursor != 1 ||
                bridge.EventsThisFrame[0].Kind != FantasyWorld.Simulation.EventKinds.CommandApplied)
                throw new Exception("Spawn, snapshot or event cursor mismatch");
            var camera = world.GetNode<CameraController>("Camera2D");
            camera.Position += new Vector2(40, 70);
            using var wheel = new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.WheelUp };
            float zoomBefore = camera.Zoom.X; // T008 starts zoomed out to fit the map
            camera._UnhandledInput(wheel);
            if (camera.Zoom.X <= zoomBefore || firstHash != bridge.StateHash())
                throw new Exception("Camera input independence failed");
            bridge._Process(0);
            if (!bridge.EventsThisFrame.IsEmpty || bridge.EventCursor != 1)
                throw new Exception("Repeated events");
            var control = scene.Instantiate();
            control.GetNode<SimulationBridge>("SimulationBridge").StarterAnimals = 0;
            AddChild(control);
            var reference = control.GetNode<SimulationBridge>("SimulationBridge");
            reference._Process(0.4);
            bridge._Process(0.3);
            if (bridge.Snapshot.Tick != 4 || bridge.StateHash() != reference.StateHash())
                throw new Exception("Divergence after independent camera movement");
            GD.Print("T007_ACCEPTANCE_PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
