using System.Numerics;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// Smooth edges for filled triangles without multisampling: a band of one pixel along the outside of a slanted edge,
/// whose opacity falls from the color of the edge to nothing. It works the same in every renderer and in exported
/// pictures. Edges along the pixel grid are sharp already and get no band, so bars and columns stay crisp.
/// </summary>
internal static class MeshFringe
{
    /// <summary>The width of the band in pixels.</summary>
    public const float Width = 1f;

    private const float Straight = 0.01f;

    /// <summary>Adds the band along the edge from <paramref name="a"/> to <paramref name="b"/>.</summary>
    /// <param name="mesh">Receives two triangles.</param>
    /// <param name="a">The start of the edge and its color.</param>
    /// <param name="b">The end of the edge and its color.</param>
    /// <param name="inside">A point on the filled side of the edge.</param>
    public static void Add(List<MeshVertex> mesh, in MeshVertex a, in MeshVertex b, Vector2 inside)
    {
        var along = b.Position - a.Position;
        if (MathF.Abs(along.X) < Straight || MathF.Abs(along.Y) < Straight)
        {
            return;
        }

        var normal = Vector2.Normalize(new Vector2(along.Y, -along.X));
        var side = Vector2.Dot(normal, inside - a.Position);
        if (!float.IsFinite(side) || MathF.Abs(side) < 1e-4f)
        {
            return;
        }

        var outward = (side > 0 ? -normal : normal) * Width;
        var outerA = new MeshVertex(a.Position + outward, a.Color with { A = 0 });
        var outerB = new MeshVertex(b.Position + outward, b.Color with { A = 0 });
        mesh.Add(a);
        mesh.Add(b);
        mesh.Add(outerB);
        mesh.Add(a);
        mesh.Add(outerB);
        mesh.Add(outerA);
    }
}
