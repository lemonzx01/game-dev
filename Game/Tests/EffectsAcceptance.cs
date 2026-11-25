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
            var renderer = root.GetNode<MapRenderer>("MapRenderer");
            var feed = root.GetNode<EventFeed>("UI/Feed");
            var inspector = root.GetNode<InspectorPanel>("UI/Inspector");
            var god = root.GetNode<GodTools>("GodTools");
            bool live = DisplayServer.GetName() != "headless";

            bridge._Process(0.1);
            var town = bridge.Settlements()[0];
            var map = bridge.Map;
            var (dx, dy) = Enumerable.Range(2, 4).SelectMany(r => new[] { (r, 0), (-r, 0), (0, r), (0, -r) })
                .First(o => map.At(town.X + o.Item1, town.Y + o.Item2) is Terrain.Grass or Terrain.Forest);
            if (live)
            {
                bridge.SpawnAt(town.X + dx, town.Y + dy, SpeciesCatalog.Dragon); // live preview only: the headless checks below spawn their own
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

            // Phase A (night): a wolf at the store. With R1/R2 the town's guard keeps watch and fights a lone wolf alone, so a real
            // hit happens; the impact effect must start on that frame. (A full-health dragon now makes guards raise the alarm and
            // retreat instead of fighting alone, and dragons sleep at night, so the dragon is checked separately below.)
            bridge.SpawnAt(town.X + 1, town.Y, SpeciesCatalog.Wolf);
            bool impactSeen = false, wolfFought = false;
            void Frame()
            {
                bridge._Process(0.25);
                renderer._Process(0.25);
                effects._Process(0.25);
                feed._Process(0.25);
            }
            for (int i = 0; i < 400 && !impactSeen; i++)
            {
                Frame();
                if (bridge.EventsThisFrame.Any(e => e.Kind == EventKinds.Attacked)) wolfFought = true;
                if (bridge.EventsThisFrame.Any(e => e.Kind == EventKinds.Attacked) && effects.Active.Contains("blunt-hit")) impactSeen = true;
            }
            Check(wolfFought, "the guard fought the wolf in town");
            Check(impactSeen, "a real hit started an impact effect");

            // Phase B (day): a dragon spawned away from both towns is awake and wanders; check what is drawn for it.
            while (bridge.Snapshot.Time.TickOfDay < bridge.TicksPerDay * 3 / 10) Frame(); // 07:12, dragons are awake
            var towns = bridge.Settlements();
            int site = Enumerable.Range(0, map.Cells.Length).First(c =>
            {
                int x = c % map.Width, y = c / map.Width;
                return map.Cells[c] == Terrain.Grass && x > 4 && y > 4 && x < map.Width - 5 && y < map.Height - 5
                       && towns.All(t => System.Math.Max(System.Math.Abs(t.X - x), System.Math.Abs(t.Y - y)) > t.Radius + 6);
            });
            bridge.SpawnAt(site % map.Width, site / map.Width, SpeciesCatalog.Dragon);
            long dragon = 0;
            int lastX = int.MinValue, turnsSeen = 0, bodiesChecked = 0;
            for (int i = 0; i < 800 && (dragon == 0 || turnsSeen == 0 || bodiesChecked == 0); i++)
            {
                Frame();
                foreach (var e in bridge.EventsThisFrame)
                    if (e.Kind == EventKinds.CommandApplied && dragon == 0 && bridge.Details(e.Target)?.SpeciesKey == SpeciesCatalog.Dragon) dragon = e.Target;
                // FIX-R2 #15: the dragon is drawn 1:1 from its row of the owner sheet, a non-empty frame, mirrored when it last stepped left.
                if (dragon != 0 && bridge.Snapshot.Chunks.SelectMany(c => c).FirstOrDefault(r => r.Id.Value == dragon) is { VisualKey: SpeciesCatalog.Dragon } row
                    && row.LifeState == LifeStates.Alive)
                {
                    var sprite = renderer.SpriteOf(row);
                    Check(sprite is { } sp && sp.Sheet.ResourcePath.EndsWith("owner_sheet.png") && sp.Region.Position.Y == 13 * 104 && sp.Scale == 1f, "the dragon is drawn 1:1 from its row of the owner sheet");
                    if (lastX != int.MinValue && row.X != lastX)
                    {
                        Check(sprite!.Value.Mirrored == row.X < lastX, $"the dragon faces the way it stepped ({lastX} -> {row.X})");
                        turnsSeen++;
                    }
                    lastX = row.X;
                    if (bodiesChecked < 3)
                    {
                        using var sheet = Image.LoadFromFile(sprite!.Value.Sheet.ResourcePath);
                        using var frame = sheet.GetRegion((Rect2I)sprite.Value.Region);
                        int opaque = 0;
                        for (int y = 0; y < frame.GetHeight(); y++)
                            for (int x = 0; x < frame.GetWidth(); x++)
                                if (frame.GetPixel(x, y).A > 0.5f) opaque++;
                        Check(opaque > 300, $"the dragon frame has a body ({opaque} opaque pixels)");
                        bodiesChecked++;
                    }
                }
            }
            Check(dragon != 0 && bridge.Details(dragon)?.SpeciesKey == SpeciesCatalog.Dragon, "the dragon was spawned");
            Check(turnsSeen > 0 && bodiesChecked > 0, "the dragon moved sideways at least once (facing checked) and its frames were checked");

            // Firebolt: a fireball flies, then lands as an impact; Heal and taming play their own effects.
            effects.Advance(5f);
            Check(effects.ActiveCount == 0, "one-shots end");
            // A person casts at the dragon. FIX-R2 #10: the SpellCast and its Attacked arrive in different drains.
            long mage = bridge.Snapshot.Chunks.SelectMany(c => c).First(r => r.VisualKey == SpeciesCatalog.Human && r.LifeState == LifeStates.Alive).Id.Value;
            var target = bridge.Details(dragon)!;
            long t = bridge.Snapshot.Tick;
            effects.Handle([new WorldEvent(0, t, EventKinds.SpellCast, 0, mage, dragon, target.X, target.Y, (int)Spell.Firebolt, 40)]);
            effects.Handle([
                new WorldEvent(0, t, EventKinds.Attacked, 0, mage, dragon, target.X, target.Y, 0, 75_000),
                new WorldEvent(0, t, EventKinds.SpellCast, 0, mage, mage, target.X, target.Y, (int)Spell.Heal, 250_000),
                new WorldEvent(0, t, EventKinds.AnimalTamed, 0, mage, dragon, target.X, target.Y, 1, SpeciesCatalog.Deer),
            ]);
            var names = effects.Active.OrderBy(n => n).ToArray();
            Check(names.SequenceEqual(new[] { "collect-burst", "fireball", "heal-aoe" }), $"fireball in flight, no early impact across drains ({string.Join(",", names)})");
            Check(effects.PendingBoltCount == 0, "the bolt suppression was used once");
            effects.Handle([new WorldEvent(0, t, EventKinds.Attacked, 0, dragon, mage, target.X, target.Y, 0, 700_000)]);
            Check(effects.Active.Count(n => n == "blunt-hit") == 1, "an unrelated melee hit still plays at once");
            effects.Advance(0.6f);
            Check(!effects.Active.Contains("fireball") && effects.Active.Count(n => n == "blunt-hit") == 2, "the fireball landed as exactly one impact");

            // FIX-R2 #12: a flood of events never exceeds the cap, and suppression stays bounded.
            effects.Advance(5f);
            var flood = new System.Collections.Generic.List<WorldEvent>();
            for (int k = 0; k < 400; k++) flood.Add(new WorldEvent(0, t + 1, EventKinds.Attacked, 0, mage, dragon, target.X, target.Y, 0, 1));
            for (int k = 0; k < 200; k++) flood.Add(new WorldEvent(0, t + 1, EventKinds.SpellCast, 0, k + 1000, dragon, target.X, target.Y, (int)Spell.Firebolt, 40));
            effects.Handle(flood);
            Check(effects.ActiveCount <= effects.MaxActive && effects.PendingBoltCount <= 64, $"bounded under a flood ({effects.ActiveCount} active, {effects.PendingBoltCount} pending)");
            effects.Advance(5f); // fireballs land and start their impacts
            effects.Advance(5f); // the impacts finish
            Check(effects.ActiveCount == 0, "the flood fully expires");

            // FIX-R2 #11: a fireball in flight when the world is replaced never lands on the loaded world.
            string save = ProjectSettings.GlobalizePath("user://effects_acceptance.fws");
            bridge.SaveTo(save);
            effects.Handle([new WorldEvent(0, t, EventKinds.SpellCast, 0, mage, dragon, target.X, target.Y, (int)Spell.Firebolt, 40)]);
            Check(effects.Active.Contains("fireball"), "a fireball is in flight before load");
            bridge.LoadFrom(save);
            effects._Process(0.01);
            Check(effects.ActiveCount == 0 && effects.PendingBoltCount == 0, "load clears effects from the old timeline");
            effects.Advance(1f);
            Check(effects.ActiveCount == 0, "no stale impact lands after load");

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
