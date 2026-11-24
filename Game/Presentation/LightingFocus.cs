using Godot;

namespace Game.Presentation;

// T018-mood: optional "diorama" tilt-shift (Shaders/tilt_shift.gdshader) blurring the top and bottom of the world view.
// F4 toggles it; off by default until the owner decides. Sits on a CanvasLayer below the UI so panels stay sharp.
public partial class LightingFocus : ColorRect
{
    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Keycode: Key.F4, Pressed: true, Echo: false }) return;
        Visible = !Visible;
        GetViewport().SetInputAsHandled();
    }
}
