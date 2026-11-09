using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>
/// A map from measured points at uneven places, such as temperature over speed and load: the points are connected
/// to a surface. Beyond the outer points the surface ends, and points with a missing value are left out.
/// </summary>
/// <param name="x">The first coordinates.</param>
/// <param name="y">The second coordinates.</param>
/// <param name="z">The measured values.</param>
public sealed class MeasuredMap3D(double[] x, double[] y, double[] z)
{
    /// <summary>Gets a value indicating whether the values show as labels above their points; labels that would cover others are left out.</summary>
    public bool ShowsValues { get; init; }

    /// <summary>Gets the first coordinates.</summary>
    public double[] X { get; } = x ?? throw new ArgumentNullException(nameof(x));

    /// <summary>Gets the second coordinates.</summary>
    public double[] Y { get; } = y ?? throw new ArgumentNullException(nameof(y));

    /// <summary>Gets the measured values.</summary>
    public double[] Z { get; } = z ?? throw new ArgumentNullException(nameof(z));

    /// <summary>Gets the colors.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Viridis;

    /// <summary>Gets how the surface takes the light.</summary>
    public Material3D Material { get; init; } = new();

    /// <summary>Gets a value indicating whether the measured points are marked.</summary>
    public bool ShowsPoints { get; init; } = true;

    /// <summary>
    /// Gets the longest edge of a triangle as a share of the box width, or <see langword="null"/> for no limit.
    /// Longer triangles are left out, so that wide gaps between measurements stay empty.
    /// </summary>
    public double? LongestEdge { get; init; }

    /// <summary>Connects the points. The triangles form in the box, so that axes of different size weigh alike.</summary>
    /// <param name="xAxis">The first axis.</param>
    /// <param name="yAxis">The second axis.</param>
    /// <returns>The surface.</returns>
    internal Mesh3D CreateMesh(AxisBase xAxis, AxisBase yAxis)
    {
        var count = Math.Min(Math.Min(X.Length, Y.Length), Z.Length);
        var kept = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            if (double.IsFinite(X[i]) && double.IsFinite(Y[i]) && double.IsFinite(Z[i]))
            {
                kept.Add(i);
            }
        }

        var (boxX, boxY) = (new double[kept.Count], new double[kept.Count]);
        var range = DoubleRange.Empty;
        for (var i = 0; i < kept.Count; i++)
        {
            boxX[i] = AxisBox3D.ToBox(xAxis, X[kept[i]]);
            boxY[i] = AxisBox3D.ToBox(yAxis, Y[kept[i]]);
            range = range.Union(new DoubleRange(Z[kept[i]], Z[kept[i]]));
        }

        var vertices = new Vertex3D[kept.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var share = range.Max > range.Min ? (Z[kept[i]] - range.Min) / (range.Max - range.Min) : 0.5;
            vertices[i] = new Vertex3D(new Point3D(X[kept[i]], Y[kept[i]], Z[kept[i]]), Colormap.Sample(share));
        }

        return new Mesh3D(vertices, Shorten(Triangulation.Create(boxX, boxY), boxX, boxY)) { Material = Material };
    }

    private int[] Shorten(int[] triangles, double[] x, double[] y)
    {
        if (LongestEdge is not { } longest)
        {
            return triangles;
        }

        var limit = 2 * longest * 2 * longest;
        var kept = new List<int>(triangles.Length);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var (a, b, c) = (triangles[i], triangles[i + 1], triangles[i + 2]);
            if (Square(x, y, a, b) <= limit && Square(x, y, b, c) <= limit && Square(x, y, c, a) <= limit)
            {
                kept.AddRange([a, b, c]);
            }
        }

        return [.. kept];
    }

    private static double Square(double[] x, double[] y, int a, int b) =>
        ((x[a] - x[b]) * (x[a] - x[b])) + ((y[a] - y[b]) * (y[a] - y[b]));
}
