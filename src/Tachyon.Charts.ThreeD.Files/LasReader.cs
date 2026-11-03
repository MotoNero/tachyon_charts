using System.Buffers.Binary;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Reads LAS files. The header gives the number and size of the records, so the points read in one pass.</summary>
internal static class LasReader
{
    private const int HeaderBytes = 227;
    private const int LongHeaderBytes = 375;
    private const int RecordsPerRead = 1 << 16;
    private const int LargestFormat = 10;
    private const int ColorSniff = 4096;
    private const byte CompressedBits = 0xC0;

    // The smallest record per format, and where the colors start. Formats without colors hold -1.
    private static readonly int[] _sizes = [20, 28, 26, 34, 57, 63, 30, 36, 38, 59, 67];
    private static readonly int[] _colors = [-1, -1, 20, 28, -1, 28, -1, 30, 30, -1, 30];

    public static PointFileData Read(Stream stream)
    {
        var header = new byte[LongHeaderBytes];
        var length = stream.Length;
        stream.ReadAtLeast(header, Math.Min(LongHeaderBytes, (int)Math.Min(length, LongHeaderBytes)), false);
        if (length < HeaderBytes || !header.AsSpan(0, 4).SequenceEqual("LASF"u8))
        {
            throw new PointFileException("This is no LAS file: it does not start with 'LASF'.");
        }

        var layout = ReadLayout(header, length);
        var data = PointFile.Allocate(layout.Count, layout.ColorAt >= 0);
        stream.Position = layout.Offset;
        ReadRecords(stream, layout, data);
        return data;
    }

    private static Layout ReadLayout(byte[] header, long length)
    {
        var format = header[104];
        if ((format & CompressedBits) != 0)
        {
            throw new PointFileException("The points are compressed like in LAZ, which is not supported.");
        }

        if (format > LargestFormat)
        {
            throw new PointFileException($"The point format {format} is unknown.");
        }

        var (minor, headerSize) = (header[25], BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(94)));
        var offset = (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(96));
        var record = (int)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(105));
        long count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(107));
        if (minor >= 4 && headerSize >= LongHeaderBytes)
        {
            var longCount = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(247));
            count = longCount > (ulong)length ? throw new PointFileException($"The file is cut off or damaged: the header claims {longCount} points.") : (long)longCount;
        }

        if (record < _sizes[format])
        {
            throw new PointFileException($"A record of format {format} needs {_sizes[format]} bytes, the header says {record}.");
        }

        if (offset < HeaderBytes || offset + (count * record) > length)
        {
            throw new PointFileException($"The file is cut off: {count} points from byte {offset} need {offset + (count * record)} bytes, but it has {length}.");
        }

        var (scale, origin) = (ReadVector(header, 131), ReadVector(header, 155));
        if (!IsValid(scale.X) || !IsValid(scale.Y) || !IsValid(scale.Z) || !double.IsFinite(origin.X + origin.Y + origin.Z))
        {
            throw new PointFileException("The scale or offset in the header of the LAS file is invalid.");
        }

        return new Layout(count, record, offset, _colors[format], scale, origin);
    }

    private static bool IsValid(double scale) => double.IsFinite(scale) && scale != 0;

    private static (double X, double Y, double Z) ReadVector(byte[] header, int at) =>
        (BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(at)), BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(at + 8)), BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(at + 16)));

    private static void ReadRecords(Stream stream, in Layout layout, PointFileData data)
    {
        var buffer = new byte[RecordsPerRead * layout.Record];
        var shift = -1;
        for (var done = 0; done < layout.Count;)
        {
            var count = (int)Math.Min(RecordsPerRead, layout.Count - done);
            stream.ReadExactly(buffer, 0, count * layout.Record);
            shift = shift < 0 && layout.ColorAt >= 0 ? FindShift(buffer, layout, count) : shift;
            for (var i = 0; i < count; i++)
            {
                var record = buffer.AsSpan(i * layout.Record, layout.Record);
                data.X[done + i] = (BinaryPrimitives.ReadInt32LittleEndian(record) * layout.Scale.X) + layout.Origin.X;
                data.Y[done + i] = (BinaryPrimitives.ReadInt32LittleEndian(record[4..]) * layout.Scale.Y) + layout.Origin.Y;
                data.Z[done + i] = (BinaryPrimitives.ReadInt32LittleEndian(record[8..]) * layout.Scale.Z) + layout.Origin.Z;
                if (data.Colors is { } colors)
                {
                    colors[done + i] = ReadColor(record[layout.ColorAt..], shift);
                }
            }

            done += count;
        }
    }

    // The format asks for 16 bits per color, yet many files store 8. The first points tell which it is.
    private static int FindShift(byte[] buffer, in Layout layout, int count)
    {
        for (var i = 0; i < Math.Min(count, ColorSniff); i++)
        {
            var color = buffer.AsSpan((i * layout.Record) + layout.ColorAt);
            if (BinaryPrimitives.ReadUInt16LittleEndian(color) > 255 || BinaryPrimitives.ReadUInt16LittleEndian(color[2..]) > 255 || BinaryPrimitives.ReadUInt16LittleEndian(color[4..]) > 255)
            {
                return 8;
            }
        }

        return 0;
    }

    private static Rgba8 ReadColor(ReadOnlySpan<byte> place, int shift) => new(
        (byte)(BinaryPrimitives.ReadUInt16LittleEndian(place) >> shift),
        (byte)(BinaryPrimitives.ReadUInt16LittleEndian(place[2..]) >> shift),
        (byte)(BinaryPrimitives.ReadUInt16LittleEndian(place[4..]) >> shift),
        255);

    private readonly record struct Layout(long Count, int Record, long Offset, int ColorAt, (double X, double Y, double Z) Scale, (double X, double Y, double Z) Origin);
}
