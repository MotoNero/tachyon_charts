using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>
/// The values of the series that ask for them (<see cref="ChartSeries.ShowsValues"/>), written at their points. A
/// label that would cover one drawn before is left out, so dense data stay readable and show only some values. One
/// instance per chart keeps its buffers, so a frame allocates nothing once they have grown.
/// </summary>
internal sealed class ValueLabelMarks
{
    /// <summary>The most labels a series hands over per frame.</summary>
    public const int Limit = 2000;

    // At most three decimals and thousands grouped: 913, 0.5, 1,250.25; tiny values read as 0.
    private const string ValueFormat = "#,0.###";
    private const float Padding = 3;
    private const float Distance = 4;

    private readonly List<ValueLabel> _labels = [];
    private readonly List<DipRect> _placed = [];
    private readonly char[] _text = new char[48];

    // Runs every frame: an index loop, because an enumerator would allocate.
    public static bool Any(ChartSeries[] series)
    {
        for (var i = 0; i < series.Length; i++)
        {
            if (series[i].ShowsValues)
            {
                return true;
            }
        }

        return false;
    }

    public void Draw(OverlayCanvas canvas, ChartLayoutResult layout, ChartSeries[] series)
    {
        _placed.Clear();
        var plot = layout.PlotArea;
        var line = canvas.GetLineHeight(canvas.LabelSize);
        var back = canvas.BackgroundColor with { A = 190 };
        for (var s = 0; s < series.Length; s++)
        {
            var item = series[s];
            if (!item.ShowsValues)
            {
                continue;
            }

            _labels.Clear();
            item.CollectValueLabels(item.XAxis.VisibleRange, item.YAxis.VisibleRange, _labels);
            for (var i = 0; i < _labels.Count; i++)
            {
                var label = _labels[i];
                if (!TryPlace(canvas, item, label, out var point) || point.X < plot.X || point.X > plot.Right || point.Y < plot.Y || point.Y > plot.Bottom || !label.Value.TryFormat(_text, out var written, ValueFormat, ChartText.Culture))
                {
                    continue;
                }

                var text = _text.AsSpan(0, written);
                var width = canvas.MeasureWidth(text, canvas.LabelSize) + (2 * Padding);
                var height = line + Padding;
                var area = label.Place switch
                {
                    ValueLabelPlace.Center => new DipRect(point.X - (width / 2), point.Y - (height / 2), width, height),
                    ValueLabelPlace.Right => new DipRect(point.X + Distance, point.Y - (height / 2), width, height),
                    _ => new DipRect(point.X - (width / 2), point.Y - Distance - height, width, height),
                };
                if (Covers(area))
                {
                    continue;
                }

                _placed.Add(area);
                canvas.FillRectangle(area, back);
                canvas.DrawText(text, Math.Round(area.X + Padding), Math.Round(area.Y + (Padding / 2) + (line * 0.8)), canvas.LabelSize, canvas.TextColor);
            }
        }
    }

    // The X axis may lie vertically, as with the X axis downwards; each axis then gives the other coordinate.
    private static bool TryPlace(OverlayCanvas canvas, ChartSeries series, in ValueLabel label, out Vector2 point)
    {
        if (series.TryPlaceValueLabel(canvas.Layout.Panes[0], label, out point))
        {
            return true;
        }

        if (!canvas.TryGetPosition(series.XAxis, label.X, out var x) || !canvas.TryGetPosition(series.YAxis, label.Y, out var y) || !double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        point = series.XAxis.IsHorizontal ? new Vector2((float)x, (float)y) : new Vector2((float)y, (float)x);
        return true;
    }

    private bool Covers(in DipRect area)
    {
        for (var i = 0; i < _placed.Count; i++)
        {
            var other = _placed[i];
            if (area.X < other.X + other.Width && other.X < area.X + area.Width && area.Y < other.Y + other.Height && other.Y < area.Y + area.Height)
            {
                return true;
            }
        }

        return false;
    }
}
