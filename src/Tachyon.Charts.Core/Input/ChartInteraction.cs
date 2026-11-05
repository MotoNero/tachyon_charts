using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Core.Input;

/// <summary>Which directions of a chart an interaction changes.</summary>
[Flags]
public enum ChartDirection // NOSONAR: the name is public API
{
    /// <summary>The X axes: horizontal, or standing when the X axis runs down.</summary>
    X = 1,

    /// <summary>The Y axes: those of the pane, or the horizontal ones when the X axis runs down.</summary>
    Y = 2,

    /// <summary>Both directions.</summary>
    Both = X | Y,
}

/// <summary>A pane under the pointer.</summary>
/// <param name="Bounds">The area of the pane in device-independent units.</param>
/// <param name="Axes">The vertical axes of the pane.</param>
public readonly record struct PaneHit(DipRect Bounds, IReadOnlyList<AxisBase> Axes);

/// <summary>
/// Connects modifiers on the UI thread with a chart on the render thread: hit tests read an immutable
/// layout, changes of axes run where the axes live.
/// </summary>
/// <param name="chart">The chart.</param>
/// <param name="dispatch">
/// Runs a change on the thread that owns the chart, usually <see cref="RenderScheduler.Post"/>.
/// <see langword="null"/> runs it directly, for a chart without a scheduler.
/// </param>
public sealed class ChartInteraction(ChartSurface chart, Action<Action>? dispatch = null)
{
    /// <summary>Gets the chart.</summary>
    public ChartSurface Chart { get; } = chart ?? throw new ArgumentNullException(nameof(chart));

    /// <summary>Gets the undo steps of navigation. It belongs to the thread of the chart.</summary>
    public ZoomHistory History { get; } = new();

    /// <summary>Runs a change on the thread that owns the chart.</summary>
    /// <param name="change">The change.</param>
    public void Dispatch(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (dispatch is null)
        {
            change();
        }
        else
        {
            dispatch(change);
        }
    }

    /// <summary>Finds the pane at a position.</summary>
    /// <param name="position">The position relative to the chart.</param>
    /// <param name="hit">Receives the pane.</param>
    /// <returns>Whether there is a pane. Before the first frame there is none.</returns>
    public bool TryHitPane(Vector2 position, out PaneHit hit)
    {
        hit = default;
        if (Chart.CurrentLayout is not { } layout)
        {
            return false;
        }

        for (var i = 0; i < layout.Panes.Count; i++)
        {
            if (Contains(layout.Panes[i], position))
            {
                hit = new PaneHit(layout.Panes[i], NavigableAxes(Chart.Panes[i].Axes));
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the axis at a position.</summary>
    /// <param name="position">The position relative to the chart.</param>
    /// <param name="hit">Receives the axis.</param>
    /// <returns>Whether there is an axis.</returns>
    public bool TryHitAxis(Vector2 position, out AxisPlacement hit)
    {
        hit = default;
        if (Chart.CurrentLayout is not { } layout)
        {
            return false;
        }

        foreach (var placement in layout.Axes)
        {
            if (Contains(placement.Bounds, position))
            {
                hit = placement;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Zooms around a position, so that the data under it stays there.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="position">The position relative to the chart.</param>
    /// <param name="factor">The new visible length relative to the old one.</param>
    /// <param name="direction">The directions to change.</param>
    public void Zoom(PaneHit pane, Vector2 position, double factor, ChartDirection direction) => Dispatch(() =>
    {
        History.Record(Chart);
        direction = OnScreen(direction);
        if ((direction & ChartDirection.X) != 0)
        {
            foreach (var axis in Chart.HorizontalAxes)
            {
                axis.ZoomAround(ToData(axis, pane.Bounds, position.X), factor);
            }
        }

        if ((direction & ChartDirection.Y) != 0)
        {
            foreach (var axis in pane.Axes)
            {
                axis.ZoomAround(ToData(axis, pane.Bounds, position.Y), factor);
            }
        }
    });

    /// <summary>
    /// Moves the data by a distance, so that it follows the pointer.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="delta">The distance in device-independent units.</param>
    /// <param name="direction">The directions to change.</param>
    public void Pan(PaneHit pane, Vector2 delta, ChartDirection direction) => Dispatch(() =>
    {
        History.Record(Chart);
        direction = OnScreen(direction);
        if ((direction & ChartDirection.X) != 0)
        {
            foreach (var axis in Chart.HorizontalAxes)
            {
                PanAxis(axis, pane.Bounds, delta.X);
            }
        }

        if ((direction & ChartDirection.Y) != 0)
        {
            foreach (var axis in pane.Axes)
            {
                PanAxis(axis, pane.Bounds, delta.Y);
            }
        }
    });

    /// <summary>Moves one axis, so that its data follows the pointer.</summary>
    /// <param name="placement">The axis.</param>
    /// <param name="distance">The distance along the axis in device-independent units.</param>
    public void PanAxis(AxisPlacement placement, double distance) => Dispatch(() =>
    {
        History.Record(Chart);
        PanAxis(placement.Axis, placement.Bounds, distance);
    });

    /// <summary>Scales one axis around a position.</summary>
    /// <param name="placement">The axis.</param>
    /// <param name="position">The position along the axis in device-independent units.</param>
    /// <param name="factor">The new visible length relative to the old one.</param>
    public void ZoomAxis(AxisPlacement placement, double position, double factor) => Dispatch(() =>
    {
        History.Record(Chart);
        placement.Axis.ZoomAround(ToData(placement.Axis, placement.Bounds, position), factor);
    });

    /// <summary>Lets an axis follow the data again.</summary>
    /// <param name="axis">The axis.</param>
    public void AutoRange(AxisBase axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        Dispatch(() =>
        {
            History.Record(Chart);
            axis.IsAutoRange = true;
        });
    }

    /// <summary>Lets the horizontal axes and the axes of a pane follow the data again.</summary>
    /// <param name="pane">The pane.</param>
    public void AutoRange(PaneHit pane) => Dispatch(() =>
    {
        History.Record(Chart);
        foreach (var axis in Chart.HorizontalAxes)
        {
            axis.IsAutoRange = true;
        }

        foreach (var axis in pane.Axes)
        {
            axis.IsAutoRange = true;
        }
    });

    /// <summary>Shows only an area of a pane.</summary>
    /// <param name="pane">The pane.</param>
    /// <param name="area">The area in device-independent units.</param>
    /// <param name="direction">The directions to change.</param>
    public void ZoomTo(PaneHit pane, DipRect area, ChartDirection direction) => Dispatch(() =>
    {
        History.Record(Chart);
        direction = OnScreen(direction);
        if ((direction & ChartDirection.X) != 0)
        {
            foreach (var axis in Chart.HorizontalAxes)
            {
                ShowPixels(Chart, axis, pane.Bounds, area.X, area.Right);
            }
        }

        if ((direction & ChartDirection.Y) != 0)
        {
            foreach (var axis in pane.Axes)
            {
                ShowPixels(Chart, axis, pane.Bounds, area.Y, area.Bottom);
            }
        }
    });

    // X and Y name the axes of the data; with the X axis down, X runs up and down on the screen.
    private ChartDirection OnScreen(ChartDirection direction)
    {
        if (!Chart.IsXDown || direction == ChartDirection.Both)
        {
            return direction;
        }

        return direction == ChartDirection.X ? ChartDirection.Y : ChartDirection.X;
    }

    /// <summary>Converts a pointer position to a value of the first X axis, also when the X axis runs down.</summary>
    /// <param name="position">The position in chart coordinates.</param>
    /// <returns>The value of the last frame.</returns>
    public double ToXValue(Vector2 position)
    {
        if (!Chart.IsXDown)
        {
            return ToAxisValue(position.X);
        }

        return Chart.CurrentLayout is { } layout ? ToData(Chart.FirstXAxis, layout.PlotArea, position.Y) : 0;
    }

    /// <summary>Converts a position to a value of the first horizontal axis.</summary>
    /// <param name="x">The horizontal position in chart coordinates.</param>
    /// <returns>The value of the last frame.</returns>
    public double ToAxisValue(double x)
    {
        var axis = Chart.HorizontalAxes[0];
        return Chart.CurrentLayout is { } layout ? ToData(axis, layout.PlotArea, x) : 0;
    }

    /// <summary>Converts a distance on the screen to a distance on an axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="pixels">The distance in device-independent units.</param>
    /// <returns>The distance in units of the axis, with the sign of the direction of the axis.</returns>
    public double ToAxisDistance(AxisBase axis, double pixels)
    {
        ArgumentNullException.ThrowIfNull(axis);
        if (Chart.CurrentLayout is not { } layout)
        {
            return 0;
        }

        var start = axis.IsHorizontal ? layout.PlotArea.X : layout.PlotArea.Y;
        foreach (var placement in layout.Axes)
        {
            if (placement.Axis == axis)
            {
                return ToData(axis, placement.Bounds, start + pixels) - ToData(axis, placement.Bounds, start);
            }
        }

        return 0;
    }

    /// <summary>Switches the diagnostics overlay on or off.</summary>
    public void ToggleDiagnostics() => Dispatch(() => Chart.ShowDiagnostics = !Chart.ShowDiagnostics);

    /// <summary>Shows or hides the diagnostics overlay.</summary>
    /// <param name="isShown">Whether the overlay is shown.</param>
    public void ShowDiagnostics(bool isShown) => Dispatch(() => Chart.ShowDiagnostics = isShown);

    /// <summary>Sets the rectangle that previews a zoom.</summary>
    /// <param name="area">The area, or <see langword="null"/> for none.</param>
    public void ShowZoomRectangle(DipRect? area) => Dispatch(() => Chart.ZoomRectangle = area);

    private static bool Contains(DipRect bounds, Vector2 position) =>
        position.X >= bounds.X && position.X <= bounds.Right && position.Y >= bounds.Y && position.Y <= bounds.Bottom;

    // The value scale of a raster stays when the plot moves.
    private static IReadOnlyList<AxisBase> NavigableAxes(IReadOnlyList<AxisBase> axes) =>
        axes.Any(axis => axis is ColorAxis) ? [.. axes.Where(axis => axis is not ColorAxis)] : axes;

    private static double ToData(AxisBase axis, DipRect bounds, double pixel) => axis.IsHorizontal
        ? axis.CreateTransform(bounds.X, bounds.Width).ToData(pixel)
        : axis.CreateTransform(bounds.Y, bounds.Height).ToData(pixel);

    private static void PanAxis(AxisBase axis, DipRect bounds, double distance)
    {
        // Both ends by pixels: on a nonlinear axis, the same distance is another amount of values at each end.
        var (start, length) = axis.IsHorizontal ? (bounds.X, bounds.Width) : (bounds.Y, bounds.Height);
        axis.ShowRange(ToData(axis, bounds, start - distance), ToData(axis, bounds, start + length - distance));
    }

    private static void ShowPixels(ChartSurface chart, AxisBase axis, DipRect bounds, double first, double second)
    {
        var (from, to) = (ToData(axis, bounds, first), ToData(axis, bounds, second));
        if (chart.ZoomAnimationDuration > TimeSpan.Zero)
        {
            chart.Animator.AnimateRange(axis, new Numerics.DoubleRange(Math.Min(from, to), Math.Max(from, to)), chart.ZoomAnimationDuration);
        }
        else
        {
            axis.ShowRange(from, to);
        }
    }
}
