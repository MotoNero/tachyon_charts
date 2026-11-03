using System.Numerics;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A smooth curve through points with ascending X. It stays between neighboring values: a measurement
/// must not show peaks that were never there.
/// </summary>
internal static class MonotoneSpline
{
    private const int Steps = 8;

    /// <summary>Adds the curve (Fritsch-Carlson).</summary>
    /// <param name="points">The points in pixels.</param>
    /// <param name="curve">Receives the vertices.</param>
    public static void Add(ReadOnlySpan<Vector2> points, List<Vector2> curve)
    {
        if (points.Length < 3)
        {
            foreach (var point in points)
            {
                curve.Add(point);
            }

            return;
        }

        curve.Add(points[0]);
        for (var i = 0; i < points.Length - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            var width = end.X - start.X;
            var startSlope = Slope(points, i);
            var endSlope = Slope(points, i + 1);
            for (var step = 1; step <= Steps; step++)
            {
                var t = step / (float)Steps;
                var square = t * t;
                var cube = square * t;
                var y = (((2 * cube) - (3 * square) + 1) * start.Y)
                    + ((cube - (2 * square) + t) * width * startSlope)
                    + (((-2 * cube) + (3 * square)) * end.Y)
                    + ((cube - square) * width * endSlope);
                curve.Add(new Vector2(start.X + (t * width), y));
            }
        }
    }

    private static float Slope(ReadOnlySpan<Vector2> points, int index)
    {
        if (index == 0)
        {
            return Secant(points, 0);
        }

        if (index == points.Length - 1)
        {
            return Secant(points, index - 1);
        }

        var before = Secant(points, index - 1);
        var after = Secant(points, index);
        return before * after <= 0 ? 0 : 2 * before * after / (before + after);
    }

    private static float Secant(ReadOnlySpan<Vector2> points, int index)
    {
        var width = points[index + 1].X - points[index].X;
        return width > 0 ? (points[index + 1].Y - points[index].Y) / width : 0;
    }
}
