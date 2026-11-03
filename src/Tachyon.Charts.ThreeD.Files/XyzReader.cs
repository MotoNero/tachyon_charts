using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Reads text with one point per line. Two passes: the first one only counts, so that the arrays fit exactly.</summary>
internal static class XyzReader
{
    private const int ColorWords = 6;

    public static PointFileData Read(Stream stream)
    {
        var (count, hasColors) = Count(stream);
        var data = PointFile.Allocate(count, hasColors);
        stream.Position = 0;
        var at = 0;
        TextLines.ForEach(stream, (line, number) =>
        {
            if (TextLines.HasData(line))
            {
                ReadLine(line, number, data, at++);
            }
        });
        return data;
    }

    private static (long Count, bool HasColors) Count(Stream stream)
    {
        var (count, hasColors) = (0L, false);
        TextLines.ForEach(stream, (line, _) =>
        {
            if (TextLines.HasData(line))
            {
                hasColors |= count == 0 && CountWords(line) >= ColorWords;
                count++;
            }
        });
        return (count, hasColors);
    }

    private static int CountWords(ReadOnlySpan<byte> line)
    {
        var words = 0;
        var isNumber = TextLines.TryTake(ref line, out _);
        while (isNumber)
        {
            words++;
            isNumber = TextLines.TryTake(ref line, out _);
        }

        return words;
    }

    private static void ReadLine(ReadOnlySpan<byte> line, long number, PointFileData data, int at)
    {
        if (!TextLines.TryTake(ref line, out var x) || !TextLines.TryTake(ref line, out var y) || !TextLines.TryTake(ref line, out var z))
        {
            throw new PointFileException($"Line {number} needs three numbers: x, y and z.");
        }

        (data.X[at], data.Y[at], data.Z[at]) = (x, y, z);
        if (data.Colors is { } colors)
        {
            if (!TextLines.TryTake(ref line, out var red) || !TextLines.TryTake(ref line, out var green) || !TextLines.TryTake(ref line, out var blue))
            {
                throw new PointFileException($"Line {number} needs red, green and blue like the first line.");
            }

            colors[at] = new Rgba8(ToByte(red), ToByte(green), ToByte(blue), 255);
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(value, 0, 255);
}
