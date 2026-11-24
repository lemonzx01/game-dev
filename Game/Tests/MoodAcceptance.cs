using System;
using System.Linq;
using FantasyWorld.Simulation;
using Game.Integration;
using Game.Presentation;
using Godot;

namespace Game.Tests;

// T018-mood acceptance: one lit lamp per town at night, lamps off and a bright world at noon, F4 toggles tilt-shift.
//   godot --headless --path Game res://Tests/MoodAcceptance.tscn      (add -- --dusk without --headless to stop at dusk)
public partial class MoodAcceptance : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private static void Check(bool pass, string reason)
    {
        if (!pass) throw new InvalidOperationException(reason);
    }

    private void Run()
    {
        try
        {
            var scene = GD.Load<PackedScene>("res://Presentation/Main.tscn").Instantiate();
            AddChild(scene);
            var bridge = scene.GetNode<SimulationBridge>("SimulationBridge");
            var lamps = scene.GetNode<LightingTowns>("LightingTowns");
            var focus = scene.GetNode<LightingFocus>("LightingFocus/Focus");
            var tint = scene.GetNode<DayNightTint>("DayNight");
            bridge._Process(0.1);
            lamps._Process(0);
            var towns = bridge.Settlements();
            var lights = lamps.GetChildren().OfType<PointLight2D>().ToArray();
            Check(towns.Count > 0 && lights.Length == towns.Count, "One lamp per founded town");
            Check(lights.All(l => l.Enabled && l.Visible && l.Energy > 0 && l.Texture != null), "Night lamps emit light");
            Check(!focus.Visible, "Tilt starts off");
            using var key = new InputEventKey { Keycode = Key.F4, Pressed = true };
            GetViewport().PushInput(key, true);
            Check(focus.Visible, "F4 enables tilt");
            GetViewport().PushInput(key, true);
            Check(!focus.Visible, "F4 disables tilt");

            int noon = bridge.TicksPerDay / 2;
            while (bridge.Snapshot.Time.TickOfDay < noon) bridge._Process(0.1);
            lamps._Process(0);
            tint._Process(0);
            Check(lights.All(l => !l.Enabled && l.Energy == 0), "Noon lamps are off");
            Check(tint.Color.R > 0.95f && tint.Color.B > 0.85f, "Noon world is bright");
            if (OS.GetCmdlineUserArgs().Contains("--dusk"))
            {
                int dusk = bridge.TicksPerDay * 3 / 4;
                while (bridge.Snapshot.Time.TickOfDay < dusk) bridge._Process(0.1);
                bridge.Clock.Paused = true;
                return;
            }
            GD.Print("MOOD_ACCEPTANCE_PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MOOD_ACCEPTANCE_FAIL {ex.Message}");
            GD.PushError(ex.ToString());
            GetTree().Quit(1);
        }
    }
}
