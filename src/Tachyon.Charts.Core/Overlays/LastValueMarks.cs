using System.Numerics;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>
/// The newest value of each series that asks for it, as a plate in the color of the series at its vertical axis.
/// Plates move apart so that they never overlap; a small arrow points at the value. One instance per chart keeps
/// its buffers, so a frame allocates nothing.
/// </summary>
internal sealed class LastValueMarks
{
    private const float LabelSize = 11;
    private const float Padding = 4;

    private static readonly string[] _formats = ["F0", "F1", "F2", "F3", "F4", "F5", "F6"];

    private readonly char[] _text = new char[48];
    private Entry[] _entries = new Entry[8];
    private double[] _places = new double[8];

    // Runs every frame: an index loop, because an enumerator would allocate.
    public static bool Any(ChartSeries[] series)
    {
        for (var i = 0; i < series.Length; i++)
        {
            if (series[i].ShowsLastValue)
            {
                return true;
            }
        }

        return false;
    }

    public void Draw(OverlayCanvas canvas, ChartLayoutResult layout, ChartSeries[] series)
    {
        var count = Collect(layout, series);
        if (count == 0)
        {
            return;
        }

        // Sorted from the top, then spread apart within the plot area. Few marks: an insertion sort, which allocates nothing.
        for (var i = 1; i < count; i++)
        {
            var entry = _entries[i];
            var j = i - 1;
            while (j >= 0 && _entries[j].Y > entry.Y)
            {
                _entries[j + 1] = _entries[j];
                j--;
            }

            _entries[j + 1] = entry;
        }

        var line = canvas.GetLineHeight(LabelSize) + 2;
        for (var i = 0; i < count; i++)
        {
            _places[i] = _entries[i].Y;
        }

        PieLabelOverlay.Spread(_places.AsSpan(0, count), layout.PlotArea.Y + (line / 2), layout.PlotArea.Bottom - (line / 2), line);
        for (var i = 0; i < count; i++)
        {
            DrawOne(canvas, layout, _entries[i], (float)_places[i], line);
        }
    }

    private static Rgba8 ContrastTo(Rgba8 color) => (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B) > 150
        ? new Rgba8(20, 20, 20, 255)
        : new Rgba8(255, 255, 255, 255);

    private int Collect(ChartLayoutResult layout, ChartSeries[] series)
    {
        var count = 0;
        for (var i = 0; i < series.Length; i++)
        {
            var item = series[i];
            if (!item.ShowsLastValue || !item.TryGetLatestValue(out var value) || !TryPlace(layout, item, out var bounds, out var isRight))
            {
                continue;
            }

            var transform = item.YAxis.CreateTransform(bounds.Y, bounds.Height);
            var y = transform.ToPixel(value);
            if (!double.IsFinite(y))
            {
                continue;
            }

            if (count == _entries.Length)
            {
                Array.Resize(ref _entries, count * 2);
                Array.Resize(ref _places, count * 2);
            }

            // A value outside the view sticks to the edge, so that it still shows where it lies.
            y = Math.Clamp(y, bounds.Y, bounds.Bottom);
            _entries[count++] = new Entry(y, value, item.AccentColor, bounds, isRight, item.YAxis.VisibleRange.Length);
        }

        return count;
    }

    // The placement of the vertical axis of a series; a hidden axis puts the plate at the right edge of the plot.
    private static bool TryPlace(ChartLayoutResult layout, ChartSeries series, out DipRect bounds, out bool isRight)
    {
        var axes = layout.Axes;
        for (var i = 0; i < axes.Count; i++)
        {
            if (axes[i].Axis == series.YAxis)
            {
                bounds = axes[i].Bounds;
                isRight = bounds.X >= layout.PlotArea.Right - 1;
                return true;
            }
        }

        var plot = layout.PlotArea;
        bounds = new DipRect(plot.Right, plot.Y, 0, plot.Height);
        isRight = true;
        return layout.Panes.Count > 0;
    }

    private void DrawOne(OverlayCanvas canvas, ChartLayoutResult layout, in Entry entry, float y, float line)
    {
        var digits = Math.Clamp((int)Math.Ceiling(-Math.Log10(Math.Max(entry.Span, 1e-12) / 1000)), 0, _formats.Length - 1);
        if (!entry.Value.TryFormat(_text, out var written, _formats[digits], ChartText.Culture))
        {
            return;
        }

        var text = _text.AsSpan(0, written);
        var width = canvas.MeasureWidth(text, LabelSize) + (Padding * 2);
        var plot = layout.PlotArea;
        var left = entry.IsRight ? plot.Right + 4 : plot.X - 4 - width;
        var plate = new DipRect(left, y - (line / 2), width, line);
        canvas.FillRectangle(plate, entry.Color);
        var tip = entry.IsRight ? (float)plot.Right : (float)plot.X;
        var edge = entry.IsRight ? (float)left : (float)(left + width);
        canvas.FillTriangle(new Vector2(tip, (float)entry.Y), new Vector2(edge, y - (line / 2)), new Vector2(edge, y + (line / 2)), entry.Color);
        canvas.DrawText(text, left + Padding, y + (line * 0.3), LabelSize, ContrastTo(entry.Color));
    }

    private readonly record struct Entry(double Y, double Value, Rgba8 Color, DipRect Bounds, bool IsRight, double Span);
}
