using System.Collections.Generic;
using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Plays pixel-art effects (pixelfx sprite sheets, Assets/Effects, MIT) for real simulation events drained this frame:
// Firebolt (a fireball flying caster → target, then an impact), Heal (a green ring on the healed), melee hits, and a
// sparkle when an animal is tamed. Presentation only: it reads events and never changes the world. Lives in its own
// CanvasLayer following the camera so the night tint does not dim the glow.
public partial class EffectsLayer : Node2D
{
    [Export] public NodePath BridgePath = "../../SimulationBridge";
    [Export] public float CellSize = 16f;
    [Export] public int MaxActive = 96; // fast-forwarded fights skip extra effects instead of piling up

    // Frame metadata from each sheet's JSON (meta.pixelfx: fps, frames, frameSize, pivot, columns), pixelfx 5294518.
    private sealed record Sheet(string Name, Texture2D Texture, int Frames, int Columns, Vector2 Size, Vector2 Pivot, float Fps, bool Loop);

    private sealed class Fx
    {
        public required Sheet Sheet;
        public Vector2 Pos, From, To;
        public float Age, Scale = 1f, Flight; // Flight > 0: a projectile travelling From → To in that many seconds
        public Color Tint = Colors.White;
    }

    private SimulationBridge bridge = null!;
    private Sheet fireball = null!, hit = null!, heal = null!, burst = null!;
    private readonly List<Fx> active = [];
    // Firebolts whose matching Attacked record may arrive in a later drain (FIX-R2 #10): one use each, bounded,
    // expired by simulation tick and cleared when the world is replaced (#11).
    private readonly List<(long Tick, long Actor, long Target)> pendingBolts = [];
    private const int MaxPendingBolts = 64, BoltTicksToLive = 2;
    private int generation = -1;

    public int ActiveCount => active.Count;

    /// <summary>Names of the sheets currently playing (diagnostics and acceptance).</summary>
    public IEnumerable<string> Active { get { foreach (var fx in active) yield return fx.Sheet.Name; } }

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        TextureFilter = TextureFilterEnum.Nearest;
        fireball = Load("fireball", 36, 4, 136, 56, 112, 28, true);
        hit = Load("blunt-hit", 28, 5, 128, 88, 64, 72, false);
        heal = Load("heal-aoe", 88, 7, 256, 136, 128, 104, false);
        burst = Load("collect-burst", 42, 7, 112, 108, 56, 94, false);
    }

    private static Sheet Load(string name, int frames, int columns, int w, int h, int px, int py, bool loop) =>
        new(name, GD.Load<Texture2D>($"res://Assets/Effects/{name}.png"), frames, columns, new Vector2(w, h), new Vector2(px, py), 30f, loop);

    private Vector2 Feet(int x, int y) => new((x + 0.5f) * CellSize, (y + 1) * CellSize);

    private bool HasRoom => active.Count < MaxActive;

    private void Spawn(Fx fx)
    {
        if (HasRoom) active.Add(fx);
    }

    /// <summary>Drops every effect and correlation (world replaced by load or a new game).</summary>
    public void Reset()
    {
        active.Clear();
        pendingBolts.Clear();
    }

    public override void _Process(double delta)
    {
        if (generation != bridge.WorldGeneration)
        {
            generation = bridge.WorldGeneration;
            Reset(); // effects from an abandoned timeline must not land on the loaded world (FIX-R2 #11)
        }
        Handle(bridge.EventsThisFrame);
        Advance((float)delta);
        QueueRedraw();
    }

    /// <summary>Starts effects for a batch of drained events.</summary>
    public void Handle(IEnumerable<WorldEvent> events)
    {
        // Firebolts first, so the impact they cause is drawn when the fireball lands rather than at once. The suppression
        // entry is kept even when there is no room to draw the fireball, so the matching hit is never drawn early.
        long latest = long.MinValue;
        foreach (var e in events)
        {
            latest = System.Math.Max(latest, e.Tick);
            if (e.Kind != EventKinds.SpellCast) continue;
            if (e.Reason == (int)Spell.Firebolt)
            {
                if (pendingBolts.Count == MaxPendingBolts) pendingBolts.RemoveAt(0);
                pendingBolts.Add((e.Tick, e.Actor, e.Target));
                if (!HasRoom) continue; // no allocation or caster lookup for an effect that would be dropped (FIX-R2 #12)
                var to = Feet(e.X, e.Y) - new Vector2(0, 10);
                var from = bridge.Details(e.Actor) is { } c ? Feet(c.X, c.Y) - new Vector2(0, 12) : to;
                float flight = Mathf.Clamp(from.DistanceTo(to) / 160f, 0.12f, 0.5f);
                Spawn(new Fx { Sheet = fireball, Pos = from, From = from, To = to, Flight = flight, Scale = 0.6f });
            }
            else if (e.Reason == (int)Spell.Heal && HasRoom)
                Spawn(new Fx { Sheet = heal, Pos = Feet(e.X, e.Y), Scale = 0.6f });
        }
        foreach (var e in events)
        {
            if (e.Kind == EventKinds.Attacked)
            {
                int bolt = pendingBolts.IndexOf((e.Tick, e.Actor, e.Target));
                if (bolt >= 0) pendingBolts.RemoveAt(bolt); // the fireball's landing draws this impact
                else if (HasRoom) Spawn(new Fx { Sheet = hit, Pos = Feet(e.X, e.Y), Scale = 0.45f });
            }
            else if (e.Kind == EventKinds.AnimalTamed && HasRoom)
                Spawn(new Fx { Sheet = burst, Pos = Feet(e.X, e.Y) - new Vector2(0, 6), Scale = 0.5f });
        }
        if (latest != long.MinValue) pendingBolts.RemoveAll(b => b.Tick < latest - BoltTicksToLive);
    }

    public int PendingBoltCount => pendingBolts.Count;

    /// <summary>Ages effects by real time: projectiles fly and land, one-shots end after their last frame.</summary>
    public void Advance(float dt)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var fx = active[i];
            fx.Age += dt;
            if (fx.Flight > 0)
            {
                fx.Pos = fx.From.Lerp(fx.To, Mathf.Min(1f, fx.Age / fx.Flight));
                if (fx.Age >= fx.Flight)
                {
                    active.RemoveAt(i);
                    Spawn(new Fx { Sheet = hit, Pos = fx.To + new Vector2(0, 10), Scale = 0.5f, Tint = new Color(1f, 0.72f, 0.45f) });
                }
            }
            else if (!fx.Sheet.Loop && (int)(fx.Age * fx.Sheet.Fps) >= fx.Sheet.Frames)
                active.RemoveAt(i);
        }
    }

    public override void _Draw()
    {
        foreach (var fx in active)
        {
            var s = fx.Sheet;
            int frame = (int)(fx.Age * s.Fps) % s.Frames;
            var src = new Rect2(frame % s.Columns * s.Size.X, frame / s.Columns * s.Size.Y, s.Size.X, s.Size.Y);
            float angle = fx.Flight > 0 ? (fx.To - fx.From).Angle() : 0f; // the fireball sheet faces right
            DrawSetTransform(fx.Pos, angle, Vector2.One * fx.Scale);
            DrawTextureRectRegion(s.Texture, new Rect2(-s.Pivot, s.Size), src, fx.Tint);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
