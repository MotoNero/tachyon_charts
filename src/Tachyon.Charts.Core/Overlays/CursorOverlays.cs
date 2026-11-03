using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>
/// Two lines through a position, with the values on the axes.
/// </summary>
public sealed class CrosshairOverlay : ChartOverlay
{
    private const float MarkerSize = 4;

    private Vector2? _position;
    private double? _dataX;
    private AxisBase? _xAxis;
    private HitResult? _snapped;

    /// <summary>Gets the position, or <see langword="null"/> while hidden.</summary>
    public Vector2? Position => _position;

    /// <summary>Shows the crosshair at a position.</summary>
    /// <param name="position">The position in chart coordinates.</param>
    /// <param name="snapped">The point the crosshair sits on, if any.</param>
    public void Show(Vector2 position, HitResult? snapped = null)
    {
        _position = position;
        _snapped = snapped;
        _dataX = null;
        OnChanged();
    }

    /// <summary>Shows only the vertical line at a value of an axis, for example from another chart.</summary>
    /// <param name="axis">The horizontal axis.</param>
    /// <param name="value">The value.</param>
    public void ShowAt(AxisBase axis, double value)
    {
        _xAxis = axis;
        _dataX = value;
        _position = null;
        _snapped = null;
        OnChanged();
    }

    /// <summary>Hides the crosshair.</summary>
    public void Hide()
    {
        if (_position is not null || _dataX is not null)
        {
            _position = null;
            _dataX = null;
            _snapped = null;
            OnChanged();
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        if (_dataX is { } value && _xAxis is { } axis)
        {
            DrawSyncedLine(canvas, axis, value);
        }
        else if (_position is { } position)
        {
            DrawCross(canvas, position);
        }
    }

    private static Vector2 Center(OverlayCanvas canvas, HitResult hit, Vector2 fallback)
    {
        var center = fallback;
        if (canvas.TryGetPosition(hit.Series.XAxis, hit.X, out var x) && canvas.TryGetPosition(hit.Series.YAxis, hit.Y, out var y))
        {
            center = new Vector2((float)x, (float)y);
        }

        return center;
    }

    private static void DrawAxisLabels(OverlayCanvas canvas, Vector2 center)
    {
        foreach (var placement in canvas.Layout.Axes)
        {
            var axis = placement.Axis;
            var bounds = placement.Bounds;
            double along = axis.IsHorizontal ? center.X : center.Y;
            var isInside = axis.IsHorizontal
                ? along >= bounds.X && along <= bounds.Right
                : along >= bounds.Y && along <= bounds.Bottom;
            if (!isInside || !canvas.TryGetValue(axis, along, out var value))
            {
                continue;
            }

            var text = axis.FormatCursorValue(value);
            var width = canvas.MeasureWidth(text, canvas.LabelSize) + 8;
            var height = canvas.GetLineHeight(canvas.LabelSize) + 2;
            var (x, y) = axis.Position switch
            {
                AxisPosition.Bottom => (along - (width / 2), bounds.Y + 2),
                AxisPosition.Top => (along - (width / 2), bounds.Bottom - height - 2),
                AxisPosition.Left => (bounds.Right - width - 2, along - (height / 2)),
                _ => (bounds.X + 2, along - (height / 2)),
            };

            canvas.DrawLabel(text, x, y, canvas.AccentColor, canvas.BackgroundColor);
        }
    }

    private static void DrawSyncedLine(OverlayCanvas canvas, AxisBase axis, double value)
    {
        if (!canvas.TryGetPosition(axis, value, out var x))
        {
            return;
        }

        foreach (var pane in canvas.Layout.Panes)
        {
            canvas.Clip = pane;
            canvas.DrawLine(new Vector2((float)x, (float)pane.Y), new Vector2((float)x, (float)pane.Bottom), 1, canvas.AccentColor);
        }

        canvas.Clip = null;
    }

    private void DrawCross(OverlayCanvas canvas, Vector2 position)
    {
        var center = _snapped is { } hit ? Center(canvas, hit, position) : position;
        if (!canvas.TryGetPane(center, out var pane))
        {
            return;
        }

        canvas.Clip = pane;
        canvas.DrawLine(new Vector2(center.X, (float)pane.Y), new Vector2(center.X, (float)pane.Bottom), 1, canvas.AccentColor);
        canvas.DrawLine(new Vector2((float)pane.X, center.Y), new Vector2((float)pane.Right, center.Y), 1, canvas.AccentColor);
        canvas.Clip = null;

        if (_snapped is not null)
        {
            canvas.FillRectangle(new DipRect(center.X - MarkerSize, center.Y - MarkerSize, MarkerSize * 2, MarkerSize * 2), canvas.AccentColor);
        }

        DrawAxisLabels(canvas, center);
    }
}

/// <summary>
/// A vertical line with the values of all series at that place.
/// </summary>
public sealed class RolloverOverlay : ChartOverlay
{
    private const float MarkerSize = 3;

    private readonly List<HitResult> _hits = [];
    private double? _x;
    private AxisBase? _axis;

    /// <summary>Gets the points shown.</summary>
    public IReadOnlyList<HitResult> Hits => _hits;

    /// <summary>Shows the values of some points.</summary>
    /// <param name="axis">The horizontal axis of the line.</param>
    /// <param name="x">The value on that axis.</param>
    /// <param name="hits">The points.</param>
    public void Show(AxisBase axis, double x, IEnumerable<HitResult> hits)
    {
        _axis = axis;
        _x = x;
        _hits.Clear();
        _hits.AddRange(hits);
        OnChanged();
    }

    /// <summary>Hides the line.</summary>
    public void Hide()
    {
        if (_x is not null)
        {
            _x = null;
            _hits.Clear();
            OnChanged();
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        if (_x is not { } value || _axis is not { } axis || !canvas.TryGetPosition(axis, value, out var x))
        {
            return;
        }

        foreach (var pane in canvas.Layout.Panes)
        {
            canvas.Clip = pane;
            canvas.DrawLine(new Vector2((float)x, (float)pane.Y), new Vector2((float)x, (float)pane.Bottom), 1, canvas.AccentColor);
        }

        canvas.Clip = null;
        var labelHeight = canvas.GetLineHeight(canvas.LabelSize) + 4;
        var lastBottom = double.NegativeInfinity;
        foreach (var hit in _hits.OrderBy(hit => canvas.TryGetPosition(hit.Series.YAxis, hit.Y, out var position) ? position : 0))
        {
            if (!canvas.TryGetPosition(hit.Series.YAxis, hit.Y, out var y) || !canvas.TryGetPosition(hit.Series.XAxis, hit.X, out var pointX))
            {
                continue;
            }

            canvas.FillRectangle(new DipRect(pointX - MarkerSize, y - MarkerSize, MarkerSize * 2, MarkerSize * 2), hit.Series.AccentColor);

            // Labels of near values stack instead of covering each other.
            var top = Math.Max(y - (labelHeight / 2), lastBottom);
            var text = hit.Series.Name is { Length: > 0 } name ? $"{name}: {hit.FormatY()}" : hit.FormatY();
            canvas.DrawLabel(text, pointX + 8, top, hit.Series.AccentColor, canvas.BackgroundColor);
            lastBottom = top + labelHeight;
        }
    }
}
