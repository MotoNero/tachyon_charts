using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>How a surface takes the light.</summary>
public sealed record Material3D
{
    /// <summary>Gets a value indicating whether light falls on the surface. Without it, colors stay exactly as given.</summary>
    public bool IsLit { get; init; } = true;

    /// <summary>Gets the share of the surface that covers what lies behind it, from 0 to 1.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Gets the strength of the highlights, from 0 to 1.</summary>
    public double Gloss { get; init; } = 0.18;

    /// <summary>Gets the sharpness of the highlights. Higher values give smaller highlights.</summary>
    public double Sharpness { get; init; } = 64;
}

/// <summary>A point of a surface.</summary>
/// <param name="Position">The place in units of the axes.</param>
/// <param name="Color">The color.</param>
public readonly record struct Vertex3D(Point3D Position, Rgba8 Color);

/// <summary>A surface made of triangles. Triangles with a corner that is not finite are left out.</summary>
/// <param name="vertices">The points.</param>
/// <param name="indices">Three numbers of points for each triangle.</param>
public sealed class Mesh3D(IReadOnlyList<Vertex3D> vertices, IReadOnlyList<int> indices)
{
    /// <summary>Gets the points.</summary>
    public IReadOnlyList<Vertex3D> Vertices { get; } = vertices ?? throw new ArgumentNullException(nameof(vertices));

    /// <summary>Gets the triangles.</summary>
    public IReadOnlyList<int> Indices { get; } = indices ?? throw new ArgumentNullException(nameof(indices));

    /// <summary>Gets or sets how the surface takes the light.</summary>
    public Material3D Material { get; init; } = new();

    /// <summary>
    /// Gets a value per point, such as a measured temperature, or <see langword="null"/>. The value under the pointer is
    /// blended from the corners of the triangle it points at.
    /// </summary>
    /// <exception cref="ArgumentException">The number of values differs from the number of points.</exception>
    public IReadOnlyList<double>? Values
    {
        get;
        init => field = value is null || value.Count == Vertices.Count
            ? value
            : throw new ArgumentException($"There are {value.Count} values for {Vertices.Count} points.", nameof(value));
    }

    /// <summary>Gets the triangles whose corners are all finite.</summary>
    /// <param name="positions">The places of the points.</param>
    /// <param name="indices">The triangles.</param>
    /// <returns>The valid triangles.</returns>
    internal static uint[] ValidIndices(ReadOnlySpan<Point3D> positions, IReadOnlyList<int> indices)
    {
        var valid = new List<uint>(indices.Count);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            if (IsFinite(positions[indices[i]]) && IsFinite(positions[indices[i + 1]]) && IsFinite(positions[indices[i + 2]]))
            {
                valid.Add((uint)indices[i]);
                valid.Add((uint)indices[i + 1]);
                valid.Add((uint)indices[i + 2]);
            }
        }

        return [.. valid];
    }

    /// <summary>Calculates the direction away from the surface at each point, as the mean of the adjacent triangles.</summary>
    /// <param name="positions">The places of the points.</param>
    /// <param name="indices">The triangles.</param>
    /// <returns>One direction of length 1 per point, or zero for a point without a valid triangle.</returns>
    internal static Point3D[] CalculateNormals(ReadOnlySpan<Point3D> positions, IReadOnlyList<int> indices)
    {
        var normals = new Point3D[positions.Length];
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
            var face = Point3D.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (double.IsFinite(face.X) && double.IsFinite(face.Y) && double.IsFinite(face.Z))
            {
                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].Normalized();
        }

        return normals;
    }

    private static bool IsFinite(Point3D point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
}
