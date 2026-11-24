using System;
using System.Linq;
using FantasyWorld.Simulation;
using Game.Integration;
using Game.Presentation;
using Godot;

namespace Game.Tests;

// T019 presentation acceptance: a God-spawned dragon next to Ashford is drawn from its own sheet and fought by the guard,
// and the real hits start impact effects; Firebolt flies and lands as an impact, Heal and taming play their effects;
// the feed and inspector describe the new events; the God spawn cycle reaches the dragon.
//   godot --headless --path Game res://Tests/EffectsAcceptance.tscn
// Run without --headless (optionally with --write-movie) to watch the fight: the scene then keeps running live.
public partial class EffectsAcceptance : Node
{
    private SimulationBridge bridge = null!;
    private EffectsLayer effects = null!;

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
            bridge = root.GetNode<SimulationBridge>("SimulationBridge");
            effects = root.GetNode<EffectsLayer>("FxLayer/Effects");
            var feed = root.GetNode<EventFeed>("UI/Feed");
            var inspector = root.GetNode<InspectorPanel>("UI/Inspector");
            var god = root.GetNode<GodTools>("GodTools");
            bool live = DisplayServer.GetName() != "headless";

            bridge._Process(0.1);
            var town = bridge.Settlements()[0];
            var map = bridge.Map;
            var (dx, dy) = Enumerable.Range(2, 4).SelectMany(r => new[] { (r, 0), (-r, 0), (0, r), (0, -r) })
                .First(o => map.At(town.X + o.Item1, town.Y + o.Item2) is Terrain.Grass or Terrain.Forest);
            bridge.SpawnAt(town.X + dx, town.Y + dy, SpeciesCatalog.Dragon);
            if (live)
            {
                // Watch it play out in real time. Preview only (this test scene, never the game): replay a Firebolt and a
                // Heal every two seconds from the town centre toward the dragon's spawn tile so the spell effects can be seen.
                var timer = new Timer { WaitTime = 2.0, Autostart = true };
                AddChild(timer);
                timer.Timeout += () => effects.Handle([
                    new WorldEvent(0, 0, EventKinds.SpellCast, 0, 0, 0, town.X + dx, town.Y + dy, (int)Spell.Firebolt, 40),
                    new WorldEvent(0, 0, EventKinds.SpellCast, 0, 0, 0, town.X - 1, town.Y + 1, (int)Spell.Heal, 250_000),
                ]);
                return;
            }

            bool hitSeen = false;
            long dragon = 0;
            for (int i = 0; i < 400 && !hitSeen; i++)
            {
                bridge._Process(0.25);
                effects._Process(0.25);
                feed._Process(0.25);
                foreach (var e in bridge.EventsThisFrame)
                {
                    if (e.Kind == EventKinds.CommandApplied && dragon == 0) dragon = e.Target;
                    if (e.Kind == EventKinds.Attacked && (e.Target == dragon || e.Actor == dragon)) hitSeen = true;
                }
            }
            Check(dragon != 0 && bridge.Details(dragon)?.SpeciesKey == SpeciesCatalog.Dragon, "the dragon was spawned");
            Check(hitSeen, "the dragon fought");
            Check(effects.Active.Contains("blunt-hit"), "a real hit started an impact effect");

            // Firebolt: a fireball flies, then lands as an impact; Heal and taming play their own effects.
            effects.Advance(5f);
            Check(effects.ActiveCount == 0, "one-shots end");
            var caster = bridge.Details(dragon)!;
            effects.Handle([
                new WorldEvent(0, 1, EventKinds.SpellCast, 0, dragon, dragon, caster.X + 3, caster.Y, (int)Spell.Firebolt, 40),
                new WorldEvent(0, 1, EventKinds.Attacked, 0, dragon, dragon, caster.X + 3, caster.Y, 0, 75_000),
                new WorldEvent(0, 1, EventKinds.SpellCast, 0, dragon, dragon, caster.X, caster.Y, (int)Spell.Heal, 250_000),
                new WorldEvent(0, 1, EventKinds.AnimalTamed, 0, dragon, dragon, caster.X, caster.Y, 1, SpeciesCatalog.Deer),
            ]);
            var names = effects.Active.OrderBy(n => n).ToArray();
            Check(names.SequenceEqual(new[] { "collect-burst", "fireball", "heal-aoe" }), $"fireball in flight, no early impact ({string.Join(",", names)})");
            effects.Advance(0.6f);
            Check(!effects.Active.Contains("fireball") && effects.Active.Contains("blunt-hit"), "the fireball landed as an impact");

            Check(feed.Format(new WorldEvent(0, 5, EventKinds.SpellCast, 0, 1, dragon, 0, 0, (int)Spell.Firebolt, 40))!.Contains("casts Firebolt"), "feed: Firebolt");
            Check(feed.Format(new WorldEvent(0, 5, EventKinds.AnimalTamed, 0, 1, dragon, 0, 0, 1, 1))!.Contains("tamed by"), "feed: taming");
            string text = inspector.Describe(dragon);
            Check(text.Contains("Dragon"), $"inspector names the dragon ({text.Split('\n')[0]})");

            god.Toggle();
            god.Select(GodTools.Tool.Spawn);
            for (int i = 0; i < 7 && god.CurrentSpecies != SpeciesCatalog.Dragon; i++) god.CycleOption();
            Check(god.CurrentSpecies == SpeciesCatalog.Dragon, "God spawn cycle reaches the dragon");

            GD.Print($"EFFECTS_ACCEPTANCE_PASS tick={bridge.Snapshot.Tick}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"EFFECTS_ACCEPTANCE_FAIL {e.Message}");
            GetTree().Quit(1);
        }
    }
}
