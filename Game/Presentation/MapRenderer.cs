using FantasyWorld.Simulation;
using Game.Integration;
using Godot;

namespace Game.Presentation;

// Draws the bridge's most recent real WorldSnapshot. Rows are drawn as flat
// placeholder squares until species visuals exist (T007 allows placeholders);
// it never invents entities, positions or outcomes not present in the snapshot.
public partial class MapRenderer : Node2D
{
    [Export] public NodePath BridgePath = "../SimulationBridge";
    [Export] public float CellSize = 16f;
    [Export] public Color PlaceholderColor = new(0.9f, 0.85f, 0.2f);
    [Export] public NodePath ObserverPath = "../Observer";

    private ObserverController? observer;

    // Placeholder colours per species until sprite atlases replace them.
    private static Color SpeciesColor(int key) => key switch
    {
        SpeciesCatalog.Deer => new Color("b07a45"),
        SpeciesCatalog.Sheep => new Color("f2f0e6"),
        SpeciesCatalog.Rabbit => new Color("d9c38c"),
        SpeciesCatalog.Wolf => new Color("3d3f45"),
        _ => new Color(0.9f, 0.85f, 0.2f),
    };

    private SimulationBridge bridge = null!;

    private ImageTexture terrain = null!;
    private WorldSnapshot? lastDrawn;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        observer = GetNodeOrNull<ObserverController>(ObserverPath);
        var map = bridge.Map;
        BuildTerrain();
        TextureFilter = TextureFilterEnum.Nearest;
        var camera = GetViewport().GetCamera2D()!;
        camera.Position = new Vector2(map.Width, map.Height) * CellSize / 2;
        var view = GetViewport().GetVisibleRect().Size;
        float fit = Mathf.Min(view.X / (map.Width * CellSize), view.Y / (map.Height * CellSize));
        camera.Zoom = Vector2.One * Mathf.Clamp(fit, 0.25f, 4f); // start with the whole map in view
    }

    private int builtRevision = -1;

    public static Color TerrainColor(Terrain t) => t switch
    {
        Terrain.Water => new Color("3b7494"),
        Terrain.Grass => new Color("96ad63"),
        Terrain.Forest => new Color("446849"),
        Terrain.Mountain => new Color("807b74"),
        Terrain.Snow => new Color("dce5df"),
        _ => throw new System.InvalidOperationException("Unknown terrain")
    };

    // Rebuilds the terrain texture from the bridge's current map (start, God terrain edits, load).
    private void BuildTerrain()
    {
        var map = bridge.Map;
        using var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                image.SetPixel(x, y, TerrainColor(map.At(x, y)));
        terrain = ImageTexture.CreateFromImage(image);
        builtRevision = bridge.MapRevision;
    }

    public int BuiltRevision => builtRevision;

    public override void _Process(double delta)
    {
        if (builtRevision != bridge.MapRevision) BuildTerrain();
        if (!ReferenceEquals(lastDrawn, bridge.Snapshot) || observer is not null) QueueRedraw(); // selection outline follows input
    }

    public override void _Draw()
    {
        WorldSnapshot snapshot = bridge.Snapshot;
        if (snapshot is null)
            return;
        lastDrawn = snapshot;
        DrawTextureRect(terrain, new Rect2(Vector2.Zero, terrain.GetSize() * CellSize), false);
        var size = new Vector2(CellSize, CellSize) * 0.8f;
        foreach (var chunk in snapshot.Chunks)
        {
            foreach (EntityRow row in chunk)
            {
                var position = new Vector2(row.X, row.Y) * CellSize + new Vector2(CellSize, CellSize) * 0.1f;
                var color = row.LifeState == LifeStates.Dead ? new Color(0.45f, 0.45f, 0.45f) : SpeciesColor(row.VisualKey);
                DrawRect(new Rect2(position, size), color);
                if (observer is not null && row.Id.Value == observer.SelectedId)
                    DrawRect(new Rect2(position - Vector2.One * 2, size + Vector2.One * 4), new Color(1f, 0.15f, 0.15f), false, 2f);
            }
        }
    }
}

