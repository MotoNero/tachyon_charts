using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;

namespace Tachyon.Charts.Core.Input;

/// <summary>
/// Zooms with the wheel around the cursor, pans by dragging, and follows pan and pinch gestures. A
/// double click lets the axes of the pane follow the data again.
/// </summary>
public sealed class ZoomPanModifier : ChartModifier
{
    /// <summary>The default dispatch priority.</summary>
    public const int DefaultPriority = 10;

    private PaneHit _pane;
    private Vector2 _last;
    private long _pointerId;
    private bool _isPanning;

    /// <summary>Initializes a new instance of the <see cref="ZoomPanModifier"/> class.</summary>
    public ZoomPanModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets or sets the directions of zooming.</summary>
    public ChartDirection ZoomDirection { get; set; } = ChartDirection.Both;

    /// <summary>Gets or sets the directions of panning.</summary>
    public ChartDirection PanDirection { get; set; } = ChartDirection.Both;

    /// <summary>Gets or sets the factor of one wheel step, greater than one.</summary>
    public double ZoomStep { get; set; } = 1.2;

    /// <summary>Gets or sets the button that pans.</summary>
    public PointerButton PanButton { get; set; } = PointerButton.Primary;

    /// <inheritdoc />
    protected internal override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (Interaction is not { } interaction || input.Delta.Y == 0 || !interaction.TryHitPane(input.Position, out var pane))
        {
            return ModifierResult.Ignored;
        }

        interaction.Zoom(pane, input.Position, Math.Pow(ZoomStep, -input.Delta.Y), ZoomDirection);
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (_isPanning)
        {
            return input.PointerId == _pointerId ? ContinuePan(input) : ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != PanButton
            || input.Modifiers != InputModifiers.None
            || Interaction is not { } interaction
            || !interaction.TryHitPane(input.Position, out _pane))
        {
            return ModifierResult.Ignored;
        }

        if (input.ClickCount >= 2)
        {
            interaction.AutoRange(_pane);
            return ModifierResult.Handled;
        }

        _isPanning = true;
        _pointerId = input.PointerId;
        _last = input.Position;
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnGesture(in GestureInputEvent input)
    {
        if (Interaction is not { } interaction || !interaction.TryHitPane(input.Position, out var pane))
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == GestureKind.Pan && input.Delta != Vector2.Zero)
        {
            interaction.Pan(pane, input.Delta, PanDirection);
        }
        else if (input.Kind == GestureKind.Pinch && input.Scale > 0 && input.Scale != 1)
        {
            interaction.Zoom(pane, input.Position, 1.0 / input.Scale, ZoomDirection);
        }

        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _isPanning = false;

    private ModifierResult ContinuePan(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                Interaction?.Pan(_pane, input.Position - _last, PanDirection);
                _last = input.Position;
                return ModifierResult.Handled;
            case PointerEventKind.Released:
            case PointerEventKind.Canceled:
                _isPanning = false;
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }
}

/// <summary>
/// Zooms into a dragged rectangle. Limits of the axes apply, and Escape cancels the drag.
/// </summary>
public sealed class RubberBandZoomModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above panning.</summary>
    public const int DefaultPriority = 20;

    private PaneHit _pane;
    private Vector2 _start;
    private long _pointerId;
    private bool _isDragging;
    private bool _isCanceled;

    /// <summary>Initializes a new instance of the <see cref="RubberBandZoomModifier"/> class.</summary>
    public RubberBandZoomModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets or sets the directions of the rectangle. The others span the whole pane.</summary>
    public ChartDirection Direction { get; set; } = ChartDirection.Both;

    /// <summary>Gets or sets the keys to hold when pressing the button.</summary>
    public InputModifiers ModifierKey { get; set; } = InputModifiers.Control;

    /// <summary>Gets or sets the button that drags.</summary>
    public PointerButton Button { get; set; } = PointerButton.Primary;

    /// <summary>Gets or sets the smallest extent that zooms, in device-independent units.</summary>
    public double MinimumExtent { get; set; } = 4;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (_isDragging)
        {
            return input.PointerId == _pointerId ? ContinueDrag(input) : ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != Button
            || input.Modifiers != ModifierKey
            || Interaction is not { } interaction
            || !interaction.TryHitPane(input.Position, out _pane))
        {
            return ModifierResult.Ignored;
        }

        _isDragging = true;
        _isCanceled = false;
        _pointerId = input.PointerId;
        _start = input.Position;
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (_isDragging && !_isCanceled && input.Key == ChartKey.Escape && input.Kind == KeyEventKind.Pressed)
        {
            _isCanceled = true;
            Interaction?.ShowZoomRectangle(null);
            return ModifierResult.Handled;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture)
    {
        _isDragging = false;
        Interaction?.ShowZoomRectangle(null);
    }

    private ModifierResult ContinueDrag(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                if (!_isCanceled)
                {
                    Interaction?.ShowZoomRectangle(GetArea(input.Position));
                }

                return ModifierResult.Handled;
            case PointerEventKind.Released:
                Finish(GetArea(input.Position));
                return ModifierResult.Release;
            case PointerEventKind.Canceled:
                _isCanceled = true;
                Finish(default);
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void Finish(DipRect area)
    {
        _isDragging = false;
        if (Interaction is not { } interaction)
        {
            return;
        }

        interaction.ShowZoomRectangle(null);
        var isWideEnough = (Direction & ChartDirection.X) == 0 || area.Width >= MinimumExtent;
        var isHighEnough = (Direction & ChartDirection.Y) == 0 || area.Height >= MinimumExtent;
        if (!_isCanceled && isWideEnough && isHighEnough)
        {
            interaction.ZoomTo(_pane, area, Direction);
        }
    }

    // The rectangle stays inside the pane where the drag began.
    private DipRect GetArea(Vector2 position)
    {
        var pane = _pane.Bounds;
        var x = Math.Clamp(position.X, pane.X, pane.Right);
        var y = Math.Clamp(position.Y, pane.Y, pane.Bottom);
        var left = (Direction & ChartDirection.X) != 0 ? Math.Min(_start.X, x) : pane.X;
        var right = (Direction & ChartDirection.X) != 0 ? Math.Max(_start.X, x) : pane.Right;
        var top = (Direction & ChartDirection.Y) != 0 ? Math.Min(_start.Y, y) : pane.Y;
        var bottom = (Direction & ChartDirection.Y) != 0 ? Math.Max(_start.Y, y) : pane.Bottom;
        return new DipRect(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// Acts on a single axis: dragging pans it, dragging with the scale key or the wheel scales it, and a
/// double click lets it follow the data again.
/// </summary>
public sealed class AxisDragModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above everything that acts on the plot.</summary>
    public const int DefaultPriority = 30;

    private const double ScaleSpeed = 0.01;

    private AxisPlacement _axis;
    private Vector2 _start;
    private Vector2 _last;
    private long _pointerId;
    private bool _isDragging;

    /// <summary>Initializes a new instance of the <see cref="AxisDragModifier"/> class.</summary>
    public AxisDragModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets or sets a value indicating whether only color bars react, not the other axes.</summary>
    public bool OnlyColorAxes { get; set; }

    /// <summary>Gets or sets the keys that scale instead of panning.</summary>
    public InputModifiers ScaleKey { get; set; } = InputModifiers.Shift;

    /// <summary>Gets or sets the factor of one wheel step, greater than one.</summary>
    public double ZoomStep { get; set; } = 1.2;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (_isDragging)
        {
            return input.PointerId == _pointerId ? ContinueDrag(input) : ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != PointerButton.Primary
            || Interaction is not { } interaction
            || !interaction.TryHitAxis(input.Position, out _axis)
            || !Applies(_axis))
        {
            return ModifierResult.Ignored;
        }

        if (input.ClickCount >= 2)
        {
            interaction.AutoRange(_axis.Axis);
            return ModifierResult.Handled;
        }

        _isDragging = true;
        _pointerId = input.PointerId;
        _start = input.Position;
        _last = input.Position;
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (Interaction is not { } interaction || input.Delta.Y == 0 || !interaction.TryHitAxis(input.Position, out var hit) || !Applies(hit))
        {
            return ModifierResult.Ignored;
        }

        var position = hit.Axis.IsHorizontal ? input.Position.X : input.Position.Y;
        interaction.ZoomAxis(hit, position, Math.Pow(ZoomStep, -input.Delta.Y));
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _isDragging = false;

    private bool Applies(in AxisPlacement placement) => !OnlyColorAxes || placement.Axis is ColorAxis;

    private ModifierResult ContinueDrag(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                Drag(input);
                return ModifierResult.Handled;
            case PointerEventKind.Released:
            case PointerEventKind.Canceled:
                _isDragging = false;
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void Drag(in PointerInputEvent input)
    {
        var delta = input.Position - _last;
        _last = input.Position;
        if (Interaction is not { } interaction)
        {
            return;
        }

        var isHorizontal = _axis.Axis.IsHorizontal;
        var distance = isHorizontal ? delta.X : delta.Y;
        if (ScaleKey != InputModifiers.None && (input.Modifiers & ScaleKey) == ScaleKey)
        {
            // Towards larger values zooms in, on both kinds of axis.
            var growth = isHorizontal ? distance : -distance;
            interaction.ZoomAxis(_axis, isHorizontal ? _start.X : _start.Y, Math.Exp(-growth * ScaleSpeed));
        }
        else
        {
            interaction.PanAxis(_axis, distance);
        }
    }
}

/// <summary>
/// Switches the diagnostics overlay with a key.
/// </summary>
public sealed class DiagnosticsModifier : ChartModifier
{
    /// <summary>Initializes a new instance of the <see cref="DiagnosticsModifier"/> class.</summary>
    public DiagnosticsModifier()
        : base(0)
    {
    }

    /// <summary>Gets or sets the key.</summary>
    public KeyBinding Key { get; set; } = new(ChartKey.F12);

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (!Key.Matches(input) || input.IsRepeat || Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        interaction.ToggleDiagnostics();
        return ModifierResult.Handled;
    }
}
