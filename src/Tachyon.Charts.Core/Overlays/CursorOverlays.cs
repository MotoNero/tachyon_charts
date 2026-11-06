using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Instruments;
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
    private const float Swatch = 12;
    private const float BoxPadding = 6;
    private const float BoxOffset = 14;

    private Vector2? _position;
    private double? _dataX;
    private AxisBase? _xAxis;
    private HitResult? _snapped;
    private readonly List<(AxisBase Axis, double Value, long Stamp, string Text)> _axisTexts = [];
    private HitResult[] _values = [];
    private string[] _texts = [];
    private string? _header;

    /// <summary>Gets the position, or <see langword="null"/> while hidden.</summary>
    public Vector2? Position => _position;

    /// <summary>Shows the crosshair at a position.</summary>
    /// <param name="position">The position in chart coordinates.</param>
    /// <param name="snapped">The point the crosshair sits on, if any.</param>
    public void Show(Vector2 position, HitResult? snapped = null) => Show(position, snapped, [], null);

    /// <summary>Shows the crosshair with a point on each curve and a box with the values of all series there.</summary>
    /// <param name="position">The position in chart coordinates.</param>
    /// <param name="snapped">The point the crosshair sits on, if any.</param>
    /// <param name="values">The point of each series at the X of the position.</param>
    /// <param name="header">The X value as text, the first line of the box, or <see langword="null"/> for none.</param>
    public void Show(Vector2 position, HitResult? snapped, IReadOnlyList<HitResult> values, string? header)
    {
        ArgumentNullException.ThrowIfNull(values);

        // The texts come from the pointer, so that drawing a frame formats nothing.
        var texts = new string[values.Count];
        for (var i = 0; i < texts.Length; i++)
        {
            var hit = values[i];
            texts[i] = hit.Series.Name is { Length: > 0 } name ? name + ": " + hit.FormatY() : hit.FormatY();
        }

        (_values, _texts, _header) = ([.. values], texts, header);
        _position = position;
        _snapped = snapped;
        _dataX = null;
        OnChanged();
    }

    /// <summary>Gets the points of the series shown in the box.</summary>
    public IReadOnlyList<HitResult> Values => _values;

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
            (_values, _texts, _header) = ([], [], null);
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
        if (canvas.TryGetPoint(hit.Series.XAxis, hit.Series.YAxis, hit.X, hit.Y, out var point))
        {
            center = point;
        }

        return center;
    }

    // The text of each axis is kept while its value stays, so that a resting cross formats nothing per frame.
    private string TextOf(AxisBase axis, double value)
    {
        for (var i = 0; i < _axisTexts.Count; i++)
        {
            if (_axisTexts[i].Axis == axis)
            {
                if (_axisTexts[i].Value.Equals(value) && _axisTexts[i].Stamp == Stamp(axis))
                {
                    return _axisTexts[i].Text;
                }

                var text = axis.FormatCursorValue(value);
                _axisTexts[i] = (axis, value, Stamp(axis), text);
                return text;
            }
        }

        var first = axis.FormatCursorValue(value);
        _axisTexts.Add((axis, value, Stamp(axis), first));
        return first;
    }

    // The format of a value can follow the zoom and the language.
    private static long Stamp(AxisBase axis) => axis.Version + ChartText.Version;

    private void DrawAxisLabels(OverlayCanvas canvas, Vector2 center)
    {
        // Index loops: an enumerator of a list interface would allocate every frame.
        var placements = canvas.Layout.Axes;
        for (var p = 0; p < placements.Count; p++)
        {
            var placement = placements[p];
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

            var text = TextOf(axis, value);
            var width = canvas.MeasureWidth(text, canvas.LabelSize) + 8;
            var height = canvas.GetLineHeight(canvas.LabelSize) + 2;
            var (x, y) = Place(axis, bounds, along, width, height, canvas.GetLineHeight(canvas.LabelSize) + 4);
            canvas.DrawLabel(text, x, y, canvas.AccentColor, canvas.BackgroundColor);
        }
    }

    /// <summary>
    /// Places the value of an axis next to the axis line, in the room of the scale values. Where the label would reach
    /// the title of the axis, it moves into the plot beside the line instead, so that the title stays readable.
    /// </summary>
    internal static (double X, double Y) Place(AxisBase axis, DipRect bounds, double along, double width, double height, double title)
    {
        var room = axis.Title is null || axis.IsHidden ? 0 : title;
        return axis.Position switch
        {
            AxisPosition.Bottom => (along - (width / 2), bounds.Y + 2 + height <= bounds.Bottom - room ? bounds.Y + 2 : bounds.Y - height - 2),
            AxisPosition.Top => (along - (width / 2), bounds.Bottom - height - 2 >= bounds.Y + room ? bounds.Bottom - height - 2 : bounds.Bottom + 2),
            AxisPosition.Left => (bounds.Right - width - 2 >= bounds.X + room ? bounds.Right - width - 2 : bounds.Right + 2, along - (height / 2)),
            _ => (bounds.X + 2 + width <= bounds.Right - room ? bounds.X + 2 : bounds.X - width - 2, along - (height / 2)),
        };
    }

    private static void DrawSyncedLine(OverlayCanvas canvas, AxisBase axis, double value)
    {
        if (!canvas.TryGetPosition(axis, value, out var at))
        {
            return;
        }

        foreach (var pane in canvas.Layout.Panes)
        {
            canvas.Clip = pane;
            DrawAcross(canvas, axis, at, pane);
        }

        canvas.Clip = null;
    }

    /// <summary>Draws the line through a pane at a place of an axis: upright for a horizontal axis, level for a standing one.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="axis">The axis.</param>
    /// <param name="at">The place on the axis.</param>
    /// <param name="pane">The pane.</param>
    internal static void DrawAcross(OverlayCanvas canvas, AxisBase axis, double at, DipRect pane)
    {
        var line = axis.IsHorizontal
            ? (new Vector2((float)at, (float)pane.Y), new Vector2((float)at, (float)pane.Bottom))
            : (new Vector2((float)pane.X, (float)at), new Vector2((float)pane.Right, (float)at));
        canvas.DrawLine(line.Item1, line.Item2, 1, canvas.AccentColor);
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
        if (_values.Length > 0)
        {
            DrawValues(canvas, center, pane);
        }
    }

    // A point on each curve, then a box beside the cross that stays inside the pane.
    private void DrawValues(OverlayCanvas canvas, Vector2 center, DipRect pane)
    {
        canvas.Clip = pane;
        foreach (var hit in _values)
        {
            if (canvas.TryGetPoint(hit.Series.XAxis, hit.Series.YAxis, hit.X, hit.Y, out var point))
            {
                var dot = new DipRect(point.X - MarkerSize, point.Y - MarkerSize, MarkerSize * 2, MarkerSize * 2);
                canvas.FillRectangle(dot, hit.Series.AccentColor);
                canvas.DrawRectangle(dot, canvas.BackgroundColor);
            }
        }

        canvas.Clip = null;
        var line = canvas.GetLineHeight(canvas.LabelSize) + 2;
        var width = _header is null ? 0 : canvas.MeasureWidth(_header, canvas.LabelSize);
        foreach (var text in _texts)
        {
            width = Math.Max(width, canvas.MeasureWidth(text, canvas.LabelSize) + Swatch + BoxPadding);
        }

        var rows = _texts.Length + (_header is null ? 0 : 1);
        var size = (Width: width + (BoxPadding * 2), Height: (rows * line) + (BoxPadding * 2));
        var x = center.X + BoxOffset + size.Width > pane.Right ? center.X - BoxOffset - size.Width : center.X + BoxOffset;
        var y = center.Y + BoxOffset + size.Height > pane.Bottom ? center.Y - BoxOffset - size.Height : center.Y + BoxOffset;
        var box = new DipRect(Math.Max(x, pane.X), Math.Max(y, pane.Y), size.Width, size.Height);
        canvas.FillRectangle(box, canvas.BackgroundColor with { A = 235 });
        canvas.DrawRectangle(box, canvas.AccentColor with { A = 140 });
        var baseline = box.Y + BoxPadding + (line * 0.78);
        if (_header is { } header)
        {
            canvas.DrawText(header, box.X + BoxPadding, baseline, canvas.LabelSize, canvas.TextColor);
            baseline += line;
        }

        for (var i = 0; i < _texts.Length; i++)
        {
            canvas.FillRectangle(new DipRect(box.X + BoxPadding, baseline - (line * 0.62), Swatch - 4, Swatch - 4), _values[i].Series.AccentColor);
            canvas.DrawText(_texts[i], box.X + BoxPadding + Swatch + BoxPadding, baseline, canvas.LabelSize, canvas.TextColor);
            baseline += line;
        }
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
        if (_x is not { } value || _axis is not { } axis || !canvas.TryGetPosition(axis, value, out var at))
        {
            return;
        }

        foreach (var pane in canvas.Layout.Panes)
        {
            canvas.Clip = pane;
            CrosshairOverlay.DrawAcross(canvas, axis, at, pane);
        }

        canvas.Clip = null;
        var labelHeight = canvas.GetLineHeight(canvas.LabelSize) + 4;
        var lastBottom = double.NegativeInfinity;
        foreach (var hit in _hits.OrderBy(hit => canvas.TryGetPoint(hit.Series.XAxis, hit.Series.YAxis, hit.X, hit.Y, out var point) ? point.Y : 0))
        {
            if (!canvas.TryGetPoint(hit.Series.XAxis, hit.Series.YAxis, hit.X, hit.Y, out var point))
            {
                continue;
            }

            var (pointX, y) = (point.X, point.Y);
            canvas.FillRectangle(new DipRect(pointX - MarkerSize, y - MarkerSize, MarkerSize * 2, MarkerSize * 2), hit.Series.AccentColor);

            // Labels of near values stack instead of covering each other.
            var top = Math.Max(y - (labelHeight / 2), lastBottom);
            var text = hit.Series.Name is { Length: > 0 } name ? $"{name}: {hit.FormatY()}" : hit.FormatY();
            canvas.DrawLabel(text, pointX + 8, top, hit.Series.AccentColor, canvas.BackgroundColor);
            lastBottom = top + labelHeight;
        }
    }
}
