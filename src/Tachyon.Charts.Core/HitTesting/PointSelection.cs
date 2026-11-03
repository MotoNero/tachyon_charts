using System.Numerics;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Core.HitTesting;

/// <summary>
/// An area of the screen in physical pixels: a rectangle, narrowed by a polygon if there is one.
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Bottom">The bottom edge.</param>
/// <param name="Polygon">The closed outline, or empty for the whole rectangle.</param>
public readonly record struct SelectionArea(float Left, float Top, float Right, float Bottom, ReadOnlyMemory<Vector2> Polygon)
{
    /// <summary>Tests whether a position lies in the area. Edges of the rectangle belong to it.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>Whether it does.</returns>
    public bool Contains(double x, double y)
    {
        if (x < Left || x > Right || y < Top || y > Bottom)
        {
            return false;
        }

        var outline = Polygon.Span;
        if (outline.IsEmpty)
        {
            return true;
        }

        // Crossing number: an odd count of edges right of the position means inside.
        var inside = false;
        var previous = outline[^1];
        foreach (var current in outline)
        {
            if ((current.Y > y) != (previous.Y > y)
                && x < ((previous.X - current.X) * (y - current.Y) / (previous.Y - current.Y)) + current.X)
            {
                inside = !inside;
            }

            previous = current;
        }

        return inside;
    }

    /// <summary>Creates the area of a polygon.</summary>
    /// <param name="outline">The closed outline.</param>
    /// <returns>The area.</returns>
    public static SelectionArea FromPolygon(Vector2[] outline)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var point in outline)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return new SelectionArea(min.X, min.Y, max.X, max.Y, outline);
    }
}

/// <summary>
/// Finds all points of data inside an area.
/// </summary>
internal static class PointSelection
{
    /// <summary>Collects the indices of all points inside an area.</summary>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values.</param>
    /// <param name="origin">The value zero of the X axis in data units.</param>
    /// <param name="frame">The place of the series.</param>
    /// <param name="area">The area.</param>
    /// <param name="isSorted">Whether X never decreases, which narrows the search.</param>
    /// <param name="firstIndex">The number of the first point.</param>
    /// <param name="indices">Receives the numbers.</param>
    public static void Collect<TX, TY>(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in SeriesFrame frame, in SelectionArea area, bool isSorted, long firstIndex, List<long> indices)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>
    {
        var start = 0;
        var end = x.Length;
        if (isSorted)
        {
            var first = frame.X.ToData(area.Left);
            var second = frame.X.ToData(area.Right);
            start = LowerBound(x, origin, Math.Min(first, second));
            end = LowerBound(x, origin, Math.BitIncrement(Math.Max(first, second)));
        }

        for (var i = start; i < end; i++)
        {
            var valueY = double.CreateTruncating(y[i]);
            var valueX = double.CreateTruncating(x[i] - origin);
            if (double.IsFinite(valueX) && double.IsFinite(valueY) && area.Contains(frame.X.ToPixel(valueX), frame.Y.ToPixel(valueY)))
            {
                indices.Add(firstIndex + i);
            }
        }
    }

    private static int LowerBound<TX>(ReadOnlySpan<TX> x, TX origin, double value)
        where TX : unmanaged, INumber<TX>
    {
        var low = 0;
        var high = x.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (double.CreateTruncating(x[middle] - origin) < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
