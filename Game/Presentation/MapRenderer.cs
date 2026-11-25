using System.Collections.Generic;
using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Draws the bridge's most recent real WorldSnapshot with the project's pixel-art atlases
// (Assets/CREDITS.md). Terrain is baked into one texture per map revision; resources,
// farm plots, towns and creatures are drawn from simulation read models each frame. It never invents entities,
// positions, stock or outcomes that are not in the simulation.
public partial class MapRenderer : Node2D
{
    [Export] public NodePath BridgePath = "../SimulationBridge";
    [Export] public float CellSize = 16f;
    [Export] public Color PlaceholderColor = new(0.9f, 0.85f, 0.2f);
    [Export] public NodePath ObserverPath = "../Observer";
    [Export] public float StartZoom = 3f; // integer zoom keeps the 24 px character art crisp

    // Atlas regions (see Assets/CREDITS.md).
    private const int TileForest = 3, TileMountain = 4, TileSnow = 5, TileShallow = 6, TileDeep = 7, TileFarmland = 8;
    private const int ShoreFirst = 9, ShimmerFirst = 13;
    private static readonly Rect2 Ring = new(224, 0, 32, 32);
    private static Rect2 Prop(int slot) => new(slot * 32, 0, 32, 32);

    private SimulationBridge bridge = null!;
    private ObserverController? observer;
    private Texture2D terrainAtlas = null!, humanoid = null!, plants = null!, characters = null!;
    private ImageTexture terrain = null!;
    private int builtRevision = -1;
    private bool cameraPlaced;
    private IReadOnlyList<ResourceView> nodes = [];
    private long nodesTick = -1;

    public int BuiltRevision => builtRevision;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        observer = GetNodeOrNull<ObserverController>(ObserverPath);
        terrainAtlas = GD.Load<Texture2D>("res://Assets/Art/environment_tiles.png");
        humanoid = GD.Load<Texture2D>("res://Assets/Placeholders/humanoid_modular_32x32_placeholder.png");
        plants = GD.Load<Texture2D>("res://Assets/Art/environment_props.png");
        // Owner art (Assets/Source/owner-art-2026-11-25, imported by tools/art/import_owner_art.py). The previous generated
        // sheets (characters_24.png, dragon_48.png) stay in the repository as a fallback but are no longer drawn.
        characters = GD.Load<Texture2D>("res://Assets/Art/owner_sheet.png");
        TextureFilter = TextureFilterEnum.Nearest;
        BuildTerrain();
        var map = bridge.Map;
        var camera = GetViewport().GetCamera2D()!;
        camera.Position = new Vector2(map.Width, map.Height) * CellSize / 2;
        camera.Zoom = Vector2.One * StartZoom; // close enough to see creatures; wheel zooms out to the whole map
    }

    public static Color TerrainColor(Terrain t) => t switch
    {
        Terrain.Water => new Color("3b7494"),
        Terrain.Grass => new Color("96ad63"),
        Terrain.Forest => new Color("446849"),
        Terrain.Mountain => new Color("807b74"),
        Terrain.Snow => new Color("dce5df"),
        _ => throw new System.InvalidOperationException("Unknown terrain")
    };

    // Bakes the tile grid into one texture (start, God terrain edits, load). Water touching land is shallow.
    private void BuildTerrain()
    {
        var map = bridge.Map;
        using var atlas = terrainAtlas.GetImage();
        atlas.Convert(Image.Format.Rgba8);
        const int t = 16;
        using var image = Image.CreateEmpty(map.Width * t, map.Height * t, false, Image.Format.Rgba8);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var kind = map.At(x, y);
                int shore = kind == Terrain.Water ? Shore(map, x, y) : 0;
                int tile = kind switch
                {
                    Terrain.Grass => (int)(CellHash(x,y) % 3),
                    Terrain.Forest => TileForest,
                    Terrain.Mountain => TileMountain,
                    Terrain.Snow => TileSnow,
                    _ => shore != 0 ? TileShallow : TileDeep,
                };
                var dest = new Vector2I(x * t, y * t);
                image.BlitRect(atlas, new Rect2I(tile * t, 0, t, t), dest);
                for (int side = 0; side < 4; side++)
                    if ((shore & (1 << side)) != 0)
                        image.BlendRect(atlas, new Rect2I((ShoreFirst + side) * t, 0, t, t), dest);
            }
        terrain = ImageTexture.CreateFromImage(image);
        builtRevision = bridge.MapRevision;
    }

    private static uint CellHash(int x, int y)
    {
        uint h = unchecked((uint)x * 0x85ebca6bu ^ (uint)y * 0xc2b2ae35u);
        h ^= h >> 16;
        return unchecked(h * 0x7feb352du) ^ (h >> 11);
    }

    // N, W, S, E match the counterclockwise rotations in the atlas generator.
    private static int Shore(MapView map, int x, int y) =>
        (y > 0 && map.At(x, y - 1) != Terrain.Water ? 1 : 0) |
        (x > 0 && map.At(x - 1, y) != Terrain.Water ? 2 : 0) |
        (y + 1 < map.Height && map.At(x, y + 1) != Terrain.Water ? 4 : 0) |
        (x + 1 < map.Width && map.At(x + 1, y) != Terrain.Water ? 8 : 0);

    private void DrawWaterShimmer()
    {
        var map = bridge.Map;
        var local = GetGlobalTransformWithCanvas().AffineInverse() * GetViewportRect();
        int left = Mathf.Max(0, Mathf.FloorToInt(local.Position.X / CellSize));
        int top = Mathf.Max(0, Mathf.FloorToInt(local.Position.Y / CellSize));
        int right = Mathf.Min(map.Width, Mathf.CeilToInt(local.End.X / CellSize));
        int bottom = Mathf.Min(map.Height, Mathf.CeilToInt(local.End.Y / CellSize));
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                if (map.At(x, y) != Terrain.Water) continue;
                uint hash = CellHash(x, y);
                if (hash % 7 != 0) continue;
                int frame = ((int)(clock * 2) + (int)(hash % 3)) % 3;
                DrawTextureRectRegion(terrainAtlas, new Rect2(new Vector2(x, y) * CellSize, Vector2.One * CellSize),
                    new Rect2((ShimmerFirst + frame) * 16, 0, 16, 16));
            }
    }

    public override void _Process(double delta)
    {
        if (builtRevision != bridge.MapRevision) BuildTerrain();
        UpdateMotion((float)delta);
        if (!cameraPlaced && bridge.Settlements() is { Count: > 0 } towns) // towns appear on the first tick
        {
            GetViewport().GetCamera2D()!.Position = new Vector2(towns[0].X + 0.5f, towns[0].Y + 0.5f) * CellSize;
            cameraPlaced = true;
        }
        QueueRedraw(); // creatures move and the selection ring follows input
    }

    // Sprite of `size` tiles wide, bottom-centred on the tile.
    private void DrawSprite(Texture2D atlas, Rect2 region, int x, int y, float size, Color? tint = null)
    {
        float w = size * CellSize;
        var dest = new Rect2(new Vector2((x + 0.5f) * CellSize - w / 2, (y + 1) * CellSize - w), new Vector2(w, w));
        DrawTextureRectRegion(atlas, dest, region, tint ?? Colors.White);
    }

    public override void _Draw()
    {
        WorldSnapshot snapshot = bridge.Snapshot;
        if (snapshot is null)
            return;
        DrawTextureRect(terrain, new Rect2(Vector2.Zero, new Vector2(bridge.Map.Width, bridge.Map.Height) * CellSize), false);
        DrawWaterShimmer();

        if (nodesTick != snapshot.Tick) { nodes = bridge.ResourceNodes(); nodesTick = snapshot.Tick; }
        foreach (var n in nodes)
        {
            if (n.Kind == ResourceKind.Food) DrawSprite(plants, Prop(n.Amount > 0 ? 3 : 4), n.X, n.Y, 2f);
            else if (n.Kind == ResourceKind.Wood) DrawSprite(plants, Prop(n.Amount == 0 ? 2 : CellHash(n.X, n.Y) % 3 == 0 ? 1 : 0), n.X, n.Y, 2f);
        }

        DrawSettlements();

        // Creatures sorted by drawn y so lower ones overlap higher ones.
        drawOrder.Clear();
        foreach (var chunk in snapshot.Chunks)
            foreach (EntityRow row in chunk)
                drawOrder.Add(row);
        drawOrder.Sort((a, b) => DrawnAt(a).Y.CompareTo(DrawnAt(b).Y));
        foreach (var row in drawOrder) DrawCreature(row);
    }

    // Farm plots (farmland + growing/ripe crops), the storage shed at the centre, and a zoom-aware name.
    private void DrawSettlements()
    {
        var font = ThemeDB.FallbackFont;
        float scale = 1f / Mathf.Max(0.05f, GetViewport().GetCamera2D()?.Zoom.X ?? 1f);
        foreach (var town in bridge.Settlements())
        {
            var faction = town.Faction == Faction.Human ? new Color("7fb0ff") : new Color("ff7a7a");
            foreach (var (x, y, crop) in bridge.FarmPlots(town.Id))
            {
                DrawTextureRectRegion(terrainAtlas, new Rect2(new Vector2(x, y) * CellSize, Vector2.One * CellSize), new Rect2(TileFarmland * 16, 0, 16, 16));
                if (crop > 0) DrawSprite(plants, Prop(crop >= 3 ? 6 : 5), x, y, 2f);
            }
            DrawSprite(plants, Prop(town.Faction == Faction.Human ? 7 : 8), town.X, town.Y, 2f);
            string label = $"{town.Name} ({town.Population})";
            int fontSize = (int)Mathf.Clamp(14 * scale, 8, 96);
            var at = new Vector2(town.X * CellSize - CellSize, (town.Y - 1.6f) * CellSize);
            DrawStringOutline(font, at, label, HorizontalAlignment.Left, -1, fontSize, Mathf.Max(1, fontSize / 7), new Color(0.05f, 0.05f, 0.08f));
            DrawString(font, at, label, HorizontalAlignment.Left, -1, fontSize, faction);
        }
    }

    // ---- Character animation (presentation only: reads rows, never changes the simulation) ----
    // Sheet Assets/Art/characters_24.png (tools/art/make_characters.py): 24 px frames, columns below, rows per species/variant.
    // Sheet frames are 104×104 world pixels, bottom-centred, already at real proportions (tools/art/import_owner_art.py):
    // a person about one cell, the town store about two people, a dragon about four people tall. Drawn 1:1, nearest.
    private const int Frame = 104, ColIdle = 0, ColWalk = 2, ColWork = 6, ColAttack = 8, ColRead = 10, ColDead = 12;
    private const int HumanRows = 4, GuardRow = 4, DemonRow0 = 5, DemonRows = 4, SheepRow = 9, DeerRow = 10, RabbitRow = 11, WolfRow = 12, DragonRow = 13;

    // Guards wear the guard sprite. Job is not on the snapshot row, so it is read from details at most every 20 ticks per person.
    private readonly Dictionary<long, (long Tick, bool Guard)> guardCache = [];

    private bool IsGuard(EntityRow row)
    {
        long now = bridge.Snapshot.Tick;
        if (guardCache.TryGetValue(row.Id.Value, out var c) && now - c.Tick < 20 && now >= c.Tick) return c.Guard;
        bool guard = bridge.Details(row.Id.Value) is { Job: Job.Guard };
        if (guardCache.Count > 4096) guardCache.Clear();
        guardCache[row.Id.Value] = (now, guard);
        return guard;
    }

    private sealed class Motion
    {
        public Vector2 Pos, Target;
        public bool Left;
        public long SeenTick;
    }

    private readonly Dictionary<long, Motion> motion = [];
    private readonly List<EntityRow> drawOrder = [];
    private long motionTick = -1;
    private float clock;

    // Each new tick sets targets (and facing from the step direction); positions glide toward them between ticks.
    private void UpdateMotion(float delta)
    {
        clock += delta;
        var snapshot = bridge.Snapshot;
        if (snapshot is not null && snapshot.Tick != motionTick)
        {
            motionTick = snapshot.Tick;
            int live = 0;
            foreach (var chunk in snapshot.Chunks)
                foreach (var row in chunk)
                {
                    live++;
                    var target = new Vector2(row.X, row.Y);
                    if (!motion.TryGetValue(row.Id.Value, out var m))
                        motion[row.Id.Value] = m = new Motion { Pos = target, Target = target, Left = (row.Id.Value & 1) == 1 };
                    if (target.X != m.Target.X) m.Left = target.X < m.Target.X;
                    m.Target = target;
                    if (m.Pos.DistanceTo(target) > 3) m.Pos = target; // teleport, load or long catch-up: no sliding across the map
                    m.SeenTick = snapshot.Tick;
                }
            if (motion.Count > live + 64)
            {
                var gone = new List<long>();
                foreach (var (id, m) in motion) if (m.SeenTick != snapshot.Tick) gone.Add(id);
                foreach (var id in gone) motion.Remove(id);
            }
        }
        float k = 1f - Mathf.Exp(-delta * 14f);
        foreach (var m in motion.Values) m.Pos = m.Pos.Lerp(m.Target, k);
    }

    private Vector2 DrawnAt(EntityRow row) => motion.TryGetValue(row.Id.Value, out var m) ? m.Pos : new Vector2(row.X, row.Y);

    private static int Hash(long id) => (int)(((ulong)id * 2654435761UL >> 7) & 0x7fffffff);

    private int SheetRow(EntityRow row) => row.VisualKey switch
    {
        SpeciesCatalog.Human => row.LifeState == LifeStates.Alive && IsGuard(row) ? GuardRow : Hash(row.Id.Value) % HumanRows,
        SpeciesCatalog.Demon => DemonRow0 + Hash(row.Id.Value) % DemonRows,
        SpeciesCatalog.Sheep => SheepRow,
        SpeciesCatalog.Deer => DeerRow,
        SpeciesCatalog.Rabbit => RabbitRow,
        SpeciesCatalog.Wolf => WolfRow,
        SpeciesCatalog.Dragon => DragonRow,
        _ => -1,
    };

    private int SheetColumn(EntityRow row, bool moving, bool animal)
    {
        if (row.LifeState == LifeStates.Dead) return ColDead;
        float t = clock + (Hash(row.Id.Value) % 97) * 0.13f;
        var action = (ActionKind)row.Action;
        if (moving) return ColWalk + (int)(t * (action == ActionKind.Flee ? 12f : 8f)) % 4;
        return action switch
        {
            ActionKind.Work => ColWork + (int)(t * 3f) % 2,
            ActionKind.Read => ColRead + (int)(t * 0.8f) % 2,
            ActionKind.Hunt or ActionKind.Defend => ColAttack + (int)(t * 4f) % 2,
            ActionKind.Eat or ActionKind.Drink when animal => ColWork + (int)(t * 1.5f) % 2,
            ActionKind.Rest => ColIdle,
            _ => ColIdle + (int)(t * 1.5f) % 2,
        };
    }

    private void DrawCreature(EntityRow row)
    {
        bool dead = row.LifeState == LifeStates.Dead;
        var at = DrawnAt(row);
        motion.TryGetValue(row.Id.Value, out var m);
        bool moving = !dead && m is not null && m.Pos.DistanceSquaredTo(m.Target) > 0.0004f;
        if (observer is not null && row.Id.Value == observer.SelectedId)
        {
            float w = 1.6f * CellSize;
            DrawTextureRectRegion(humanoid, new Rect2(new Vector2((at.X + 0.5f) * CellSize - w / 2, (at.Y + 1) * CellSize - w), new Vector2(w, w)), Ring);
        }
        if (SpriteOf(row) is not { } sprite)
        {
            DrawRect(new Rect2(at * CellSize, Vector2.One * CellSize * 0.8f), dead ? new Color(0.45f, 0.45f, 0.45f, 0.8f) : PlaceholderColor);
            return;
        }
        // Bottom-centred on the cell with feet on its lower edge; mirrored when facing left.
        float size = sprite.Region.Size.X * sprite.Scale;
        var origin = new Vector2((at.X + 0.5f) * CellSize, (at.Y + 1) * CellSize + 2);
        DrawSetTransform(origin, 0, new Vector2(sprite.Mirrored ? -1 : 1, 1));
        DrawTextureRectRegion(sprite.Sheet, new Rect2(-size / 2, -size, size, size), sprite.Region,
            dead ? new Color(0.78f, 0.76f, 0.8f, 0.9f) : Colors.White);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>
    /// The sheet, source frame and facing a creature is drawn with this frame (null: no sprite, placeholder square).
    /// Every creature uses the owner sheet (Assets/Art/owner_sheet.png); Scale converts art pixels to world pixels.
    /// Also used by the acceptance test to prove what is actually drawn.
    /// </summary>
    public (Texture2D Sheet, Rect2 Region, bool Mirrored, float Scale)? SpriteOf(EntityRow row)
    {
        int sheetRow = SheetRow(row);
        if (sheetRow < 0) return null;
        bool dead = row.LifeState == LifeStates.Dead;
        motion.TryGetValue(row.Id.Value, out var m);
        bool moving = !dead && m is not null && m.Pos.DistanceSquaredTo(m.Target) > 0.0004f;
        int col = SheetColumn(row, moving, sheetRow >= SheepRow);
        return (characters, new Rect2(col * Frame, sheetRow * Frame, Frame, Frame), m is { Left: true }, 1f);
    }
}
