using FantasyWorldSimulator.Sandbox.PresentationTest.MockData;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

/// <summary>
/// Prototype 6-layer Modular Character assembler using a single 32x32 atlas
/// and palette_swap.gdshader for deterministic Human/Demon variants without unique sprites.
/// </summary>
public partial class ModularCharacterPreview : Node2D
{
    private const int CellSize = 32;

    private Sprite2D _shadow = null!;
    private Sprite2D _bodyBase = null!;
    private Sprite2D _raceFeatures = null!;
    private Sprite2D _hair = null!;
    private Sprite2D _outfit = null!;
    private Sprite2D _heldProp = null!;
    private Sprite2D _selectionRing = null!;

    public void Initialize(Texture2D humanoidAtlas, Shader paletteShader)
    {
        _shadow = CreateLayer("Shadow", humanoidAtlas, 0, 0, zIndex: -1);
        _selectionRing = CreateLayer("SelectionRing", humanoidAtlas, 7, 0, zIndex: -1);
        _selectionRing.Visible = false;

        _bodyBase = CreateLayer("BodyBase", humanoidAtlas, 1, 0, zIndex: 0);
        _bodyBase.Material = CreatePaletteMaterial(paletteShader);

        _raceFeatures = CreateLayer("RaceFeatures", humanoidAtlas, 2, 0, zIndex: 1);

        _hair = CreateLayer("Hair", humanoidAtlas, 3, 0, zIndex: 2);
        _hair.Material = CreatePaletteMaterial(paletteShader);

        _outfit = CreateLayer("Outfit", humanoidAtlas, 4, 0, zIndex: 3);
        _outfit.Material = CreatePaletteMaterial(paletteShader);

        _heldProp = CreateLayer("HeldProp", humanoidAtlas, 4, 1, zIndex: 4);
    }

    public void ApplyMockVisual(MockEntityVisual entity, bool isSelected, MockAnimPose pose = MockAnimPose.Idle)
    {
        _selectionRing.Visible = isSelected;

        if (entity.LifeState == MockLifeState.Dead || pose == MockAnimPose.Dead)
        {
            SetCell(_bodyBase, 3, 1);
            ((ShaderMaterial)_bodyBase.Material).SetShaderParameter("enable_palette_swap", false);
            _shadow.Visible = false;
            _raceFeatures.Visible = false;
            _hair.Visible = false;
            _outfit.Visible = false;
            _heldProp.Visible = false;
            return;
        }

        if (pose == MockAnimPose.Sleep)
        {
            SetCell(_bodyBase, 2, 1);
            ((ShaderMaterial)_bodyBase.Material).SetShaderParameter("enable_palette_swap", false);
            _shadow.Visible = false;
            _raceFeatures.Visible = false;
            _hair.Visible = false;
            _outfit.Visible = false;
            _heldProp.Visible = false;
            return;
        }

        _shadow.Visible = true;
        int bodyCol = pose switch
        {
            MockAnimPose.Walk => 0,
            MockAnimPose.WorkAttack => 1,
            _ => 1
        };
        int bodyRow = pose == MockAnimPose.Idle ? 0 : 1;
        SetCell(_bodyBase, bodyCol, bodyRow);
        var bodyMat = (ShaderMaterial)_bodyBase.Material;
        bodyMat.SetShaderParameter("enable_palette_swap", true);

        bool isDemon = entity.Species == MockSpeciesKind.Demon;
        if (isDemon)
        {
            // Demon crimson/obsidian skin tones via palette shader.
            SetRamp(bodyMat,
                new Color(0.84f, 0.35f, 0.38f),
                new Color(0.68f, 0.23f, 0.27f),
                new Color(0.46f, 0.14f, 0.18f));
            _raceFeatures.Visible = true;
            SetCell(_raceFeatures, 2, 0);
        }
        else
        {
            // Deterministic human skin variant from AppearanceSeed.
            int skinVariant = (int)(entity.AppearanceSeed % 3);
            Color light = skinVariant switch
            {
                0 => new Color(0.96f, 0.84f, 0.72f),
                1 => new Color(0.88f, 0.70f, 0.54f),
                _ => new Color(0.64f, 0.45f, 0.33f)
            };
            SetRamp(bodyMat, light, light.Darkened(0.16f), light.Darkened(0.35f));
            _raceFeatures.Visible = false;
        }

        // Hair layer
        _hair.Visible = entity.Profession != MockProfession.Guard;
        var hairMat = (ShaderMaterial)_hair.Material;
        Color hairCol = ((entity.AppearanceSeed >> 4) % 4) switch
        {
            0 => new Color(0.24f, 0.18f, 0.15f),
            1 => new Color(0.84f, 0.68f, 0.32f),
            2 => new Color(0.66f, 0.25f, 0.18f),
            _ => new Color(0.86f, 0.88f, 0.92f)
        };
        SetRamp(hairMat, hairCol, hairCol.Darkened(0.18f), hairCol.Darkened(0.36f));

        // Profession outfit + held item
        _outfit.Visible = true;
        var outfitMat = (ShaderMaterial)_outfit.Material;
        switch (entity.Profession)
        {
            case MockProfession.Guard:
                SetCell(_outfit, 5, 0);
                outfitMat.SetShaderParameter("enable_palette_swap", false);
                _heldProp.Visible = true;
                SetCell(_heldProp, 4, 1); // Sword
                break;
            case MockProfession.Farmer:
                SetCell(_outfit, 6, 0);
                outfitMat.SetShaderParameter("enable_palette_swap", false);
                _heldProp.Visible = true;
                SetCell(_heldProp, 5, 1); // Hoe
                break;
            case MockProfession.Mage:
                SetCell(_outfit, 4, 0);
                outfitMat.SetShaderParameter("enable_palette_swap", true);
                SetRamp(outfitMat,
                    new Color(0.42f, 0.32f, 0.78f),
                    new Color(0.30f, 0.22f, 0.60f),
                    new Color(0.20f, 0.14f, 0.42f));
                _heldProp.Visible = true;
                SetCell(_heldProp, 6, 1); // Staff
                break;
            default:
                SetCell(_outfit, 4, 0);
                outfitMat.SetShaderParameter("enable_palette_swap", true);
                Color tunic = isDemon ? new Color(0.36f, 0.36f, 0.44f) : new Color(0.32f, 0.56f, 0.78f);
                SetRamp(outfitMat, tunic, tunic.Darkened(0.18f), tunic.Darkened(0.36f));
                _heldProp.Visible = false;
                break;
        }
    }

    private Sprite2D CreateLayer(string name, Texture2D atlas, int col, int row, int zIndex)
    {
        var s = new Sprite2D
        {
            Name = name,
            Texture = atlas,
            RegionEnabled = true,
            RegionRect = new Rect2(col * CellSize, row * CellSize, CellSize, CellSize),
            Centered = false,
            Offset = new Vector2(-16f, -28f),
            TextureFilter = TextureFilterEnum.Nearest,
            ZIndex = zIndex
        };
        AddChild(s);
        return s;
    }

    private static void SetCell(Sprite2D sprite, int col, int row)
    {
        sprite.RegionRect = new Rect2(col * CellSize, row * CellSize, CellSize, CellSize);
    }

    private static ShaderMaterial CreatePaletteMaterial(Shader shader)
    {
        return new ShaderMaterial { Shader = shader };
    }

    private static void SetRamp(ShaderMaterial mat, Color light, Color mid, Color dark)
    {
        mat.SetShaderParameter("tone_light", light);
        mat.SetShaderParameter("tone_mid", mid);
        mat.SetShaderParameter("tone_dark", dark);
    }
}
