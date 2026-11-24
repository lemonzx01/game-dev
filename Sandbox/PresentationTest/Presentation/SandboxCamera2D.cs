using System;
using Godot;

namespace FantasyWorldSimulator.Sandbox.PresentationTest.Presentation;

/// <summary>
/// Isolated Observer/Test camera with WASD/Drag pan, cursor-centered zoom,
/// follow target tracking, and viewport frustum query.
/// Completely decoupled from any Simulation Engine state.
/// </summary>
public partial class SandboxCamera2D : Camera2D
{
    [Export] public float PanSpeedScreenPx { get; set; } = 520f;
    [Export] public float MinZoom { get; set; } = 0.25f;
    [Export] public float MaxZoom { get; set; } = 4.0f;
    [Export] public float ZoomFactor { get; set; } = 1.2f;

    public int? FollowedEntityId { get; private set; }
    private bool _isMiddleDragging;
    private Vector2 _lastMouseScreenPos;

    public override void _Ready()
    {
        AnchorMode = AnchorModeEnum.DragCenter;
        Enabled = true;
        Zoom = new Vector2(2.0f, 2.0f);
        Position = new Vector2(33f * 16f, 26f * 16f);
    }

    public void SetZoomScalar(float zoomValue)
    {
        float clamped = Mathf.Clamp(zoomValue, MinZoom, MaxZoom);
        Zoom = new Vector2(clamped, clamped);
    }

    /// <summary>
    /// Zooms while preserving the world point under cursorScreenPos.
    /// </summary>
    public void ZoomAtScreenPoint(float targetZoom, Vector2 cursorScreenPos, Vector2 viewportSize)
    {
        float oldZoom = MathF.Max(0.0001f, Zoom.X);
        float newZoom = Mathf.Clamp(targetZoom, MinZoom, MaxZoom);
        if (MathF.Abs(newZoom - oldZoom) < 0.00001f)
        {
            return;
        }

        Vector2 screenOffset = cursorScreenPos - (viewportSize * 0.5f);
        Vector2 worldUnderCursor = Position + (screenOffset / oldZoom);
        Zoom = new Vector2(newZoom, newZoom);
        Position = worldUnderCursor - (screenOffset / newZoom);
    }

    public Vector2 ScreenToWorld(Vector2 screenPos, Vector2 viewportSize)
    {
        float z = MathF.Max(0.0001f, Zoom.X);
        return Position + ((screenPos - (viewportSize * 0.5f)) / z);
    }

    public Rect2 GetVisibleWorldRect(Vector2 viewportSize, float marginPx = 64f)
    {
        float z = MathF.Max(0.0001f, Zoom.X);
        Vector2 halfSize = (viewportSize * 0.5f / z) + new Vector2(marginPx, marginPx);
        return new Rect2(Position - halfSize, halfSize * 2f);
    }

    public void StartFollow(int entityId)
    {
        FollowedEntityId = entityId;
    }

    public void StopFollow()
    {
        FollowedEntityId = null;
    }

    public void UpdateFollow(Vector2 targetWorldPos, float delta)
    {
        if (!FollowedEntityId.HasValue)
        {
            return;
        }

        float t = 1f - MathF.Exp(-12f * delta);
        Position = Position.Lerp(targetWorldPos, t);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        Vector2 viewportSize = GetViewportRect().Size;

        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _isMiddleDragging = mb.Pressed;
                _lastMouseScreenPos = mb.Position;
                if (mb.Pressed)
                {
                    StopFollow();
                }
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomAtScreenPoint(Zoom.X * ZoomFactor, mb.Position, viewportSize);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAtScreenPoint(Zoom.X / ZoomFactor, mb.Position, viewportSize);
            }
        }
        else if (@event is InputEventMouseMotion mm && _isMiddleDragging)
        {
            Vector2 deltaScreen = mm.Position - _lastMouseScreenPos;
            _lastMouseScreenPos = mm.Position;
            Position -= deltaScreen / MathF.Max(0.0001f, Zoom.X);
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Equal || key.Keycode == Key.KpAdd)
            {
                ZoomAtScreenPoint(Zoom.X * ZoomFactor, viewportSize * 0.5f, viewportSize);
            }
            else if (key.Keycode == Key.Minus || key.Keycode == Key.KpSubtract)
            {
                ZoomAtScreenPoint(Zoom.X / ZoomFactor, viewportSize * 0.5f, viewportSize);
            }
        }
    }

    public override void _Process(double delta)
    {
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1f;

        if (dir != Vector2.Zero)
        {
            StopFollow();
            float speedWorld = PanSpeedScreenPx / MathF.Max(0.0001f, Zoom.X);
            Position += dir.Normalized() * speedWorld * (float)delta;
        }
    }
}
