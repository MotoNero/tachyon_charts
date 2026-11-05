using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>
/// Makes flat shapes in space as meshes: free polygons, rectangles and planes at a limit. They take the light, cut
/// planes and the pointer like any mesh, and with an <see cref="Material3D.Opacity"/> below 1 they show what lies behind
/// them, without sorting. Where a surface crosses an opaque or transparent plane, the crossing shows as an edge.
/// </summary>
public static class Shapes3D
{
    /// <summary>Gets the look of a plane at a limit: transparent and without highlights.</summary>
    public static Material3D LimitMaterial { get; } = new() { Opacity = 0.35, Gloss = 0 };

    /// <summary>
    /// Places a polygon, holes included, on a plane in space: a corner (x, y) of the polygon lies at
    /// <paramref name="origin"/> + x · <paramref name="across"/> + y · <paramref name="up"/>.
    /// </summary>
    /// <param name="polygon">The polygon in the coordinates of the plane.</param>
    /// <param name="origin">The place of (0, 0), in units of the axes.</param>
    /// <param name="across">The step for 1 along x.</param>
    /// <param name="up">The step for 1 along y.</param>
    /// <param name="color">The color.</param>
    /// <param name="material">How it takes the light, or <see langword="null"/> for the default.</param>
    /// <returns>The mesh.</returns>
    public static Mesh3D Polygon(Polygon polygon, Point3D origin, Point3D across, Point3D up, Rgba8 color, Material3D? material = null)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var (x, y, triangles) = polygon.Triangulate();
        var vertices = new Vertex3D[x.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vertex3D(origin + (across * x[i]) + (up * y[i]), color);
        }

        return new Mesh3D(vertices, triangles) { Material = material ?? new Material3D() };
    }

    /// <summary>Makes a flat polygon from corners in space. Corners off one plane are moved onto the plane that fits them best.</summary>
    /// <param name="corners">The corners in order, at least three, in units of the axes.</param>
    /// <param name="color">The color.</param>
    /// <param name="material">How it takes the light, or <see langword="null"/> for the default.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentException">There are fewer than three corners, or they lie on one line.</exception>
    public static Mesh3D Polygon(IReadOnlyList<Point3D> corners, Rgba8 color, Material3D? material = null)
    {
        ArgumentNullException.ThrowIfNull(corners);
        if (corners.Count < 3)
        {
            throw new ArgumentException($"A polygon needs at least three corners, there are {corners.Count}.", nameof(corners));
        }

        // The normal of Newell's method fits any order of corners, also when the polygon is concave.
        var (normal, middle) = (default(Point3D), default(Point3D));
        for (var i = 0; i < corners.Count; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % corners.Count]);
            normal += new Point3D((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            middle += a;
        }

        middle *= 1.0 / corners.Count;
        if (normal.Length < 1e-12 || !double.IsFinite(normal.Length))
        {
            throw new ArgumentException("The corners lie on one line or are not finite.", nameof(corners));
        }

        normal = normal.Normalized();
        var helper = Math.Abs(normal.Z) < 0.9 ? new Point3D(0, 0, 1) : new Point3D(1, 0, 0);
        var across = Point3D.Cross(helper, normal).Normalized();
        var up = Point3D.Cross(normal, across);
        var (xs, ys) = (new double[corners.Count], new double[corners.Count]);
        for (var i = 0; i < corners.Count; i++)
        {
            var offset = corners[i] - middle;
            (xs[i], ys[i]) = (Point3D.Dot(offset, across), Point3D.Dot(offset, up));
        }

        return Polygon(new Polygon(new PolygonRing(xs, ys)), middle, across, up, color, material);
    }

    /// <summary>Makes a rectangle from a corner and its two sides.</summary>
    /// <param name="corner">One corner, in units of the axes.</param>
    /// <param name="across">The first side.</param>
    /// <param name="up">The second side.</param>
    /// <param name="color">The color.</param>
    /// <param name="material">How it takes the light, or <see langword="null"/> for the default.</param>
    /// <returns>The mesh.</returns>
    public static Mesh3D Rectangle(Point3D corner, Point3D across, Point3D up, Rgba8 color, Material3D? material = null)
    {
        Vertex3D[] vertices = [new(corner, color), new(corner + across, color), new(corner + across + up, color), new(corner + up, color)];
        return new Mesh3D(vertices, [0, 1, 2, 0, 2, 3]) { Material = material ?? new Material3D() };
    }

    /// <summary>Makes a level plane at a limit of the third axis, such as an alarm threshold. The pointer shows the limit.</summary>
    /// <param name="x">The range of the first axis it covers.</param>
    /// <param name="y">The range of the second axis it covers.</param>
    /// <param name="limit">The value on the third axis.</param>
    /// <param name="color">The color.</param>
    /// <param name="material">How it takes the light, or <see langword="null"/> for <see cref="LimitMaterial"/>.</param>
    /// <returns>The mesh.</returns>
    public static Mesh3D Limit(DoubleRange x, DoubleRange y, double limit, Rgba8 color, Material3D? material = null)
    {
        var mesh = Rectangle(new Point3D(x.Min, y.Min, limit), new Point3D(x.Max - x.Min, 0, 0), new Point3D(0, y.Max - y.Min, 0), color, material ?? LimitMaterial);
        return new Mesh3D(mesh.Vertices, mesh.Indices) { Material = mesh.Material, Values = [limit, limit, limit, limit] };
    }
}
