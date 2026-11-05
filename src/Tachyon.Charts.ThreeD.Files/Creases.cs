using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>
/// Shares corners between faces that meet at a gentle angle and keeps them apart at a sharp edge. The light then runs
/// smoothly over a curved part and still shows the edges of a machined one.
/// </summary>
internal static class Creases
{
    /// <summary>The cosine of the angle between two faces above which the edge between them stays sharp.</summary>
    private static readonly double _smooth = Math.Cos(40 * Math.PI / 180);

    /// <summary>Joins equal corners of separate triangles, as in STL, then keeps sharp edges apart.</summary>
    /// <param name="corners">Three corners for each triangle.</param>
    /// <returns>The model.</returns>
    public static ModelFileData Join(Point3D[] corners)
    {
        var places = new Dictionary<(float X, float Y, float Z), int>(corners.Length / 4);
        var positions = new List<Point3D>(corners.Length / 4);
        var triangles = new int[corners.Length];
        for (var i = 0; i < corners.Length; i++)
        {
            var point = corners[i];

            // Adding zero turns -0 into 0, which is the same place.
            var key = ((float)point.X + 0f, (float)point.Y + 0f, (float)point.Z + 0f);
            if (!places.TryGetValue(key, out var index))
            {
                index = positions.Count;
                places.Add(key, index);
                positions.Add(point);
            }

            triangles[i] = index;
        }

        return Split([.. positions], triangles, null);
    }

    /// <summary>Gives a corner one point for each group of faces around it that meet at a gentle angle.</summary>
    /// <param name="positions">The places.</param>
    /// <param name="triangles">Three numbers of places for each triangle.</param>
    /// <param name="colors">The colors of the places, or <see langword="null"/>.</param>
    /// <returns>The model, with a point for each group.</returns>
    public static ModelFileData Split(Point3D[] positions, int[] triangles, Rgba8[]? colors)
    {
        // One chain of points for each place, and the face direction each point was made for.
        var first = new int[positions.Length];
        Array.Fill(first, -1);
        var next = new List<int>(positions.Length);
        var place = new List<int>(positions.Length);
        var facing = new List<Point3D>(positions.Length);
        var result = new int[triangles.Length];
        for (var t = 0; t < triangles.Length; t += 3)
        {
            var (a, b, c) = (positions[triangles[t]], positions[triangles[t + 1]], positions[triangles[t + 2]]);
            var face = Point3D.Cross(b - a, c - a).Normalized();
            for (var k = 0; k < 3; k++)
            {
                var at = triangles[t + k];
                var point = first[at];
                while (point >= 0 && Point3D.Dot(facing[point], face) < _smooth && face != default && facing[point] != default)
                {
                    point = next[point];
                }

                if (point < 0)
                {
                    point = place.Count;
                    place.Add(at);
                    facing.Add(face);
                    next.Add(first[at]);
                    first[at] = point;
                }

                result[t + k] = point;
            }
        }

        var points = new Point3D[place.Count];
        var pointColors = colors is null ? null : new Rgba8[place.Count];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = positions[place[i]];
            if (pointColors is not null)
            {
                pointColors[i] = colors![place[i]];
            }
        }

        return new ModelFileData(points, result, pointColors);
    }
}
