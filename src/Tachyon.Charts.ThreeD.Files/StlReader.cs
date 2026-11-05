using System.Buffers.Binary;
using System.Text;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Reads STL. A binary file has no shared corners, so equal corners are joined to give a smooth surface.</summary>
internal static class StlReader
{
    private const int HeaderSize = 84;
    private const int TriangleSize = 50;

    public static ModelFileData Read(Stream stream)
    {
        var length = stream.Length - stream.Position;
        if (length <= 0)
        {
            throw new PointFileException("The file is empty.");
        }

        var head = new byte[Math.Min(HeaderSize, length)];
        stream.ReadExactly(head);
        stream.Seek(-head.Length, SeekOrigin.Current);
        var isText = head.AsSpan().StartsWith("solid"u8)
            && (length < HeaderSize || length != HeaderSize + ((long)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(80)) * TriangleSize));
        return isText ? ReadText(stream) : ReadBinary(stream, length);
    }

    private static ModelFileData ReadBinary(Stream stream, long length)
    {
        if (length < HeaderSize)
        {
            throw new PointFileException($"The file holds {length} bytes, a binary STL file needs at least {HeaderSize}.");
        }

        var header = new byte[HeaderSize];
        stream.ReadExactly(header);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80));
        if (count == 0)
        {
            throw new PointFileException("The file holds no triangles.");
        }

        if (count > Array.MaxLength / 9)
        {
            throw new PointFileException($"The file names {count} triangles, more than one model can take.");
        }

        var present = (length - HeaderSize) / TriangleSize;
        if (present < count)
        {
            throw new PointFileException($"The file ends after {present} of {count} triangles.");
        }

        var corners = new Point3D[count * 3];
        var buffer = new byte[TriangleSize * 4096];
        for (var first = 0L; first < count; first += 4096)
        {
            var take = (int)Math.Min(4096, count - first);
            var bytes = buffer.AsSpan(0, take * TriangleSize);
            stream.ReadExactly(bytes);
            for (var t = 0; t < take; t++)
            {
                var triangle = bytes.Slice(t * TriangleSize, TriangleSize);
                for (var c = 0; c < 3; c++)
                {
                    var at = 12 + (c * 12);
                    var point = new Point3D(
                        BinaryPrimitives.ReadSingleLittleEndian(triangle[at..]),
                        BinaryPrimitives.ReadSingleLittleEndian(triangle[(at + 4)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(triangle[(at + 8)..]));
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
                    {
                        throw new PointFileException($"Triangle {first + t + 1} has a corner that is not a finite number.");
                    }

                    corners[((first + t) * 3) + c] = point;
                }
            }
        }

        return Creases.Join(corners);
    }

    private static ModelFileData ReadText(Stream stream)
    {
        var corners = new List<Point3D>();
        TextLines.ForEach(stream, (line, number) =>
        {
            if (!line.StartsWith("vertex"u8))
            {
                if (line.Length > 0 && !IsKnown(line))
                {
                    throw new PointFileException($"Line {number} is not part of an STL file: '{Shorten(line)}'.");
                }

                return;
            }

            var rest = line["vertex"u8.Length..];
            if (!TextLines.TryTake(ref rest, out var x) || !TextLines.TryTake(ref rest, out var y) || !TextLines.TryTake(ref rest, out var z)
                || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                throw new PointFileException($"Line {number} needs three finite numbers after 'vertex'.");
            }

            corners.Add(new Point3D(x, y, z));
        });

        if (corners.Count == 0)
        {
            throw new PointFileException("The file holds no triangles.");
        }

        if (corners.Count % 3 != 0)
        {
            throw new PointFileException($"The file holds {corners.Count} corners, which is no whole number of triangles.");
        }

        return Creases.Join([.. corners]);
    }

    private static bool IsKnown(ReadOnlySpan<byte> line) =>
        line.StartsWith("solid"u8) || line.StartsWith("endsolid"u8) || line.StartsWith("facet"u8) || line.StartsWith("endfacet"u8)
        || line.StartsWith("outer"u8) || line.StartsWith("endloop"u8);

    private static string Shorten(ReadOnlySpan<byte> line) => Encoding.UTF8.GetString(line[..Math.Min(line.Length, 40)]);
}
