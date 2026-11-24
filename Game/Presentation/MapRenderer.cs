using System.Collections.Generic;
using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Draws the bridge's most recent real WorldSnapshot with the project's placeholder pixel-art atlases
// (Assets/Placeholders, CC0 / project-owned). Terrain is baked into one texture per map revision; resources,
// farm plots, towns and creatures are drawn from simulation read models each frame. It never invents entities,
// positions, stock or outcomes that are not in the simulation.
public partial class MapRenderer : Node2D
{
    [Export] public NodePath BridgePath = "../SimulationBridge";
    [Export] public float CellSize = 16f;
    [Export] public Color PlaceholderColor = new(0.9f, 0.85f, 0.2f);
    [Export] public NodePath ObserverPath = "../Observer";
    [Export] public float StartZoom = 2.5f;

    // Atlas regions (see Assets/CREDITS.md).
    private const int TileGrass = 0, TileShallow = 2, TileDeep = 3, TileForest = 4, TileMountain = 5, TileSnow = 6, TileFarmland = 7;
    private static readonly Rect2 Sheep = new(0, 0, 32, 32), Deer = new(32, 0, 32, 32), Wolf = new(64, 0, 32, 32), Storage = new(32, 32, 32, 32);
    private static readonly Rect2 Shadow = new(0, 0, 32, 32), Body = new(32, 0, 32, 32), Horns = new(64, 0, 32, 32), Hair = new(96, 0, 32, 32), Tunic = new(128, 0, 32, 32), Ring = new(224, 0, 32, 32);
    private static readonly Rect2 Oak = new(0, 0, 32, 32), Pine = new(32, 0, 32, 32), Berries = new(64, 0, 32, 32), EmptyBush = new(96, 0, 32, 32);
    private static readonly Rect2 CropsGrowing = new(0, 32, 32, 32), CropsRipe = new(32, 32, 32, 32), Stump = new(96, 32, 32, 32);

    private SimulationBridge bridge = null!;
    private ObserverController? observer;
    private Texture2D terrainAtlas = null!, creatures = null!, humanoid = null!, plants = null!;
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
        terrainAtlas = GD.Load<Texture2D>("res://Assets/Placeholders/terrain_tiles_16x16_placeholder.png");
        creatures = GD.Load<Texture2D>("res://Assets/Placeholders/creatures_buildings_placeholder.png");
        humanoid = GD.Load<Texture2D>("res://Assets/Placeholders/humanoid_modular_32x32_placeholder.png");
        plants = GD.Load<Texture2D>("res://Assets/Placeholders/plants_flora_placeholder.png");
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
        var atlas = terrainAtlas.GetImage();
        atlas.Convert(Image.Format.Rgba8);
        int t = (int)CellSize;
        using var image = Image.CreateEmpty(map.Width * t, map.Height * t, false, Image.Format.Rgba8);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                int tile = map.At(x, y) switch
                {
                    Terrain.Grass => TileGrass,
                    Terrain.Forest => TileForest,
                    Terrain.Mountain => TileMountain,
                    Terrain.Snow => TileSnow,
                    _ => Shore(map, x, y) ? TileShallow : TileDeep,
                };
                image.BlitRect(atlas, new Rect2I(tile * 16, 0, 16, 16), new Vector2I(x * t, y * t));
            }
        terrain = ImageTexture.CreateFromImage(image);
        builtRevision = bridge.MapRevision;
    }

    private static bool Shore(MapView map, int x, int y)
    {
        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < map.Width && ny < map.Height && map.At(nx, ny) != Terrain.Water) return true;
        }
        return false;
    }

    public override void _Process(double delta)
    {
        if (builtRevision != bridge.MapRevision) BuildTerrain();
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
        DrawTextureRect(terrain, new Rect2(Vector2.Zero, terrain.GetSize()), false);

        if (nodesTick != snapshot.Tick) { nodes = bridge.ResourceNodes(); nodesTick = snapshot.Tick; }
        foreach (var n in nodes)
        {
            if (n.Kind == ResourceKind.Food) DrawSprite(plants, n.Amount > 0 ? Berries : EmptyBush, n.X, n.Y, 1.1f);
            else if (n.Kind == ResourceKind.Wood) DrawSprite(plants, n.Amount == 0 ? Stump : ((n.X + n.Y) % 3 == 0 ? Pine : Oak), n.X, n.Y, 1.4f);
        }

        DrawSettlements();

        foreach (var chunk in snapshot.Chunks)
            foreach (EntityRow row in chunk)
                DrawCreature(row);
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
                if (crop > 0) DrawSprite(plants, crop >= 3 ? CropsRipe : CropsGrowing, x, y, 1f);
            }
            DrawSprite(creatures, Storage, town.X, town.Y, 2f);
            string label = $"{town.Name} ({town.Population})";
            int fontSize = (int)Mathf.Clamp(14 * scale, 8, 96);
            var at = new Vector2(town.X * CellSize - CellSize, (town.Y - 1.6f) * CellSize);
            DrawStringOutline(font, at, label, HorizontalAlignment.Left, -1, fontSize, Mathf.Max(1, fontSize / 7), new Color(0.05f, 0.05f, 0.08f));
            DrawString(font, at, label, HorizontalAlignment.Left, -1, fontSize, faction);
        }
    }

    private void DrawCreature(EntityRow row)
    {
        bool dead = row.LifeState == LifeStates.Dead;
        var deadTint = new Color(0.45f, 0.45f, 0.45f, 0.8f);
        if (observer is not null && row.Id.Value == observer.SelectedId)
            DrawSprite(humanoid, Ring, row.X, row.Y, 1.6f);
        switch (row.VisualKey)
        {
            case SpeciesCatalog.Sheep: DrawSprite(creatures, Sheep, row.X, row.Y, 1.2f, dead ? deadTint : null); break;
            case SpeciesCatalog.Deer: DrawSprite(creatures, Deer, row.X, row.Y, 1.3f, dead ? deadTint : null); break;
            case SpeciesCatalog.Wolf: DrawSprite(creatures, Wolf, row.X, row.Y, 1.3f, dead ? deadTint : null); break;
            case SpeciesCatalog.Rabbit: // no rabbit sprite yet: a small, tinted deer silhouette
                DrawSprite(creatures, Deer, row.X, row.Y, 0.8f, dead ? deadTint : new Color("e0cfa0")); break;
            case SpeciesCatalog.Human:
            case SpeciesCatalog.Demon:
                bool demon = row.VisualKey == SpeciesCatalog.Demon;
                DrawSprite(humanoid, Shadow, row.X, row.Y, 1.5f);
                DrawSprite(humanoid, Body, row.X, row.Y, 1.5f, dead ? deadTint : demon ? new Color("e06070") : new Color("f2c9a0"));
                DrawSprite(humanoid, Tunic, row.X, row.Y, 1.5f, dead ? deadTint : demon ? new Color("7a2030") : new Color("3f6fb8"));
                DrawSprite(humanoid, demon ? Horns : Hair, row.X, row.Y, 1.5f, dead ? deadTint : demon ? null : new Color("6b4a2a"));
                break;
            default:
                DrawRect(new Rect2(new Vector2(row.X, row.Y) * CellSize, Vector2.One * CellSize * 0.8f), dead ? deadTint : PlaceholderColor);
                break;
        }
    }
}
