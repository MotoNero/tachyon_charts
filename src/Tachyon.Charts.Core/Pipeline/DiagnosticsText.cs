using System.Globalization;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// Collects the lines of the diagnostics overlay. It keeps its memory from one update to the next, so writing lines
/// costs no allocation once the text has reached its size.
/// </summary>
public sealed class DiagnosticsText
{
    private const int MaximumLines = 32;

    private readonly int[] _ends = new int[MaximumLines];
    private char[] _chars = new char[512];
    private int _length;

    /// <summary>Gets the number of lines.</summary>
    public int Count { get; private set; }

    /// <summary>Gets a line.</summary>
    /// <param name="index">The number of the line, from 0.</param>
    /// <returns>The characters of the line.</returns>
    public ReadOnlySpan<char> this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var start = index == 0 ? 0 : _ends[index - 1];
            return _chars.AsSpan(start, _ends[index] - start);
        }
    }

    /// <summary>Adds a line. Beyond 32 lines nothing happens.</summary>
    /// <param name="text">The text.</param>
    public void Add(ReadOnlySpan<char> text)
    {
        if (Count >= MaximumLines)
        {
            return;
        }

        Append(text);
        _ends[Count++] = _length;
    }

    /// <summary>Adds a line with a number, such as "GPU 3.21 ms".</summary>
    /// <param name="label">The text before the number.</param>
    /// <param name="value">The number.</param>
    /// <param name="format">The format of the number, as for <see cref="double.ToString(string)"/>.</param>
    /// <param name="unit">The text after the number.</param>
    public void Add(ReadOnlySpan<char> label, double value, ReadOnlySpan<char> format, ReadOnlySpan<char> unit)
    {
        if (Count >= MaximumLines)
        {
            return;
        }

        Append(label);
        Append(" ");
        AppendNumber(value, format);
        if (!unit.IsEmpty)
        {
            Append(" ");
            Append(unit);
        }

        _ends[Count++] = _length;
    }

    /// <summary>Adds a line with an amount of memory in B, KB, MB or GB.</summary>
    /// <param name="label">The text before the amount.</param>
    /// <param name="bytes">The amount in bytes.</param>
    public void AddBytes(ReadOnlySpan<char> label, long bytes)
    {
        var (value, unit) = bytes switch
        {
            >= 1L << 30 => (bytes / (double)(1L << 30), "GB"),
            >= 1L << 20 => (bytes / (double)(1L << 20), "MB"),
            >= 1L << 10 => (bytes / (double)(1L << 10), "KB"),
            _ => (bytes, "B"),
        };
        Add(label, value, unit == "B" ? "F0" : "F1", unit);
    }

    /// <summary>Removes all lines.</summary>
    internal void Clear() => (Count, _length) = (0, 0);

    private void AppendNumber(double value, ReadOnlySpan<char> format)
    {
        int written;
        while (!value.TryFormat(_chars.AsSpan(_length), out written, format, CultureInfo.InvariantCulture))
        {
            Array.Resize(ref _chars, _chars.Length * 2);
        }

        _length += written;
    }

    private void Append(ReadOnlySpan<char> text)
    {
        if (_length + text.Length > _chars.Length)
        {
            Array.Resize(ref _chars, Math.Max(_chars.Length * 2, _length + text.Length));
        }

        text.CopyTo(_chars.AsSpan(_length));
        _length += text.Length;
    }
}
