using System;
using System.Collections.Generic;
using System.Diagnostics;
using FantasyWorldSimulator.Sandbox.PresentationTest.MockData;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

public enum RenderOptimizationMode
{
    AutoLod,
    MultiMeshBatch,
    MultiMeshBulkBuffer,
    CulledNodePool,
    NaiveNodes
}

public readonly record struct RenderBenchmarkMetrics(
    RenderOptimizationMode ActiveStrategy,
    int TotalEntities,
    int VisibleEntities,
    int ActiveSpriteNodes,
    double UpdateMs,
    long ManagedBytes);

/// <summary>
/// Compares 3 rendering strategies on mock sprites:
/// 1) MultiMeshBatch (1 draw call for all visible sprites on an atlas)
/// 2) CulledNodePool (fixed-size pool of Sprite2D only for visible frustum)
/// 3) NaiveNodes (1 permanent Sprite2D per entity — demonstrates why Rule 5 forbids this)
/// </summary>
public partial class SpriteRenderBenchmark : Node2D
{
    public const int MaxPoolActors = 300;
    private const float AutoLodZoomThreshold = 0.75f;
    private const int AutoLodVisibleThreshold = 250;

    private readonly List<Sprite2D> _actorPool = new(MaxPoolActors);
    private readonly List<Sprite2D> _naiveNodes = new();
    private readonly List<int> _visibleIndices = new(5120);

    private Node2D _poolRoot = null!;
    private Node2D _naiveRoot = null!;
    private MultiMeshInstance2D _humanoidBatch = null!;
    private MultiMeshInstance2D _creatureBatch = null!;
    private float[] _humanoidBulkBuf = Array.Empty<float>();
    private float[] _creatureBulkBuf = Array.Empty<float>();

    private Texture2D _humanoidAtlas = null!;
    private Texture2D _creatureAtlas = null!;
    private Shader _multiMeshShader = null!;

    private IReadOnlyList<MockEntityVisual> _entities = Array.Empty<MockEntityVisual>();
    public RenderOptimizationMode RequestedMode { get; set; } = RenderOptimizationMode.AutoLod;
    public RenderBenchmarkMetrics LastMetrics { get; private set; }

    public void Initialize(Texture2D humanoidAtlas, Texture2D creatureAtlas, Shader multiMeshShader)
    {
        _humanoidAtlas = humanoidAtlas;
        _creatureAtlas = creatureAtlas;
        _multiMeshShader = multiMeshShader;

        _poolRoot = new Node2D { Name = "CulledActorPoolRoot" };
        AddChild(_poolRoot);

        _naiveRoot = new Node2D { Name = "NaiveNodesRoot" };
        AddChild(_naiveRoot);

        _humanoidBatch = CreateBatchNode("HumanoidMultiMesh", _humanoidAtlas, 32f, 32f, 5000);
        _creatureBatch = CreateBatchNode("CreatureMultiMesh", _creatureAtlas, 32f, 32f, 2000);
        _humanoidBulkBuf = new float[_humanoidBatch.Multimesh.InstanceCount * 16];
        _creatureBulkBuf = new float[_creatureBatch.Multimesh.InstanceCount * 16];
        AddChild(_humanoidBatch);
        AddChild(_creatureBatch);

        for (int i = 0; i < MaxPoolActors; i++)
        {
            var s = new Sprite2D
            {
                Visible = false,
                Centered = true,
                RegionEnabled = true,
                TextureFilter = TextureFilterEnum.Nearest
            };
            _poolRoot.AddChild(s);
            _actorPool.Add(s);
        }
    }

    public void SetEntities(IReadOnlyList<MockEntityVisual> entities)
    {
        _entities = entities;
        ClearNaiveNodes();
    }

    public RenderBenchmarkMetrics UpdateRender(Rect2 visibleRect, float cameraZoom, float elapsedSec, bool orbitPaused, float speedScale)
    {
        var sw = Stopwatch.StartNew();

        _visibleIndices.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            Vector2 pos = ResolvePreviewPos(_entities[i], elapsedSec, orbitPaused, speedScale);
            if (visibleRect.HasPoint(pos))
            {
                _visibleIndices.Add(i);
            }
        }

        RenderOptimizationMode resolved = RequestedMode;
        if (resolved == RenderOptimizationMode.AutoLod)
        {
            resolved = (cameraZoom < AutoLodZoomThreshold || _visibleIndices.Count > AutoLodVisibleThreshold)
                ? RenderOptimizationMode.MultiMeshBulkBuffer
                : RenderOptimizationMode.CulledNodePool;
        }

        int activeSpriteNodes = 0;
        switch (resolved)
        {
            case RenderOptimizationMode.MultiMeshBatch:
                HidePool();
                ClearNaiveNodes();
                RenderMultiMesh(elapsedSec, orbitPaused, speedScale);
                activeSpriteNodes = 2; // Two MultiMeshInstance2D nodes total.
                break;

            case RenderOptimizationMode.MultiMeshBulkBuffer:
                HidePool();
                ClearNaiveNodes();
                RenderMultiMeshBulk(elapsedSec, orbitPaused, speedScale);
                activeSpriteNodes = 2;
                break;

            case RenderOptimizationMode.CulledNodePool:
                HideMultiMesh();
                ClearNaiveNodes();
                activeSpriteNodes = RenderCulledPool(elapsedSec, orbitPaused, speedScale);
                break;

            case RenderOptimizationMode.NaiveNodes:
                HidePool();
                HideMultiMesh();
                activeSpriteNodes = RenderNaiveNodes(elapsedSec, orbitPaused, speedScale);
                break;
        }

        sw.Stop();
        LastMetrics = new RenderBenchmarkMetrics(
            resolved,
            _entities.Count,
            _visibleIndices.Count,
            activeSpriteNodes,
            sw.Elapsed.TotalMilliseconds,
            GC.GetTotalMemory(false));
        return LastMetrics;
    }

    public static Vector2 ResolvePreviewPos(MockEntityVisual entity, float elapsedSec, bool orbitPaused, float speedScale)
    {
        if (!entity.IsOrbitPreview || orbitPaused)
        {
            return entity.BasePosition;
        }

        float angle = elapsedSec * speedScale * 0.9f + (entity.Id * 0.7f);
        return entity.BasePosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 28f;
    }

    private void RenderMultiMesh(float elapsedSec, bool orbitPaused, float speedScale)
    {
        _humanoidBatch.Visible = true;
        _creatureBatch.Visible = true;

        var hMesh = _humanoidBatch.Multimesh;
        var cMesh = _creatureBatch.Multimesh;

        int hCount = 0;
        int cCount = 0;

        for (int idx = 0; idx < _visibleIndices.Count; idx++)
        {
            var e = _entities[_visibleIndices[idx]];
            Vector2 pos = ResolvePreviewPos(e, elapsedSec, orbitPaused, speedScale);

            if (e.Species is MockSpeciesKind.Human or MockSpeciesKind.Demon)
            {
                if (hCount >= hMesh.InstanceCount) continue;
                hMesh.SetInstanceTransform2D(hCount, new Transform2D(0f, pos));
                Color tint = e.LifeState == MockLifeState.Dead
                    ? new Color(0.55f, 0.55f, 0.6f)
                    : (e.Species == MockSpeciesKind.Demon ? new Color(0.92f, 0.42f, 0.44f) : new Color(0.95f, 0.85f, 0.75f));
                hMesh.SetInstanceColor(hCount, tint);
                // UV rect inside 256x64 humanoid atlas: cell (1,0) alive or (3,1) dead.
                Color uvRect = e.LifeState == MockLifeState.Dead
                    ? new Color(3f * 32f / 256f, 32f / 64f, 32f / 256f, 32f / 64f)
                    : new Color(32f / 256f, 0f, 32f / 256f, 32f / 64f);
                hMesh.SetInstanceCustomData(hCount, uvRect);
                hCount++;
            }
            else
            {
                if (cCount >= cMesh.InstanceCount) continue;
                float scale = e.Species == MockSpeciesKind.TrueDragon ? 2.0f : 1.0f;
                var xf = new Transform2D(0f, new Vector2(scale, scale), 0f, pos);
                cMesh.SetInstanceTransform2D(cCount, xf);
                cMesh.SetInstanceColor(cCount, Colors.White);
                Color uvRect = e.Species switch
                {
                    MockSpeciesKind.Sheep => new Color(0f, 0f, 32f / 256f, 32f / 64f),
                    MockSpeciesKind.Deer => new Color(32f / 256f, 0f, 32f / 256f, 32f / 64f),
                    MockSpeciesKind.Wolf => new Color(64f / 256f, 0f, 32f / 256f, 32f / 64f),
                    _ => new Color(128f / 256f, 0f, 64f / 256f, 64f / 64f) // TrueDragon 64x64 slot
                };
                cMesh.SetInstanceCustomData(cCount, uvRect);
                cCount++;
            }
        }

        hMesh.VisibleInstanceCount = hCount;
        cMesh.VisibleInstanceCount = cCount;
    }

    private void RenderMultiMeshBulk(float elapsedSec, bool orbitPaused, float speedScale)
    {
        _humanoidBatch.Visible = true;
        _creatureBatch.Visible = true;

        var hMesh = _humanoidBatch.Multimesh;
        var cMesh = _creatureBatch.Multimesh;

        int hCount = 0;
        int cCount = 0;

        for (int idx = 0; idx < _visibleIndices.Count; idx++)
        {
            var e = _entities[_visibleIndices[idx]];
            Vector2 pos = ResolvePreviewPos(e, elapsedSec, orbitPaused, speedScale);

            if (e.Species is MockSpeciesKind.Human or MockSpeciesKind.Demon)
            {
                if (hCount >= hMesh.InstanceCount) continue;
                Color tint = e.LifeState == MockLifeState.Dead
                    ? new Color(0.55f, 0.55f, 0.6f)
                    : (e.Species == MockSpeciesKind.Demon ? new Color(0.92f, 0.42f, 0.44f) : new Color(0.95f, 0.85f, 0.75f));
                bool stepPose = e.IsOrbitPreview && !orbitPaused && ((((int)(elapsedSec * speedScale * 4f) + e.Id) & 1) == 1);
                float uvX = e.LifeState == MockLifeState.Dead ? (96f / 256f) : (stepPose ? 0f : (32f / 256f));
                float uvY = e.LifeState == MockLifeState.Dead || stepPose ? (32f / 64f) : 0f;
                WriteBulkInstance(_humanoidBulkBuf, hCount, 1f, pos, tint, uvX, uvY, 32f / 256f, 32f / 64f);
                hCount++;
            }
            else
            {
                if (cCount >= cMesh.InstanceCount) continue;
                float scale = e.Species == MockSpeciesKind.TrueDragon ? 2.0f : 1.0f;
                bool dragonBreath = e.Species == MockSpeciesKind.TrueDragon && !orbitPaused && (((int)(elapsedSec * 2f)) % 4 == 0);
                (float uvX, float uvW, float uvH) = e.Species switch
                {
                    MockSpeciesKind.Sheep => (0f, 32f / 256f, 32f / 64f),
                    MockSpeciesKind.Deer => (32f / 256f, 32f / 256f, 32f / 64f),
                    MockSpeciesKind.Wolf => (64f / 256f, 32f / 256f, 32f / 64f),
                    _ => (dragonBreath ? (192f / 256f) : (128f / 256f), 64f / 256f, 64f / 64f)
                };
                WriteBulkInstance(_creatureBulkBuf, cCount, scale, pos, Colors.White, uvX, 0f, uvW, uvH);
                cCount++;
            }
        }

        hMesh.Buffer = _humanoidBulkBuf;
        cMesh.Buffer = _creatureBulkBuf;
        hMesh.VisibleInstanceCount = hCount;
        cMesh.VisibleInstanceCount = cCount;
    }

    private static void WriteBulkInstance(
        float[] buf, int index, float scale, Vector2 pos, Color tint,
        float uvX, float uvY, float uvW, float uvH)
    {
        int o = index * 16;
        // Transform2D row-major 2x4: (x.x, y.x, 0, origin.x, x.y, y.y, 0, origin.y)
        buf[o + 0] = scale; buf[o + 1] = 0f;    buf[o + 2] = 0f; buf[o + 3] = pos.X;
        buf[o + 4] = 0f;    buf[o + 5] = scale; buf[o + 6] = 0f; buf[o + 7] = pos.Y;
        buf[o + 8] = tint.R; buf[o + 9] = tint.G; buf[o + 10] = tint.B; buf[o + 11] = tint.A;
        buf[o + 12] = uvX; buf[o + 13] = uvY; buf[o + 14] = uvW; buf[o + 15] = uvH;
    }

    private int RenderCulledPool(float elapsedSec, bool orbitPaused, float speedScale)
    {
        int used = Math.Min(_visibleIndices.Count, _actorPool.Count);
        for (int i = 0; i < used; i++)
        {
            var e = _entities[_visibleIndices[i]];
            var sprite = _actorPool[i];
            sprite.Visible = true;
            sprite.Position = ResolvePreviewPos(e, elapsedSec, orbitPaused, speedScale);
            bool stepPose = e.IsOrbitPreview && !orbitPaused && ((((int)(elapsedSec * speedScale * 4f) + e.Id) & 1) == 1);
            bool dragonBreath = e.Species == MockSpeciesKind.TrueDragon && !orbitPaused && (((int)(elapsedSec * 2f)) % 4 == 0);
            ConfigureSpriteVisual(sprite, e, stepPose, dragonBreath);
        }

        for (int i = used; i < _actorPool.Count; i++)
        {
            _actorPool[i].Visible = false;
        }
        return used;
    }

    private int RenderNaiveNodes(float elapsedSec, bool orbitPaused, float speedScale)
    {
        if (_naiveNodes.Count != _entities.Count)
        {
            ClearNaiveNodes();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                var s = new Sprite2D
                {
                    Centered = true,
                    RegionEnabled = true,
                    TextureFilter = TextureFilterEnum.Nearest,
                    Position = e.BasePosition
                };
                ConfigureSpriteVisual(s, e);
                _naiveRoot.AddChild(s);
                _naiveNodes.Add(s);
            }
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            _naiveNodes[i].Position = ResolvePreviewPos(_entities[i], elapsedSec, orbitPaused, speedScale);
        }

        return _naiveNodes.Count;
    }

    private void ConfigureSpriteVisual(Sprite2D sprite, MockEntityVisual e, bool stepPose = false, bool dragonBreath = false)
    {
        if (e.Species is MockSpeciesKind.Human or MockSpeciesKind.Demon)
        {
            sprite.Texture = _humanoidAtlas;
            sprite.RegionRect = e.LifeState == MockLifeState.Dead
                ? new Rect2(96, 32, 32, 32)
                : (stepPose ? new Rect2(0, 32, 32, 32) : new Rect2(32, 0, 32, 32));
            sprite.Modulate = e.LifeState == MockLifeState.Dead
                ? new Color(0.6f, 0.6f, 0.65f)
                : (e.Species == MockSpeciesKind.Demon ? new Color(0.92f, 0.42f, 0.44f) : new Color(0.95f, 0.85f, 0.75f));
        }
        else
        {
            sprite.Texture = _creatureAtlas;
            sprite.Modulate = Colors.White;
            sprite.RegionRect = e.Species switch
            {
                MockSpeciesKind.Sheep => new Rect2(0, 0, 32, 32),
                MockSpeciesKind.Deer => new Rect2(32, 0, 32, 32),
                MockSpeciesKind.Wolf => new Rect2(64, 0, 32, 32),
                _ => new Rect2(dragonBreath ? 192 : 128, 0, 64, 64)
            };
        }
    }

    private MultiMeshInstance2D CreateBatchNode(string name, Texture2D atlas, float w, float h, int maxCount)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = CreateTopDownQuadMesh(w, h),
            InstanceCount = maxCount,
            VisibleInstanceCount = 0
        };

        return new MultiMeshInstance2D
        {
            Name = name,
            Texture = atlas,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = new ShaderMaterial { Shader = _multiMeshShader },
            Multimesh = mm
        };
    }

    public static ArrayMesh CreateTopDownQuadMesh(float width, float height)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;

        var vertices = new Vector3[]
        {
            new(-hw, -hh, 0f),
            new( hw, -hh, 0f),
            new( hw,  hh, 0f),
            new(-hw,  hh, 0f)
        };
        var uvs = new Vector2[]
        {
            new(0f, 0f),
            new(1f, 0f),
            new(1f, 1f),
            new(0f, 1f)
        };
        var indices = new[] { 0, 1, 2, 0, 2, 3 };

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private void HidePool()
    {
        foreach (var s in _actorPool)
        {
            s.Visible = false;
        }
    }

    private void HideMultiMesh()
    {
        _humanoidBatch.Visible = false;
        _creatureBatch.Visible = false;
    }

    private void ClearNaiveNodes()
    {
        foreach (var s in _naiveNodes)
        {
            s.QueueFree();
        }
        _naiveNodes.Clear();
    }
}
