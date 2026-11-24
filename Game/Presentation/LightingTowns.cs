using System.Collections.Generic;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// T018-mood (Codex, finished by Claude Code): one warm PointLight2D per town at its store, fading in at dusk and out at
// dawn by the simulation clock. Positions come from bridge.Settlements() only; lamps follow towns that appear or vanish
// (God founding, load). Presentation only: no per-entity nodes, nothing written to the simulation.
public partial class LightingTowns : Node2D
{
    private readonly Dictionary<int, PointLight2D> lamps = new();
    private readonly HashSet<int> removed = new();
    private SimulationBridge bridge = null!;
    private MapRenderer map = null!;
    private GradientTexture2D falloff = null!;
    private CanvasItemMaterial emissive = null!;
    private long tick = -1;
    private int generation = -1;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>("../SimulationBridge");
        map = GetNode<MapRenderer>("../MapRenderer");
        falloff = new GradientTexture2D
        {
            Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = Vector2.One / 2, FillTo = new Vector2(1, 0.5f),
            Gradient = new Gradient
            {
                Offsets = [0, 0.12f, 0.34f, 0.68f, 1],
                // Additive light uses the texture colour, so the falloff must reach black (not just zero alpha),
                // otherwise the light's square bounds show as a hard-edged box at dusk.
                Colors = [Colors.White, new("f0d2a0"), new("8a6a40"), new("2a1c0c"), Colors.Black]
            }
        };
        emissive = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
    }

    public override void _Process(double delta)
    {
        var snapshot = bridge.Snapshot;
        if (tick == snapshot.Tick && generation == bridge.WorldGeneration) return;
        tick = snapshot.Tick;
        generation = bridge.WorldGeneration;
        float strength = DayNightTint.LampStrength((float)snapshot.Time.TickOfDay / bridge.TicksPerDay);
        removed.UnionWith(lamps.Keys);
        foreach (var town in bridge.Settlements())
        {
            removed.Remove(town.Id);
            if (!lamps.TryGetValue(town.Id, out var lamp))
            {
                lamp = new PointLight2D
                {
                    Texture = falloff, TextureScale = 0.84f, Color = new("ffce91"),
                    BlendMode = Light2D.BlendModeEnum.Add, ShadowEnabled = false
                };
                AddChild(lamp);
                // A two-pixel lit window at the left of the store door.
                lamp.AddChild(new Polygon2D
                {
                    Polygon = [new(-1,-1), new(1,-1), new(1,1), new(-1,1)],
                    Color = new("ffe4a3"), Material = emissive, ZIndex = 1
                });
                lamps.Add(town.Id, lamp);
            }
            lamp.Position = new Vector2(town.X + 0.5f, town.Y + 1) * map.CellSize + new Vector2(-8, -12);
            lamp.Visible = lamp.Enabled = strength > 0;
            lamp.Energy = 0.95f * strength;
            lamp.Modulate = new Color(1, 1, 1, strength);
        }
        foreach (int id in removed)
        {
            var obsolete = lamps[id];
            RemoveChild(obsolete);
            obsolete.QueueFree();
            lamps.Remove(id);
        }
        removed.Clear();
    }
}
