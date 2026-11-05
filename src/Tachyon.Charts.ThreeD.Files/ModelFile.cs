using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>The triangles of a model file.</summary>
/// <param name="positions">The points.</param>
/// <param name="triangles">Three numbers of points for each triangle.</param>
/// <param name="colors">The colors of the points, or <see langword="null"/> when the file has none.</param>
public sealed class ModelFileData(Point3D[] positions, int[] triangles, Rgba8[]? colors)
{
    private static readonly Rgba8 _gray = new(190, 196, 204, 255);

    /// <summary>Gets the points. A corner where faces meet at a sharp edge exists once for each side of the edge, so the edge stays sharp in the light.</summary>
    public Point3D[] Positions { get; } = positions;

    /// <summary>Gets three numbers of points for each triangle.</summary>
    public int[] Triangles { get; } = triangles;

    /// <summary>Gets the colors of the points, or <see langword="null"/> when the file has none.</summary>
    public Rgba8[]? Colors { get; } = colors;

    /// <summary>Gets the number of triangles.</summary>
    public int TriangleCount => Triangles.Length / 3;

    /// <summary>Gets the smallest coordinates of all points.</summary>
    public Point3D Lowest => Bounds().Low;

    /// <summary>Gets the largest coordinates of all points.</summary>
    public Point3D Highest => Bounds().High;

    /// <summary>Creates a surface for a chart.</summary>
    /// <param name="color">One color for the whole model, or <see langword="null"/> for the colors of the file, or gray when it has none.</param>
    /// <param name="material">How the surface takes the light, or <see langword="null"/> for the default.</param>
    /// <returns>The surface.</returns>
    public Mesh3D ToMesh(Rgba8? color = null, Material3D? material = null)
    {
        var vertices = new Vertex3D[Positions.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vertex3D(Positions[i], color ?? Colors?[i] ?? _gray);
        }

        return new Mesh3D(vertices, Triangles) { Material = material ?? new Material3D() };
    }

    /// <summary>Creates a surface colored by a value per point, such as a measured temperature. The pointer shows the value.</summary>
    /// <param name="values">One value for each point of <see cref="Positions"/>.</param>
    /// <param name="colormap">The colors.</param>
    /// <param name="range">The values at the two ends of the colors.</param>
    /// <param name="material">How the surface takes the light, or <see langword="null"/> for the default.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="ArgumentException">The number of values differs from the number of points.</exception>
    public Mesh3D ToMesh(IReadOnlyList<double> values, Colormap colormap, DoubleRange range, Material3D? material = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(colormap);
        if (values.Count != Positions.Length)
        {
            throw new ArgumentException($"There are {values.Count} values for {Positions.Length} points.", nameof(values));
        }

        var vertices = new Vertex3D[Positions.Length];
        var span = range.Max - range.Min;
        for (var i = 0; i < vertices.Length; i++)
        {
            var value = values[i];
            vertices[i] = new Vertex3D(Positions[i], double.IsFinite(value) ? colormap.Sample(Position(value, range.Min, span)) : _gray);
        }

        return new Mesh3D(vertices, Triangles) { Material = material ?? new Material3D(), Values = values };
    }

    /// <summary>
    /// Adds the model to a chart to scale: all three axes get the length of the longest side, centered on the model, so
    /// that it keeps its true proportions in the cube of the chart. The axes take the coordinates of the file as they are.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="mesh">The surface, or <see langword="null"/> for <see cref="ToMesh(Rgba8?, Material3D?)"/>.</param>
    /// <param name="unit">The unit of the coordinates, for the titles of the axes.</param>
    /// <returns>The chart.</returns>
    public Chart3DBuilder AddToScale(Chart3DBuilder chart, Mesh3D? mesh = null, string unit = "mm")
    {
        ArgumentNullException.ThrowIfNull(chart);
        var (low, high) = Bounds();
        var side = Math.Max(Math.Max(high.X - low.X, high.Y - low.Y), high.Z - low.Z);
        side = side > 0 ? side * 1.02 : 1;
        var center = (low + high) * 0.5;
        return chart.XAxis(center.X - (side / 2), center.X + (side / 2), $"x in {unit}")
            .YAxis(center.Y - (side / 2), center.Y + (side / 2), $"y in {unit}")
            .ZAxis(center.Z - (side / 2), center.Z + (side / 2), $"z in {unit}")
            .AddMesh(mesh ?? ToMesh());
    }

    // A single value in the range takes the middle of the colors.
    private static double Position(double value, double minimum, double span) => span > 0 ? (value - minimum) / span : 0.5;

    private (Point3D Low, Point3D High) Bounds()
    {
        var low = new Point3D(double.MaxValue, double.MaxValue, double.MaxValue);
        var high = new Point3D(double.MinValue, double.MinValue, double.MinValue);
        foreach (var point in Positions)
        {
            low = new Point3D(Math.Min(low.X, point.X), Math.Min(low.Y, point.Y), Math.Min(low.Z, point.Z));
            high = new Point3D(Math.Max(high.X, point.X), Math.Max(high.Y, point.Y), Math.Max(high.Z, point.Z));
        }

        return Positions.Length > 0 ? (low, high) : (default, default);
    }
}

/// <summary>
/// Reads models made of triangles from STL (text and binary) and OBJ with code of its own. Materials, textures and the
/// normals of the file are not read: the chart lights the model itself and keeps edges sharp where faces meet at more
/// than 40°.
/// </summary>
public static class ModelFile
{
    /// <summary>Reads a file by its extension.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The triangles.</returns>
    /// <exception cref="PointFileException">The file is damaged or of an unknown kind.</exception>
    public static ModelFileData Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path).ToUpperInvariant();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return extension switch
        {
            ".STL" => ReadStl(stream),
            ".OBJ" => ReadObj(stream),
            _ => throw new PointFileException($"The kind of file '{extension}' is unknown. Known are STL and OBJ."),
        };
    }

    /// <summary>Reads an STL file, as text or binary.</summary>
    /// <param name="stream">The file. It must allow seeking, as the kind is told from the length.</param>
    /// <returns>The triangles.</returns>
    /// <exception cref="PointFileException">The file is damaged.</exception>
    public static ModelFileData ReadStl(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The stream must allow seeking.", nameof(stream));
        }

        return StlReader.Read(stream);
    }

    /// <summary>Reads the points (<c>v</c>, optionally with red, green and blue from 0 to 1) and faces (<c>f</c>) of an OBJ file. Faces with more than three corners become a fan of triangles.</summary>
    /// <param name="stream">The file.</param>
    /// <returns>The triangles.</returns>
    /// <exception cref="PointFileException">The file is damaged.</exception>
    public static ModelFileData ReadObj(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ObjReader.Read(stream);
    }
}
