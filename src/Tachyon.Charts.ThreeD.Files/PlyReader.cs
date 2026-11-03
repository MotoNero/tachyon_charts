using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Reads the vertices of a PLY file. Faces and other parts behind them stay unread.</summary>
internal static class PlyReader
{
    private const int LongestHeader = 1 << 20;
    private const int RecordsPerRead = 1 << 16;
    private const string Vertex = "vertex";

    private enum Format
    {
        Text,
        LittleEndian,
        BigEndian,
    }

    public static PointFileData Read(Stream stream)
    {
        var (format, shape) = ReadHeader(stream);
        var remaining = stream.Length - stream.Position;
        if (format != Format.Text && shape.SkippedBytes + (shape.Count * shape.Record) > remaining)
        {
            throw new PointFileException($"The file is cut off: {shape.Count} points need {shape.Count * shape.Record} bytes, but {remaining} are left.");
        }

        if (shape.Count > remaining && format == Format.Text)
        {
            throw new PointFileException($"The file is cut off: it cannot hold {shape.Count} points as text.");
        }

        var data = PointFile.Allocate(shape.Count, shape.Red is not null);
        if (format == Format.Text)
        {
            ReadText(stream, shape, data);
        }
        else
        {
            ReadBinary(stream, shape, data, format == Format.LittleEndian);
        }

        return data;
    }

    private static (Format Format, Shape Shape) ReadHeader(Stream stream)
    {
        if (ReadLine(stream) != "ply")
        {
            throw new PointFileException("This is no PLY file: the first line must be 'ply'.");
        }

        Format? format = null;
        var builder = new ShapeBuilder();
        while (true)
        {
            var line = ReadLine(stream);
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line == "end_header")
            {
                return (format ?? throw new PointFileException("The header of the PLY file names no format."), builder.Finish());
            }

            if (words.Length == 0 || words[0] is "comment" or "obj_info")
            {
                continue;
            }

            switch (words[0])
            {
                case "format" when words.Length >= 2:
                    format = ParseFormat(words[1]);
                    break;
                case "element" when words.Length >= 3:
                    builder.StartElement(words[1], words[2]);
                    break;
                case "property" when words.Length >= 3:
                    builder.AddProperty(words);
                    break;
                default:
                    throw new PointFileException($"The PLY header holds an unknown line: '{line}'.");
            }
        }
    }

    private static Format ParseFormat(string name) => name switch
    {
        "ascii" => Format.Text,
        "binary_little_endian" => Format.LittleEndian,
        "binary_big_endian" => Format.BigEndian,
        _ => throw new PointFileException($"The PLY format '{name}' is unknown."),
    };

    private static string ReadLine(Stream stream)
    {
        var line = new StringBuilder();
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0)
            {
                throw new PointFileException("The header of the PLY file ends early.");
            }

            if (value == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            if (stream.Position > LongestHeader)
            {
                throw new PointFileException("The header of the PLY file does not end.");
            }

            line.Append((char)value);
        }
    }

    private static void ReadText(Stream stream, Shape shape, PointFileData data)
    {
        var values = new double[shape.Properties.Length];
        var at = 0L;
        TextLines.ForEach(stream, (line, number) =>
        {
            if (at < shape.Skipped)
            {
                at++;
            }
            else if (at < shape.Skipped + shape.Count && line.Length > 0)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (!TextLines.TryTake(ref line, out values[i]))
                    {
                        throw new PointFileException($"Point {at - shape.Skipped + 1} of the PLY file needs {values.Length} numbers.");
                    }
                }

                Store(shape, values, data, (int)(at++ - shape.Skipped));
            }
        });
        if (at < shape.Skipped + shape.Count)
        {
            throw new PointFileException($"The file is cut off: it holds {at - shape.Skipped} of {shape.Count} points.");
        }
    }

    private static void ReadBinary(Stream stream, Shape shape, PointFileData data, bool isLittle)
    {
        stream.Position += shape.SkippedBytes;
        var buffer = new byte[RecordsPerRead * shape.Record];
        var values = new double[shape.Properties.Length];
        for (var done = 0; done < shape.Count;)
        {
            var count = (int)Math.Min(RecordsPerRead, shape.Count - done);
            stream.ReadExactly(buffer, 0, count * shape.Record);
            for (var record = 0; record < count; record++)
            {
                var place = buffer.AsSpan(record * shape.Record);
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = ReadValue(place[shape.Properties[i].Offset..], shape.Properties[i].Type, isLittle);
                }

                Store(shape, values, data, done + record);
            }

            done += count;
        }
    }

    private static void Store(Shape shape, double[] values, PointFileData data, int at)
    {
        (data.X[at], data.Y[at], data.Z[at]) = (values[shape.X], values[shape.Y], values[shape.Z]);
        if (data.Colors is { } colors)
        {
            colors[at] = new Rgba8(ToByte(values[shape.Red!.Value], shape), ToByte(values[shape.Green!.Value], shape), ToByte(values[shape.Blue!.Value], shape), 255);
        }
    }

    private static byte ToByte(double value, Shape shape) => (byte)Math.Clamp(shape.ColorsAreShares ? value * 255 : value, 0, 255);

    private static double ReadValue(ReadOnlySpan<byte> place, PlyType type, bool isLittle) => (type, isLittle) switch
    {
        (PlyType.Int8, _) => (sbyte)place[0],
        (PlyType.UInt8, _) => place[0],
        (PlyType.Int16, true) => BinaryPrimitives.ReadInt16LittleEndian(place),
        (PlyType.Int16, false) => BinaryPrimitives.ReadInt16BigEndian(place),
        (PlyType.UInt16, true) => BinaryPrimitives.ReadUInt16LittleEndian(place),
        (PlyType.UInt16, false) => BinaryPrimitives.ReadUInt16BigEndian(place),
        (PlyType.Int32, true) => BinaryPrimitives.ReadInt32LittleEndian(place),
        (PlyType.Int32, false) => BinaryPrimitives.ReadInt32BigEndian(place),
        (PlyType.UInt32, true) => BinaryPrimitives.ReadUInt32LittleEndian(place),
        (PlyType.UInt32, false) => BinaryPrimitives.ReadUInt32BigEndian(place),
        (PlyType.Single, true) => BinaryPrimitives.ReadSingleLittleEndian(place),
        (PlyType.Single, false) => BinaryPrimitives.ReadSingleBigEndian(place),
        (_, true) => BinaryPrimitives.ReadDoubleLittleEndian(place),
        (_, false) => BinaryPrimitives.ReadDoubleBigEndian(place),
    };

    private readonly record struct Property(string Name, PlyType Type, int Offset);

    private sealed record Shape(long Count, int Record, Property[] Properties, int X, int Y, int Z, int? Red, int? Green, int? Blue, bool ColorsAreShares, long Skipped, long SkippedBytes);

    private enum PlyType
    {
        Int8 = 1,
        UInt8 = 1 + 16,
        Int16 = 2,
        UInt16 = 2 + 16,
        Int32 = 4,
        UInt32 = 4 + 16,
        Single = 4 + 32,
        Double = 8,
    }

    /// <summary>Follows the elements of the header and keeps the one with the points.</summary>
    private sealed class ShapeBuilder
    {
        private readonly List<Property> _properties = [];
        private string? _element;
        private long _elementCount;
        private long _vertices = -1;
        private int _offset;
        private bool _isFixedSize = true;
        private long _skipped;
        private long _skippedBytes;
        private bool _vertexDone;

        public void StartElement(string name, string count)
        {
            CloseElement();
            if (!long.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out _elementCount))
            {
                throw new PointFileException($"The element '{name}' has no valid number: '{count}'.");
            }

            // Also keeps the sizes below from running over.
            if (_elementCount > Array.MaxLength)
            {
                throw new PointFileException($"The file is cut off or damaged: the element '{name}' claims {count} entries.");
            }

            _element = name;
            _vertices = name == Vertex ? _elementCount : _vertices;
        }

        public void AddProperty(string[] words)
        {
            if (words[1] == "list")
            {
                if (_element == Vertex)
                {
                    throw new PointFileException("Vertices with lists are not supported.");
                }

                _isFixedSize = false;
                return;
            }

            var type = ParseType(words[1]);
            if (_element == Vertex)
            {
                _properties.Add(new Property(words[2], type, _offset));
            }

            _offset += Size(type);
        }

        public Shape Finish()
        {
            CloseElement();
            if (_vertices < 0)
            {
                throw new PointFileException("The PLY file has no element 'vertex'.");
            }

            var (x, y, z) = (Find("x"), Find("y"), Find("z"));
            if (x is null || y is null || z is null)
            {
                throw new PointFileException("The vertices of the PLY file need the properties x, y and z.");
            }

            var (red, green, blue) = (Find("red") ?? Find("diffuse_red"), Find("green") ?? Find("diffuse_green"), Find("blue") ?? Find("diffuse_blue"));
            if (red is null || green is null || blue is null)
            {
                (red, green, blue) = (null, null, null);
            }

            var areShares = red is { } index && _properties[index].Type is PlyType.Single or PlyType.Double;
            return new Shape(_vertices, _vertexRecord, [.. _properties], x.Value, y.Value, z.Value, red, green, blue, areShares, _skipped, _skippedBytes);
        }

        private int _vertexRecord;

        private static PlyType ParseType(string name) => name switch
        {
            "char" or "int8" => PlyType.Int8,
            "uchar" or "uint8" => PlyType.UInt8,
            "short" or "int16" => PlyType.Int16,
            "ushort" or "uint16" => PlyType.UInt16,
            "int" or "int32" => PlyType.Int32,
            "uint" or "uint32" => PlyType.UInt32,
            "float" or "float32" => PlyType.Single,
            "double" or "float64" => PlyType.Double,
            _ => throw new PointFileException($"The PLY type '{name}' is unknown."),
        };

        private static int Size(PlyType type) => (int)type & 15;

        private int? Find(string name)
        {
            var index = _properties.FindIndex(property => property.Name == name);
            return index >= 0 ? index : null;
        }

        private void CloseElement()
        {
            if (_element == Vertex)
            {
                _vertexRecord = _offset;
                _vertexDone = true;
            }
            else if (_element is not null && !_vertexDone)
            {
                if (!_isFixedSize)
                {
                    throw new PointFileException($"The element '{_element}' with lists comes before the vertices, which is not supported.");
                }

                _skipped += _elementCount;
                _skippedBytes += _elementCount * _offset;
            }

            (_offset, _isFixedSize) = (0, true);
        }
    }
}
