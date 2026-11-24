using System;
using FantasyWorldSimulator.Sandbox.PresentationTest.MockData;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

public enum MockSeasonVisual
{
    Spring,
    Summer,
    Autumn,
    Winter,
    Night
}

public enum MockOverlayMode
{
    None,
    Population,
    Threat
}

/// <summary>
/// Chunked 16x16 tile terrain renderer with frustum chunk culling,
/// dirty-chunk version rebuilds, and presentation-only season/night tinting.
/// </summary>
public partial class ChunkedTerrainPreview : Node2D
{
    public const int ChunkTiles = 16;
    private const int TileSize = MockWorldFixture.TileSize;
    private const int ChunksX = MockWorldFixture.MapTilesX / ChunkTiles;
    private const int ChunksY = MockWorldFixture.MapTilesY / ChunkTiles;

    private sealed class TerrainChunk
    {
        public int Cx;
        public int Cy;
        public Rect2 WorldBounds;
        public MultiMeshInstance2D MeshNode = null!;
        public int BuiltVersion = -1;
        public int TargetVersion = 1;
    }

    private readonly TerrainChunk[,] _chunks = new TerrainChunk[ChunksX, ChunksY];
    private MockTileKind[,] _grid = null!;
    private Texture2D _terrainAtlas = null!;
    private Shader _multiMeshShader = null!;
    private ArrayMesh _tileQuad = null!;

    public int VisibleChunkCount { get; private set; }
    public int DirtyRebuildsCount { get; private set; }
    public int TotalChunkCount => ChunksX * ChunksY;

    public void Initialize(MockTileKind[,] grid, Texture2D terrainAtlas, Shader multiMeshShader)
    {
        _grid = grid;
        _terrainAtlas = terrainAtlas;
        _multiMeshShader = multiMeshShader;
        _tileQuad = SpriteRenderBenchmark.CreateTopDownQuadMesh(TileSize, TileSize);

        for (int cy = 0; cy < ChunksY; cy++)
        {
            for (int cx = 0; cx < ChunksX; cx++)
            {
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                    UseColors = true,
                    UseCustomData = true,
                    Mesh = _tileQuad,
                    InstanceCount = ChunkTiles * ChunkTiles,
                    VisibleInstanceCount = ChunkTiles * ChunkTiles
                };

                var node = new MultiMeshInstance2D
                {
                    Name = $"TerrainChunk_{cx}_{cy}",
                    Texture = _terrainAtlas,
                    TextureFilter = TextureFilterEnum.Nearest,
                    Material = new ShaderMaterial { Shader = _multiMeshShader },
                    Multimesh = mm
                };
                AddChild(node);

                var chunk = new TerrainChunk
                {
                    Cx = cx,
                    Cy = cy,
                    WorldBounds = new Rect2(
                        cx * ChunkTiles * TileSize,
                        cy * ChunkTiles * TileSize,
                        ChunkTiles * TileSize,
                        ChunkTiles * TileSize),
                    MeshNode = node
                };
                _chunks[cx, cy] = chunk;
                RebuildChunk(chunk);
            }
        }
    }

    public void MarkTileDirty(int tx, int ty, MockTileKind newTile)
    {
        if (tx < 0 || ty < 0 || tx >= MockWorldFixture.MapTilesX || ty >= MockWorldFixture.MapTilesY)
        {
            return;
        }
        _grid[tx, ty] = newTile;
        _chunks[tx / ChunkTiles, ty / ChunkTiles].TargetVersion++;
    }

    public void UpdateVisibleChunks(Rect2 visibleWorldRect)
    {
        int visible = 0;
        for (int cy = 0; cy < ChunksY; cy++)
        {
            for (int cx = 0; cx < ChunksX; cx++)
            {
                var chunk = _chunks[cx, cy];
                bool isVisible = visibleWorldRect.Intersects(chunk.WorldBounds);
                chunk.MeshNode.Visible = isVisible;
                if (!isVisible)
                {
                    continue;
                }

                visible++;
                if (chunk.BuiltVersion != chunk.TargetVersion)
                {
                    RebuildChunk(chunk);
                }
            }
        }
        VisibleChunkCount = visible;
    }

    public static Color GetSeasonModulateColor(MockSeasonVisual season)
    {
        return season switch
        {
            MockSeasonVisual.Summer => new Color(1.06f, 1.00f, 0.88f),
            MockSeasonVisual.Autumn => new Color(1.05f, 0.86f, 0.72f),
            MockSeasonVisual.Winter => new Color(0.82f, 0.90f, 1.06f),
            MockSeasonVisual.Night => new Color(0.42f, 0.48f, 0.72f),
            _ => Colors.White
        };
    }

    private void RebuildChunk(TerrainChunk chunk)
    {
        var mm = chunk.MeshNode.Multimesh;
        int startX = chunk.Cx * ChunkTiles;
        int startY = chunk.Cy * ChunkTiles;
        int idx = 0;

        for (int ly = 0; ly < ChunkTiles; ly++)
        {
            for (int lx = 0; lx < ChunkTiles; lx++)
            {
                int tx = startX + lx;
                int ty = startY + ly;
                int tile = (int)_grid[tx, ty];
                mm.SetInstanceTransform2D(idx, new Transform2D(0f, new Vector2(tx * TileSize + 8f, ty * TileSize + 8f)));
                mm.SetInstanceColor(idx, Colors.White);
                mm.SetInstanceCustomData(idx, new Color(tile / 8f, 0f, 1f / 8f, 1f));
                idx++;
            }
        }

        chunk.BuiltVersion = chunk.TargetVersion;
        DirtyRebuildsCount++;
    }
}
