using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>
/// Marks the breaks of axes: a faint line across the plot and two short slanted strokes on the axis, so that a gap in
/// the scale is never mistaken for a continuous one. Marks closer than a few units to the previous one are left out.
/// </summary>
internal static class AxisBreakMarks
{
    private const float Stroke = 5;
    private const double Closest = 6;

    // Runs every frame: an index loop, because an enumerator of the list would allocate.
    public static bool Any(ChartLayoutResult layout)
    {
        var axes = layout.Axes;
        for (var i = 0; i < axes.Count; i++)
        {
            if (axes[i].Axis.Scaling is BreakScaling or TimeGapScaling)
            {
                return true;
            }
        }

        return false;
    }

    public static void Draw(OverlayCanvas canvas, ChartLayoutResult layout)
    {
        foreach (var placement in layout.Axes)
        {
            switch (placement.Axis.Scaling)
            {
                case BreakScaling breaks:
                    foreach (var range in breaks.Breaks)
                    {
                        DrawAll(canvas, layout, placement, [range.Min]);
                    }

                    break;
                case TimeGapScaling gaps:
                    var starts = new double[gaps.Gaps.Count];
                    for (var i = 0; i < starts.Length; i++)
                    {
                        starts[i] = gaps.Gaps.StartOf(i) - gaps.Origin;
                    }

                    DrawAll(canvas, layout, placement, starts);
                    break;
            }
        }
    }

    private static void DrawAll(OverlayCanvas canvas, ChartLayoutResult layout, AxisPlacement placement, double[] values)
    {
        var axis = placement.Axis;
        var bounds = placement.Bounds;
        var transform = axis.IsHorizontal ? axis.CreateTransform(bounds.X, bounds.Width) : axis.CreateTransform(bounds.Y, bounds.Height);
        var (low, high) = axis.IsHorizontal ? (bounds.X, bounds.Right) : (bounds.Y, bounds.Bottom);
        var line = canvas.TextColor with { A = 70 };
        var mark = canvas.TextColor with { A = 220 };
        var last = double.NegativeInfinity;
        var area = layout.PlotArea;
        foreach (var value in values)
        {
            var at = transform.ToPixel(value);
            if (!double.IsFinite(at) || at <= low || at >= high || Math.Abs(at - last) < Closest)
            {
                continue;
            }

            last = at;
            var position = (float)at;
            if (axis.IsHorizontal)
            {
                var edge = (float)(axis.Position == AxisPosition.Top ? area.Y : area.Bottom);
                canvas.DrawLine(new Vector2(position, (float)area.Y), new Vector2(position, (float)area.Bottom), 1, line);
                canvas.DrawLine(new Vector2(position - 4, edge + Stroke), new Vector2(position - 1, edge - Stroke), 1.5f, mark);
                canvas.DrawLine(new Vector2(position + 1, edge + Stroke), new Vector2(position + 4, edge - Stroke), 1.5f, mark);
            }
            else
            {
                var edge = (float)(axis.Position == AxisPosition.Right ? area.Right : area.X);
                canvas.DrawLine(new Vector2((float)area.X, position), new Vector2((float)area.Right, position), 1, line);
                canvas.DrawLine(new Vector2(edge - Stroke, position + 4), new Vector2(edge + Stroke, position + 1), 1.5f, mark);
                canvas.DrawLine(new Vector2(edge - Stroke, position - 1), new Vector2(edge + Stroke, position - 4), 1.5f, mark);
            }
        }
    }
}
