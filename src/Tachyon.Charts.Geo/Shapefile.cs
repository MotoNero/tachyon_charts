using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Tachyon.Charts.Geo;

/// <summary>The kind of the shapes of a shapefile.</summary>
public enum ShapeKind
{
    /// <summary>Single points.</summary>
    Point,

    /// <summary>Lines of one or more parts.</summary>
    Line,

    /// <summary>Areas of one or more rings: outer rings run clockwise, holes counterclockwise.</summary>
    Polygon,

    /// <summary>Groups of points.</summary>
    MultiPoint,
}

/// <summary>A shape of a shapefile: its parts as rows of X and Y, and the values of its record in the table.</summary>
/// <param name="Parts">The parts: the rings of an area, the pieces of a line, or the points, each as X and Y.</param>
/// <param name="Attributes">The values of the record, by the name of the field.</param>
public sealed record GeoShape(IReadOnlyList<(double[] X, double[] Y)> Parts, IReadOnlyDictionary<string, string> Attributes);

/// <summary>The shapes of an ESRI shapefile with the values of their records.</summary>
/// <param name="Kind">The kind of the shapes.</param>
/// <param name="Shapes">The shapes, in the order of the file; null shapes are left out.</param>
/// <param name="Fields">The names of the fields of the table, empty without a table.</param>
public sealed record Shapefile(ShapeKind Kind, IReadOnlyList<GeoShape> Shapes, IReadOnlyList<string> Fields);

/// <summary>
/// Reads ESRI shapefiles: the shapes of the <c>.shp</c> file and, when it lies beside it, the table of the <c>.dbf</c>
/// file. Shapes with Z or M values give their X and Y. A damaged file fails with a message that names the file, the
/// record and the byte where it went wrong.
/// </summary>
public static class ShapefileReader
{
    private const int FileCode = 9994;
    private const int HeaderSize = 100;

    /// <summary>Reads a shapefile and the table beside it.</summary>
    /// <param name="path">The path of the <c>.shp</c> file.</param>
    /// <returns>The shapes.</returns>
    /// <exception cref="InvalidDataException">The file is damaged or of an unknown kind.</exception>
    public static Shapefile Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var bytes = File.ReadAllBytes(path);
        var table = Path.ChangeExtension(path, ".dbf");
        var (fields, records) = File.Exists(table) ? ReadTable(table) : ([], []);
        return Read(bytes, Path.GetFileName(path), fields, records);
    }

    /// <summary>Reads the shapes of a <c>.shp</c> file in memory.</summary>
    /// <param name="shapes">The bytes of the <c>.shp</c> file.</param>
    /// <param name="table">The bytes of the <c>.dbf</c> file, or empty without a table.</param>
    /// <param name="name">The name for messages.</param>
    /// <returns>The shapes.</returns>
    /// <exception cref="InvalidDataException">The data are damaged or of an unknown kind.</exception>
    public static Shapefile Read(ReadOnlySpan<byte> shapes, ReadOnlySpan<byte> table, string name = "shapefile")
    {
        var (fields, records) = table.IsEmpty ? ([], []) : ReadTable(table, name);
        return Read(shapes.ToArray(), name, fields, records);
    }

    private static Shapefile Read(byte[] bytes, string name, IReadOnlyList<string> fields, IReadOnlyList<Dictionary<string, string>> records)
    {
        if (bytes.Length < HeaderSize)
        {
            throw Damaged(name, "is shorter than the header of 100 bytes", 0);
        }

        if (BinaryPrimitives.ReadInt32BigEndian(bytes) != FileCode)
        {
            throw Damaged(name, "is no shapefile: the file code is not 9994", 0);
        }

        var declared = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(24)) * 2L;
        if (declared > bytes.Length)
        {
            throw Damaged(name, $"is cut off: the header announces {declared:N0} bytes, the file has {bytes.Length:N0}", 24);
        }

        var kind = KindOf(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(32)), name, 32);
        var shapes = new List<GeoShape>();
        var at = HeaderSize;
        var record = 0;
        while (at + 8 <= declared)
        {
            record++;
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at + 4)) * 2;
            var content = at + 8;
            if (length < 4 || content + length > declared)
            {
                throw Damaged(name, $"has a record {record} of {length} bytes that runs past the end", at);
            }

            var shape = ReadShape(bytes.AsSpan(content, length), kind, name, record, content);
            if (shape is not null)
            {
                var attributes = record - 1 < records.Count ? records[record - 1] : (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
                shapes.Add(new GeoShape(shape, attributes));
            }

            at = content + length;
        }

        if (at != declared)
        {
            throw Damaged(name, $"ends inside the header of record {record + 1}", at);
        }

        return new Shapefile(kind, shapes, fields);
    }

    private static ShapeKind KindOf(int type, string name, long offset) => type switch
    {
        1 or 11 or 21 => ShapeKind.Point,
        3 or 13 or 23 => ShapeKind.Line,
        5 or 15 or 25 => ShapeKind.Polygon,
        8 or 18 or 28 => ShapeKind.MultiPoint,
        _ => throw Damaged(name, $"has shapes of the type {type}, which this reader does not know (multipatches are not supported)", offset),
    };

    // A record: its type, then the points of a point, the points of a group, or the parts of a line or an area.
    private static List<(double[] X, double[] Y)>? ReadShape(ReadOnlySpan<byte> content, ShapeKind kind, string name, int record, long offset)
    {
        var type = BinaryPrimitives.ReadInt32LittleEndian(content);
        if (type == 0)
        {
            return null;
        }

        if (KindOf(type, name, offset) != kind)
        {
            throw Damaged(name, $"mixes kinds: record {record} has the type {type}", offset);
        }

        switch (kind)
        {
            case ShapeKind.Point:
                Need(content, 20, name, record, offset);
                return [([Double(content, 4)], [Double(content, 12)])];
            case ShapeKind.MultiPoint:
                {
                    Need(content, 40, name, record, offset);
                    var count = BinaryPrimitives.ReadInt32LittleEndian(content[36..]);
                    Need(content, 40 + (count * 16L), name, record, offset);
                    var (x, y) = (new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        (x[i], y[i]) = (Double(content, 40 + (i * 16)), Double(content, 48 + (i * 16)));
                    }

                    return [(x, y)];
                }

            default:
                {
                    Need(content, 44, name, record, offset);
                    var parts = BinaryPrimitives.ReadInt32LittleEndian(content[36..]);
                    var points = BinaryPrimitives.ReadInt32LittleEndian(content[40..]);
                    if (parts < 0 || points < 0)
                    {
                        throw Damaged(name, $"has a record {record} with {parts} parts and {points} points", offset);
                    }

                    Need(content, 44 + (parts * 4L) + (points * 16L), name, record, offset);
                    var start = 44 + (parts * 4);
                    var result = new List<(double[], double[])>(parts);
                    for (var part = 0; part < parts; part++)
                    {
                        var first = BinaryPrimitives.ReadInt32LittleEndian(content[(44 + (part * 4))..]);
                        var end = part + 1 < parts ? BinaryPrimitives.ReadInt32LittleEndian(content[(48 + (part * 4))..]) : points;
                        if (first < 0 || end > points || end < first)
                        {
                            throw Damaged(name, $"has a record {record} whose part {part + 1} runs from point {first} to {end} of {points}", offset);
                        }

                        var (x, y) = (new double[end - first], new double[end - first]);
                        for (var i = first; i < end; i++)
                        {
                            (x[i - first], y[i - first]) = (Double(content, start + (i * 16)), Double(content, start + 8 + (i * 16)));
                        }

                        result.Add((x, y));
                    }

                    return result;
                }
        }
    }

    private static double Double(ReadOnlySpan<byte> content, int at) => BinaryPrimitives.ReadDoubleLittleEndian(content[at..]);

    private static void Need(ReadOnlySpan<byte> content, long bytes, string name, int record, long offset)
    {
        if (content.Length < bytes)
        {
            throw Damaged(name, $"has a record {record} of {content.Length} bytes that needs {bytes}", offset);
        }
    }

    private static InvalidDataException Damaged(string name, string what, long offset) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{name} {what} (at byte {offset})."));

    private static (IReadOnlyList<string> Fields, IReadOnlyList<Dictionary<string, string>> Records) ReadTable(string path) =>
        ReadTable(File.ReadAllBytes(path), Path.GetFileName(path));

    // dBASE: a header with the number of records and the sizes, the fields of 32 bytes each up to 0x0D, then the records.
    private static (IReadOnlyList<string> Fields, IReadOnlyList<Dictionary<string, string>> Records) ReadTable(ReadOnlySpan<byte> bytes, string name)
    {
        if (bytes.Length < 32)
        {
            throw Damaged(name, "is shorter than the header of a table", 0);
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var headerSize = BinaryPrimitives.ReadInt16LittleEndian(bytes[8..]);
        var recordSize = BinaryPrimitives.ReadInt16LittleEndian(bytes[10..]);
        if (count < 0 || headerSize < 33 || recordSize < 1 || headerSize + ((long)count * recordSize) > bytes.Length)
        {
            throw Damaged(name, $"announces {count:N0} records of {recordSize} bytes after {headerSize} bytes, more than it holds", 4);
        }

        var fields = new List<(string Name, int Offset, int Length)>();
        var offset = 1;
        for (var at = 32; at + 32 <= headerSize && bytes[at] != 0x0D; at += 32)
        {
            var fieldName = Encoding.ASCII.GetString(bytes.Slice(at, 11)).TrimEnd('\0', ' ');
            var length = bytes[at + 16];
            fields.Add((fieldName, offset, length));
            offset += length;
        }

        var records = new List<Dictionary<string, string>>(count);
        for (var record = 0; record < count; record++)
        {
            var row = bytes.Slice(headerSize + (record * recordSize), recordSize);
            var values = new Dictionary<string, string>(fields.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (fieldName, at, length) in fields)
            {
                values[fieldName] = at + length <= row.Length ? Encoding.UTF8.GetString(row.Slice(at, length)).Trim(' ', ' ') : string.Empty;
            }

            records.Add(values);
        }

        return ([.. fields.Select(field => field.Name)], records);
    }
}
