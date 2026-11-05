using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Data;

/// <summary>A closed line of a polygon: its corners in values of the axes. The last corner connects to the first.</summary>
public sealed class PolygonRing
{
    /// <summary>Initializes a new instance of the <see cref="PolygonRing"/> class.</summary>
    /// <param name="x">The X values of the corners.</param>
    /// <param name="y">The Y values of the corners.</param>
    /// <exception cref="ArgumentException">There are fewer than three corners, the lengths differ, or a value is not finite.</exception>
    public PolygonRing(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count || x.Count < 3)
        {
            throw new ArgumentException("A ring needs at least three corners with an X and a Y each.", nameof(y));
        }

        X = [.. x];
        Y = [.. y];
        for (var i = 0; i < X.Length; i++)
        {
            if (!double.IsFinite(X[i]) || !double.IsFinite(Y[i]))
            {
                throw new ArgumentException("The corners of a ring must be finite.", nameof(x));
            }
        }
    }

    /// <summary>Gets the X values of the corners.</summary>
    public double[] X { get; }

    /// <summary>Gets the Y values of the corners.</summary>
    public double[] Y { get; }

    /// <summary>Gets the number of corners.</summary>
    public int Count => X.Length;

    /// <summary>Gets twice the signed area; positive when the corners run counterclockwise with Y upwards.</summary>
    internal double DoubleArea
    {
        get
        {
            var sum = 0.0;
            for (int i = 0, j = Count - 1; i < Count; j = i++)
            {
                sum += (X[j] * Y[i]) - (X[i] * Y[j]);
            }

            return sum;
        }
    }
}

/// <summary>A polygon: an outer ring and holes cut out of it, for example a country with its lakes.</summary>
/// <param name="outer">The outer ring.</param>
/// <param name="holes">The holes, or <see langword="null"/> for none.</param>
public sealed class Polygon(PolygonRing outer, IReadOnlyList<PolygonRing>? holes = null)
{
    /// <summary>Gets the outer ring.</summary>
    public PolygonRing Outer { get; } = outer ?? throw new ArgumentNullException(nameof(outer));

    /// <summary>Gets the holes.</summary>
    public IReadOnlyList<PolygonRing> Holes { get; } = holes is null ? [] : [.. holes];

    /// <summary>Gets or sets the fill, or the default value for the palette of the theme.</summary>
    public Rgba8 Color { get; init; }

    /// <summary>Gets or sets the name shown under the pointer.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Splits the polygon into triangles, with the holes left out.</summary>
    /// <returns>The corners, and three numbers of corners for each triangle.</returns>
    public (double[] X, double[] Y, int[] Triangles) Triangulate() => PolygonTriangulator.Triangulate(this);

    /// <summary>Finds whether a point lies inside: inside the outer ring and in no hole.</summary>
    /// <param name="x">The X value.</param>
    /// <param name="y">The Y value.</param>
    /// <returns>Whether the point is inside.</returns>
    public bool Contains(double x, double y)
    {
        if (!Inside(Outer, x, y))
        {
            return false;
        }

        foreach (var hole in Holes)
        {
            if (Inside(hole, x, y))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Inside(PolygonRing ring, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            if ((ring.Y[i] > y) != (ring.Y[j] > y) && x < ((ring.X[j] - ring.X[i]) * (y - ring.Y[i]) / (ring.Y[j] - ring.Y[i])) + ring.X[i])
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>Cuts polygons with holes into triangles: the holes are joined to the outer ring by a bridge, then ears are clipped.</summary>
internal static class PolygonTriangulator
{
    /// <summary>Writes the corners of the triangles as indices into the returned lists of X and Y.</summary>
    public static (double[] X, double[] Y, int[] Triangles) Triangulate(Polygon polygon)
    {
        var (xs, ys) = Join(polygon);
        var count = xs.Count;
        var indices = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            indices.Add(i);
        }

        var triangles = new List<int>(Math.Max(0, (count - 2) * 3));
        var guard = 0;
        while (indices.Count > 3 && guard++ < count * count)
        {
            var clipped = false;
            for (var k = 0; k < indices.Count; k++)
            {
                var (a, b, c) = (indices[(k + indices.Count - 1) % indices.Count], indices[k], indices[(k + 1) % indices.Count]);
                if (!IsEar(xs, ys, indices, a, b, c))
                {
                    continue;
                }

                triangles.AddRange([a, b, c]);
                indices.RemoveAt(k);
                clipped = true;
                break;
            }

            // A ring that is not simple has no more ears; the rest falls out rather than looping.
            if (!clipped)
            {
                break;
            }
        }

        if (indices.Count == 3)
        {
            triangles.AddRange([indices[0], indices[1], indices[2]]);
        }

        return ([.. xs], [.. ys], [.. triangles]);
    }

    // The outer ring counterclockwise, each hole clockwise, joined at its rightmost corner to a visible corner of the ring.
    private static (List<double> X, List<double> Y) Join(Polygon polygon)
    {
        var (xs, ys) = Ordered(polygon.Outer, counterclockwise: true);
        foreach (var hole in polygon.Holes.OrderByDescending(hole => hole.X.Max()))
        {
            var (holeX, holeY) = Ordered(hole, counterclockwise: false);
            var (hx, hy) = (holeX.ToArray(), holeY.ToArray());
            var start = Array.IndexOf(hx, hx.Max());
            var bridge = Nearest(xs, ys, hx, hy, start);
            var joinedX = new List<double>(xs.Count + hx.Length + 2);
            var joinedY = new List<double>(joinedX.Capacity);
            for (var i = 0; i <= bridge; i++)
            {
                joinedX.Add(xs[i]);
                joinedY.Add(ys[i]);
            }

            for (var i = 0; i <= hx.Length; i++)
            {
                joinedX.Add(hx[(start + i) % hx.Length]);
                joinedY.Add(hy[(start + i) % hx.Length]);
            }

            for (var i = bridge; i < xs.Count; i++)
            {
                joinedX.Add(xs[i]);
                joinedY.Add(ys[i]);
            }

            (xs, ys) = (joinedX, joinedY);
        }

        return (xs, ys);
    }

    private static (List<double> X, List<double> Y) Ordered(PolygonRing ring, bool counterclockwise)
    {
        var xs = ring.X.ToList();
        var ys = ring.Y.ToList();
        if ((ring.DoubleArea > 0) != counterclockwise)
        {
            xs.Reverse();
            ys.Reverse();
        }

        return (xs, ys);
    }

    // The nearest corner of the ring that the hole corner sees without crossing an edge of the ring or of the hole
    // itself: with several corners at the same distance, the first one may lie on the far side of the hole.
    private static int Nearest(List<double> xs, List<double> ys, double[] hx, double[] hy, int start)
    {
        var (x, y) = (hx[start], hy[start]);
        var best = -1;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < xs.Count; i++)
        {
            var distance = ((xs[i] - x) * (xs[i] - x)) + ((ys[i] - y) * (ys[i] - y));
            if (distance >= bestDistance || Crosses(xs, ys, i, x, y) || CrossesHole(hx, hy, start, xs[i], ys[i]))
            {
                continue;
            }

            best = i;
            bestDistance = distance;
        }

        return best < 0 ? 0 : best;
    }

    private static bool Crosses(List<double> xs, List<double> ys, int corner, double x, double y)
    {
        for (int i = 0, j = xs.Count - 1; i < xs.Count; j = i++)
        {
            if (i == corner || j == corner)
            {
                continue;
            }

            if (Intersect(xs[corner], ys[corner], x, y, xs[j], ys[j], xs[i], ys[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CrossesHole(double[] hx, double[] hy, int start, double x, double y)
    {
        for (int i = 0, j = hx.Length - 1; i < hx.Length; j = i++)
        {
            if (i != start && j != start && Intersect(hx[start], hy[start], x, y, hx[j], hy[j], hx[i], hy[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Intersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        var d1 = Cross(cx, cy, dx, dy, ax, ay);
        var d2 = Cross(cx, cy, dx, dy, bx, by);
        var d3 = Cross(ax, ay, bx, by, cx, cy);
        var d4 = Cross(ax, ay, bx, by, dx, dy);
        return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
    }

    private static double Cross(double ax, double ay, double bx, double by, double px, double py) =>
        ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));

    private static bool IsEar(List<double> xs, List<double> ys, List<int> indices, int a, int b, int c)
    {
        // Convex at b for a counterclockwise ring, and no other corner inside the triangle.
        if (Cross(xs[a], ys[a], xs[b], ys[b], xs[c], ys[c]) <= 0)
        {
            return false;
        }

        foreach (var p in indices)
        {
            if (p == a || p == b || p == c || (xs[p] == xs[a] && ys[p] == ys[a]) || (xs[p] == xs[b] && ys[p] == ys[b]) || (xs[p] == xs[c] && ys[p] == ys[c]))
            {
                continue;
            }

            if (Cross(xs[a], ys[a], xs[b], ys[b], xs[p], ys[p]) >= 0 && Cross(xs[b], ys[b], xs[c], ys[c], xs[p], ys[p]) >= 0 && Cross(xs[c], ys[c], xs[a], ys[a], xs[p], ys[p]) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Finds the cells of a grid whose middle lies inside polygons, row by row: each row crosses the edges of all rings, and the cells between pairs of crossings are inside.</summary>
internal static class PolygonMask
{
    /// <summary>Writes NaN into the cells outside the polygons.</summary>
    /// <param name="values">The values, row by row.</param>
    /// <param name="columns">The middles of the columns, rising.</param>
    /// <param name="rows">The middles of the rows.</param>
    /// <param name="polygons">The polygons.</param>
    public static void Apply(Span<float> values, double[] columns, double[] rows, IReadOnlyList<Polygon> polygons)
    {
        var crossings = new List<double>();
        var inside = new bool[columns.Length];
        for (var row = 0; row < rows.Length; row++)
        {
            FindInside(columns, rows[row], polygons, crossings, inside);

            var line = values.Slice(row * columns.Length, columns.Length);
            for (var column = 0; column < columns.Length; column++)
            {
                if (!inside[column])
                {
                    line[column] = float.NaN;
                }
            }
        }
    }

    // Marks the cells of a row between pairs of crossings with the edges of all rings.
    private static void FindInside(double[] columns, double y, IReadOnlyList<Polygon> polygons, List<double> crossings, bool[] inside)
    {
        crossings.Clear();
        foreach (var polygon in polygons)
        {
            AddCrossings(polygon.Outer, y, crossings);
            foreach (var hole in polygon.Holes)
            {
                AddCrossings(hole, y, crossings);
            }
        }

        crossings.Sort();
        Array.Clear(inside);
        for (var k = 0; k + 1 < crossings.Count; k += 2)
        {
            var first = Lower(columns, crossings[k]);
            for (var column = first; column < columns.Length && columns[column] <= crossings[k + 1]; column++)
            {
                inside[column] = !inside[column];
            }
        }
    }

    private static void AddCrossings(PolygonRing ring, double y, List<double> crossings)
    {
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (y1, y2) = (ring.Y[j], ring.Y[i]);
            if ((y1 <= y && y < y2) || (y2 <= y && y < y1))
            {
                crossings.Add(ring.X[j] + ((y - y1) * (ring.X[i] - ring.X[j]) / (y2 - y1)));
            }
        }
    }

    private static int Lower(double[] sorted, double value)
    {
        var index = Array.BinarySearch(sorted, value);
        return index < 0 ? ~index : index;
    }
}
