using System.Globalization;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Receives a line.</summary>
/// <param name="line">The bytes without the line end.</param>
/// <param name="number">The number of the line, from 1.</param>
internal delegate void LineReader(ReadOnlySpan<byte> line, long number);

/// <summary>Reads text as bytes. Parsing bytes directly is what keeps a file of millions of lines free of strings.</summary>
internal static class TextLines
{
    private const int BufferSize = 1 << 20;
    private const int LongestLine = 1 << 16;

    /// <summary>Calls back for each line of a stream.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="read">The callback.</param>
    /// <param name="firstNumber">The number of the first line.</param>
    public static void ForEach(Stream stream, LineReader read, long firstNumber = 1)
    {
        var buffer = new byte[BufferSize];
        var (filled, number) = (0, firstNumber);
        while (true)
        {
            var count = stream.Read(buffer, filled, buffer.Length - filled);
            filled += count;
            var rest = buffer.AsSpan(0, filled);
            while (rest.IndexOf((byte)'\n') is var end and >= 0)
            {
                read(Trim(rest[..end]), number++);
                rest = rest[(end + 1)..];
            }

            if (count == 0)
            {
                if (rest.Length > 0)
                {
                    read(Trim(rest), number);
                }

                return;
            }

            if (rest.Length > LongestLine)
            {
                throw new PointFileException($"Line {number} is too long. This does not look like a text file.");
            }

            rest.CopyTo(buffer);
            filled = rest.Length;
        }
    }

    /// <summary>Gets a value indicating whether a line holds data.</summary>
    /// <param name="line">The line.</param>
    /// <returns>Whether it does.</returns>
    public static bool HasData(ReadOnlySpan<byte> line) => line.Length > 0 && line[0] != (byte)'#' && line[0] != (byte)'/';

    /// <summary>Takes the next number of a line.</summary>
    /// <param name="line">The rest of the line, which moves on.</param>
    /// <param name="value">The number.</param>
    /// <returns>Whether the line held another word that is a number.</returns>
    public static bool TryTake(ref ReadOnlySpan<byte> line, out double value)
    {
        value = 0;
        var start = 0;
        while (start < line.Length && IsSeparator(line[start]))
        {
            start++;
        }

        if (start == line.Length)
        {
            line = default;
            return false;
        }

        var end = start;
        while (end < line.Length && !IsSeparator(line[end]))
        {
            end++;
        }

        var isNumber = double.TryParse(line[start..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        line = line[end..];
        return isNumber;
    }

    private static bool IsSeparator(byte value) => value is (byte)' ' or (byte)'\t' or (byte)',' or (byte)';';

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> line)
    {
        line = line.TrimEnd((byte)'\r');
        return line.TrimStart(" \t"u8).TrimEnd(" \t"u8);
    }
}
