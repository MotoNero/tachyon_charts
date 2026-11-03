using System.Numerics;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Operates the camera: dragging turns the view, with Shift or the middle button it moves the view, the wheel
/// zooms, a double click and Home return to the start. The arrow keys turn the view as well. Without a button the
/// pointer shows values, a click without moving sets a marker or removes the one it meets, and Delete removes the newest one. Dragging the outline
/// of a plane moves the plane.
/// </summary>
/// <param name="scene">The scene.</param>
/// <param name="pointer">What lies under the pointer.</param>
internal sealed class Orbit3DModifier(Scene3D scene, PointerPick3D pointer) : ChartModifier(ZoomPanModifier.DefaultPriority)
{
    private const float ClickReach = 4;
    private const double DegreesPerUnit = 0.4;
    private const double PanPerUnit = 0.0015;
    private const double ZoomPerStep = 1.15;
    private const double DegreesPerKey = 5;

    private bool _isDragging;
    private bool _isPanning;
    private bool _isClick;
    private ClipPlane3D? _plane;
    private (TransferFunction3D Function, int Index)? _handle;
    private Volume3D? _level;
    private Vector2 _last;
    private Vector2 _pressed;

    /// <inheritdoc />
    protected override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton is PointerButton.Primary or PointerButton.Middle)
        {
            return Press(input);
        }

        if (!_isDragging)
        {
            Hover(input);
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            Drag(input);
            return ModifierResult.Handled;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            _isDragging = false;
            if (_isClick)
            {
                SetMarker(input.Position);
            }

            return ModifierResult.Release;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (input.Delta.Y == 0)
        {
            return ModifierResult.Ignored;
        }

        scene.Camera.Zoom(input.Delta.Y > 0 ? 1 / ZoomPerStep : ZoomPerStep);
        Redraw();
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (input.Kind != KeyEventKind.Pressed)
        {
            return ModifierResult.Ignored;
        }

        switch (input.Key)
        {
            case ChartKey.Left:
                scene.Camera.Orbit(DegreesPerKey, 0);
                break;
            case ChartKey.Right:
                scene.Camera.Orbit(-DegreesPerKey, 0);
                break;
            case ChartKey.Up:
                scene.Camera.Orbit(0, DegreesPerKey);
                break;
            case ChartKey.Down:
                scene.Camera.Orbit(0, -DegreesPerKey);
                break;
            case ChartKey.Add:
                scene.Camera.Zoom(1 / ZoomPerStep);
                break;
            case ChartKey.Subtract:
                scene.Camera.Zoom(ZoomPerStep);
                break;
            case ChartKey.Home:
                scene.Camera.Show(CameraView.Default);
                break;
            case ChartKey.Delete:
                MarkerLinks.RemoveNewest(scene);
                break;
            default:
                return ModifierResult.Ignored;
        }

        Redraw();
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected override void OnCaptureLost(InputCapture capture) => _isDragging = false;

    private ModifierResult Press(in PointerInputEvent input)
    {
        if (input.ClickCount >= 2)
        {
            scene.Camera.Show(CameraView.Default);
            Redraw();
            return ModifierResult.Handled;
        }

        _isDragging = true;
        _handle = input.ChangedButton == PointerButton.Primary ? FindHandle(input.Position) : null;
        _level = input.ChangedButton == PointerButton.Primary && _handle is null ? FindLevel(input.Position) : null;
        _plane = input.ChangedButton == PointerButton.Primary && _handle is null && _level is null ? FindPlane(input.Position) : null;
        _isPanning = input.ChangedButton == PointerButton.Middle || (input.Modifiers & InputModifiers.Shift) != 0;
        _last = input.Position;
        _pressed = input.Position;
        _isClick = input.ChangedButton == PointerButton.Primary && !_isPanning && _plane is null && _handle is null && _level is null;
        pointer.Hide();
        return ModifierResult.Capture;
    }

    private ClipPlane3D? FindPlane(Vector2 position) =>
        Interaction?.Chart.CurrentLayout is { Panes.Count: > 0 } layout ? PlaneGrip.Find(scene, layout.Panes[0], position) : null;

    private (TransferFunction3D Function, int Index)? FindHandle(Vector2 position) =>
        Interaction?.Chart.CurrentLayout is { Panes.Count: > 0 } layout ? TransferGrip.Find(scene, layout.Panes[0], position) : null;

    private Volume3D? FindLevel(Vector2 position) =>
        Interaction?.Chart.CurrentLayout is { Panes.Count: > 0 } layout ? TransferGrip.FindLevel(scene, layout.Panes[0], position) : null;

    private void Drag(in PointerInputEvent input)
    {
        if (_level is { } level && Interaction?.Chart.CurrentLayout is { Panes.Count: > 0 } levelPanes)
        {
            TransferGrip.MoveLevel(level, levelPanes.Panes[0], input.Position);
            Redraw();
            return;
        }

        if (_handle is { } handle && Interaction?.Chart.CurrentLayout is { Panes.Count: > 0 } panes)
        {
            TransferGrip.Move(handle.Function, handle.Index, panes.Panes[0], input.Position);
            Redraw();
            return;
        }

        if (_plane is not null && Interaction?.Chart.CurrentLayout is { Panes.Count: > 0 } layout)
        {
            PlaneGrip.Move(scene, _plane, layout.Panes[0], input.Position - _last);
            _last = input.Position;
            Redraw();
            return;
        }

        _isClick &= Vector2.Distance(input.Position, _pressed) <= ClickReach;
        if (_isClick)
        {
            return;
        }

        var delta = input.Position - _last;
        _last = input.Position;
        if (_isPanning)
        {
            scene.Camera.Pan(-delta.X * PanPerUnit, delta.Y * PanPerUnit);
        }
        else
        {
            scene.Camera.Orbit(-delta.X * DegreesPerUnit, delta.Y * DegreesPerUnit);
        }

        Redraw();
    }

    private void Hover(in PointerInputEvent input)
    {
        if (Interaction is not { } interaction)
        {
            return;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            pointer.Move(interaction.Chart, input.Position);
            Redraw();
        }
        else if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled)
        {
            pointer.Hide();
            Redraw();
        }
    }

    private void SetMarker(Vector2 position)
    {
        if (Interaction?.Chart is { } chart)
        {
            pointer.Move(chart, position);
            var nearest = chart.CurrentLayout is { Panes.Count: > 0 } layout ? MarkerLinks.FindNear(scene, layout.Panes[0], position) : null;
            if (pointer.Current is { } pick)
            {
                MarkerLinks.Place(scene, pick, nearest);
                Redraw();
            }
            else if (nearest is { } index)
            {
                scene.RemoveMarker(index);
                Redraw();
            }
        }
    }

    private void Redraw() => Interaction?.Chart.Invalidate();
}
