using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Input;

/// <summary>
/// Darkens what another chart does not show, so that its visible window stands out.
/// </summary>
/// <param name="targetAxis">Finds the axis of the other chart.</param>
public sealed class OverviewOverlay(Func<AxisBase?> targetAxis) : ChartOverlay
{
    private volatile WindowBox? _window;

    /// <summary>Gets the visible window of the last frame on the X axis of this chart.</summary>
    public (double Left, double Right)? Window => _window is { } window ? (window.Left, window.Right) : null;

    /// <inheritdoc />
    protected internal override long ExternalVersion => targetAxis()?.Version ?? 0;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var plot = canvas.Layout.PlotArea;
        if (targetAxis() is not { } target || canvas.Layout.Axes.Count == 0)
        {
            return;
        }

        var axis = canvas.Layout.Axes.First(placement => placement.Axis.IsHorizontal).Axis;
        var range = target.VisibleRange;
        if (!canvas.TryGetPosition(axis, range.Min, out var first) || !canvas.TryGetPosition(axis, range.Max, out var second))
        {
            return;
        }

        var left = Math.Clamp(Math.Min(first, second), plot.X, plot.Right);
        var right = Math.Clamp(Math.Max(first, second), plot.X, plot.Right);
        _window = new WindowBox(left, right);

        canvas.Clip = plot;
        var shade = canvas.BackgroundColor with { A = 170 };
        canvas.FillRectangle(new DipRect(plot.X, plot.Y, left - plot.X, plot.Height), shade);
        canvas.FillRectangle(new DipRect(right, plot.Y, plot.Right - right, plot.Height), shade);
        canvas.DrawRectangle(new DipRect(left, plot.Y, Math.Max(right - left, 2), plot.Height), canvas.AccentColor);
    }

    private sealed record WindowBox(double Left, double Right);
}

/// <summary>
/// Turns a small chart into the overview of another one: dragging the window moves the other chart,
/// dragging its edges scales it, and a click outside centers it there.
/// </summary>
public sealed class OverviewModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above everything else.</summary>
    public const int DefaultPriority = 40;

    private readonly Func<ChartInteraction?> _target;
    private readonly OverviewOverlay _overlay;
    private DragKind _drag;
    private double _lastValue;
    private long _pointerId;

    /// <summary>Initializes a new instance of the <see cref="OverviewModifier"/> class.</summary>
    /// <param name="target">Finds the chart this one gives an overview of. It may start later.</param>
    public OverviewModifier(Func<ChartInteraction?> target)
        : base(DefaultPriority)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _overlay = new OverviewOverlay(() => TargetAxis);
    }

    private enum DragKind
    {
        None,
        Move,
        LeftEdge,
        RightEdge,
    }

    /// <summary>Gets or sets how near an edge must be for dragging it, in device-independent units.</summary>
    public double EdgeDistance { get; set; } = 6;

    /// <summary>Gets the overlay.</summary>
    public OverviewOverlay Overlay => _overlay;

    private AxisBase? TargetAxis => _target()?.Chart.HorizontalAxes[0];

    /// <inheritdoc />
    protected internal override void OnAttached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.AddOverlay(_overlay));

    /// <inheritdoc />
    protected internal override void OnDetached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.RemoveOverlay(_overlay));

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (_drag != DragKind.None)
        {
            return input.PointerId == _pointerId ? ContinueDrag(input) : ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != PointerButton.Primary
            || Interaction is not { } interaction
            || _target() is not { } target
            || _overlay.Window is not { } window
            || !interaction.TryHitPane(input.Position, out _))
        {
            return ModifierResult.Ignored;
        }

        var x = input.Position.X;
        var value = interaction.ToAxisValue(x);
        if (Math.Abs(x - window.Left) <= EdgeDistance)
        {
            _drag = DragKind.LeftEdge;
        }
        else if (Math.Abs(x - window.Right) <= EdgeDistance)
        {
            _drag = DragKind.RightEdge;
        }
        else
        {
            _drag = DragKind.Move;
            if (x < window.Left || x > window.Right)
            {
                CenterAt(target, value);
            }
        }

        _lastValue = value;
        _pointerId = input.PointerId;
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _drag = DragKind.None;

    private static void CenterAt(ChartInteraction target, double value) => target.Dispatch(() =>
    {
        target.History.Record(target.Chart);
        var axis = target.Chart.HorizontalAxes[0];
        axis.PanBy(value - axis.VisibleRange.Center);
    });

    private ModifierResult ContinueDrag(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                if (Interaction is { } interaction && _target() is { } target)
                {
                    var value = interaction.ToAxisValue(input.Position.X);
                    Apply(target, value);
                    _lastValue = value;
                }

                return ModifierResult.Handled;
            case PointerEventKind.Released:
            case PointerEventKind.Canceled:
                _drag = DragKind.None;
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void Apply(ChartInteraction target, double value)
    {
        var drag = _drag;
        var delta = value - _lastValue;
        target.Dispatch(() =>
        {
            target.History.Record(target.Chart);
            var axis = target.Chart.HorizontalAxes[0];
            var range = axis.VisibleRange;
            switch (drag)
            {
                case DragKind.Move:
                    axis.PanBy(delta);
                    break;
                case DragKind.LeftEdge:
                    axis.ShowRange(Math.Min(value, range.Max), range.Max);
                    break;
                default:
                    axis.ShowRange(range.Min, Math.Max(value, range.Min));
                    break;
            }
        });
    }
}
