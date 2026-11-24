using Godot;

namespace Game.Presentation;

// Pure input/view control: pan and zoom only. Must never read ISimulation
// or influence gameplay outcomes (ARCHITECTURE.md §7 camera invariant).
public partial class CameraController : Camera2D
{
    [Export] public float PanSpeed = 400f;
    [Export] public float ZoomStep = 0.1f;
    [Export] public float MinZoom = 0.25f;
    [Export] public float MaxZoom = 4f;

    public override void _Process(double delta)
    {
        var direction = Vector2.Zero;
        if (Input.IsActionPressed("ui_right")) direction.X += 1f;
        if (Input.IsActionPressed("ui_left")) direction.X -= 1f;
        if (Input.IsActionPressed("ui_down")) direction.Y += 1f;
        if (Input.IsActionPressed("ui_up")) direction.Y -= 1f;
        if (direction != Vector2.Zero)
            Position += direction.Normalized() * PanSpeed * (float)delta / Zoom.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse)
            return;
        float factor = mouse.ButtonIndex switch
        {
            MouseButton.WheelUp => 1f + ZoomStep,
            MouseButton.WheelDown => 1f - ZoomStep,
            _ => 1f,
        };
        if (factor == 1f)
            return;
        float next = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(next, next);
    }
}
