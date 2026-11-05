using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Reads the points and faces of OBJ. Everything else (normals, textures, materials, groups) is passed over.</summary>
internal static class ObjReader
{
    public static ModelFileData Read(Stream stream)
    {
        var positions = new List<Point3D>();
        var colors = new List<Rgba8>();
        var triangles = new List<int>();
        var corners = new List<int>();
        var (highest, highestLine) = (-1, 0L);
        try
        {
            TextLines.ForEach(stream, (line, number) =>
            {
                if (line.Length < 2 || line[1] is not ((byte)' ' or (byte)'\t'))
                {
                    return;
                }

                var rest = line[2..];
                if (line[0] == (byte)'v')
                {
                    AddPoint(rest, number, positions, colors);
                }
                else if (line[0] == (byte)'f')
                {
                    ReadCorners(rest, number, positions.Count, corners);
                    for (var i = 1; i + 1 < corners.Count; i++)
                    {
                        triangles.Add(corners[0]);
                        triangles.Add(corners[i]);
                        triangles.Add(corners[i + 1]);
                    }

                    foreach (var corner in corners)
                    {
                        if (corner > highest)
                        {
                            (highest, highestLine) = (corner, number);
                        }
                    }
                }
            });
        }
        catch (OutOfMemoryException exception)
        {
            throw new PointFileException("There is not enough memory for the model.", exception);
        }

        if (triangles.Count == 0)
        {
            throw new PointFileException("The file holds no faces.");
        }

        if (highest >= positions.Count)
        {
            throw new PointFileException($"The face in line {highestLine} uses point {highest + 1}, but the file holds only {positions.Count} points.");
        }

        var hasColors = colors.Count == positions.Count && colors.Count > 0;
        return Creases.Split([.. positions], [.. triangles], hasColors ? [.. colors] : null);
    }

    private static void AddPoint(ReadOnlySpan<byte> rest, long number, List<Point3D> positions, List<Rgba8> colors)
    {
        if (!TextLines.TryTake(ref rest, out var x) || !TextLines.TryTake(ref rest, out var y) || !TextLines.TryTake(ref rest, out var z)
            || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            throw new PointFileException($"Line {number} needs three finite numbers after 'v'.");
        }

        positions.Add(new Point3D(x, y, z));
        if (TextLines.TryTake(ref rest, out var red) && TextLines.TryTake(ref rest, out var green) && TextLines.TryTake(ref rest, out var blue))
        {
            colors.Add(new Rgba8(ToByte(red), ToByte(green), ToByte(blue), 255));
        }
    }

    private static void ReadCorners(ReadOnlySpan<byte> rest, long number, int count, List<int> corners)
    {
        corners.Clear();
        while (true)
        {
            rest = rest.TrimStart(" \t"u8);
            if (rest.IsEmpty)
            {
                break;
            }

            var end = rest.IndexOfAny(" \t"u8);
            var word = end < 0 ? rest : rest[..end];
            rest = end < 0 ? default : rest[end..];
            var slash = word.IndexOf((byte)'/');
            if (!int.TryParse(slash < 0 ? word : word[..slash], out var index) || index == 0)
            {
                throw new PointFileException($"Line {number} holds a corner that is not a point number.");
            }

            // Negative numbers count back from the last point read so far.
            var corner = index > 0 ? index - 1 : count + index;
            if (corner < 0)
            {
                throw new PointFileException($"Line {number} counts back {-index} points, but only {count} are read so far.");
            }

            corners.Add(corner);
        }

        if (corners.Count < 3)
        {
            throw new PointFileException($"The face in line {number} has fewer than three corners.");
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
