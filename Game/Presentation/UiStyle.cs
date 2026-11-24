using Godot;

namespace Game.Presentation;

// Shared look for on-screen panels: a dark translucent box with padding, so text never sits directly on the map.
public static class UiStyle
{
    public static void Panel(Label label, int fontSize = 15)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.11f, 0.78f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        label.AddThemeStyleboxOverride("normal", box);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.93f, 0.93f, 0.88f));
    }
}
