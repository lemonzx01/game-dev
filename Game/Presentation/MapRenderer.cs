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

    private SimulationBridge bridge = null!;

    private ImageTexture terrain = null!;
    private WorldSnapshot? lastDrawn;

    public override void _Ready()
    {
        bridge = GetNode<SimulationBridge>(BridgePath);
        var map = bridge.Map;
        using var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                image.SetPixel(x, y, map.At(x, y) switch
                {
                    Terrain.Water => new Color("3b7494"),
                    Terrain.Grass => new Color("96ad63"),
                    Terrain.Forest => new Color("446849"),
                    Terrain.Mountain => new Color("807b74"),
                    Terrain.Snow => new Color("dce5df"),
                    _ => throw new System.InvalidOperationException("Unknown terrain")
                });
        terrain = ImageTexture.CreateFromImage(image);
        TextureFilter = TextureFilterEnum.Nearest;
        GetViewport().GetCamera2D()!.Position = new Vector2(map.Width, map.Height) * CellSize / 2;
    }

    public override void _Process(double delta)
    {
        if (lastDrawn != bridge.Snapshot) QueueRedraw();
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
                var position = new Vector2(row.X, row.Y) * CellSize;
                DrawRect(new Rect2(position, size), PlaceholderColor);
            }
        }
    }
}

