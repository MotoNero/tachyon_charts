using System.Globalization;
using System.Text;

namespace Tachyon.Charts.Core.Data;

/// <summary>What to take from a CSV file for a <see cref="ColumnFile"/>.</summary>
/// <param name="Format">The delimiter, header and decimal separator.</param>
/// <param name="XColumn">The column of X, which must step evenly; <see langword="null"/> to number the rows.</param>
/// <param name="Columns">The columns to keep, by number from 0; <see langword="null"/> for all but the X column.</param>
/// <param name="XStart">The X of the first row when the rows are numbered.</param>
/// <param name="XStep">The X step when the rows are numbered.</param>
public sealed record CsvColumns(CsvFormat? Format = null, int? XColumn = null, IReadOnlyList<int>? Columns = null, double XStart = 0, double XStep = 1);

/// <summary>What a conversion wrote.</summary>
/// <param name="Rows">The number of rows.</param>
/// <param name="Names">The names of the columns kept, from the header or numbered.</param>
/// <param name="XStart">The X of the first row.</param>
/// <param name="XStep">The X step.</param>
public sealed record ColumnFileSummary(long Rows, IReadOnlyList<string> Names, double XStart, double XStep);

/// <summary>
/// Turns data from other formats into a <see cref="ColumnFile"/> once, so that it opens at once from then on: CSV files
/// of any size are read in pieces and parsed on all cores, and never held in memory as a whole.
/// </summary>
public static class ColumnFileConverter
{
    // A row of X may differ from an even step by this share of the step, for the rounding of printed numbers.
    private const double StepTolerance = 1e-6;
    private const int PieceBytes = 8 << 20;

    /// <summary>Converts a CSV file of numbers, in UTF-8 or ASCII, one row per line.</summary>
    /// <param name="csvPath">The CSV file.</param>
    /// <param name="columnPath">The column file to write; one that is there is replaced.</param>
    /// <param name="columns">What to take; <see langword="null"/> for all columns, rows numbered from 0.</param>
    /// <param name="progress">Receives the share of the file read, from 0 to 1.</param>
    /// <param name="cancellation">Stops the conversion; the column file is then incomplete.</param>
    /// <returns>What was written.</returns>
    /// <exception cref="InvalidDataException">The X column does not step evenly, or a column to keep is missing.</exception>
    public static ColumnFileSummary FromCsv(string csvPath, string columnPath, CsvColumns? columns = null, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(csvPath);
        ArgumentException.ThrowIfNullOrEmpty(columnPath);
        columns ??= new CsvColumns();
        var format = columns.Format ?? new CsvFormat();
        var numbers = new NumberFormatInfo { NumberDecimalSeparator = format.DecimalComma ? "," : ".", NumberGroupSeparator = format.DecimalComma ? "." : "," };

        using var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var length = Math.Max(1, stream.Length);
        var pieces = new Piece[Math.Max(2, Environment.ProcessorCount * 2)];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = new Piece();
        }

        // The header and the first row tell the columns; both are read from the start of the file.
        var carry = new List<byte>();
        var (header, columnCount, consumed) = Start(stream, format, carry);
        var kept = KeptColumns(columns, columnCount, csvPath);
        var layout = new Layout((byte)format.Delimiter, SlotsOf(columns, kept, columnCount), kept.Length, columns.XColumn is not null, numbers, format.Delimiter);
        using var writer = new ColumnFileWriter<double>(columnPath, kept.Length, columns.XStart, columns.XStep, ColumnFileWriter<double>.DefaultBlockRows);
        var state = new XState(csvPath);
        long rows = 0;
        var lineBase = consumed;
        var done = false;
        while (!done)
        {
            // Read pieces one after the other, each ending at a line end; parse them on all cores; write them in order.
            var filled = FillPieces(pieces, stream, carry);
            done = filled < pieces.Length;
            cancellation.ThrowIfCancellationRequested();
            Parallel.For(0, filled, new ParallelOptions { CancellationToken = cancellation }, index => pieces[index].ParseValues(layout));
            for (var index = 0; index < filled; index++)
            {
                var piece = pieces[index];
                if (layout.HasX)
                {
                    state.Check(piece, lineBase);
                }

                writer.Append(piece.Values.AsSpan(0, piece.Rows * kept.Length));
                rows += piece.Rows;
                lineBase += piece.Lines;
            }

            progress?.Report(Math.Min(1, (double)stream.Position / length));
        }

        var (xStart, xStep) = layout.HasX && rows >= 2 ? (state.First, (state.Last - state.First) / (rows - 1)) : (columns.XStart, columns.XStep);
        writer.SetX(xStart, xStep);
        progress?.Report(1);
        var names = kept.Select(c => NameOf(header, c)).ToArray();
        return new ColumnFileSummary(rows, names, xStart, xStep);
    }

    private static string NameOf(string[]? header, int column) =>
        header is not null && column < header.Length && header[column].Trim().Length > 0 ? header[column].Trim() : "Column " + (column + 1).ToString(CultureInfo.InvariantCulture);

    // The columns kept: those asked for, or all beside X; each must exist.
    private static int[] KeptColumns(CsvColumns columns, int columnCount, string csvPath)
    {
        var kept = (columns.Columns ?? Enumerable.Range(0, columnCount).Where(c => c != columns.XColumn).ToArray()).ToArray();
        if (kept.Length == 0)
        {
            throw new InvalidDataException($"{csvPath} has no column to keep beside X.");
        }

        var missing = kept.Concat(columns.XColumn is { } xc ? [xc] : []).FirstOrDefault(c => c < 0 || c >= columnCount, -1);
        if (missing >= 0)
        {
            throw new InvalidDataException($"{csvPath} has {columnCount} columns; there is no column {missing}.");
        }

        return kept;
    }

    // What each field of a row is: the place of a kept column, X (-2), or nothing (-1).
    private static int[] SlotsOf(CsvColumns columns, int[] kept, int columnCount)
    {
        var slots = Enumerable.Repeat(-1, columnCount).ToArray();
        for (var k = 0; k < kept.Length; k++)
        {
            slots[kept[k]] = k;
        }

        if (columns.XColumn is { } xColumn)
        {
            slots[xColumn] = -2;
        }

        return slots;
    }

    // Fills the pieces in order; fewer than all at the end of the file.
    private static int FillPieces(Piece[] pieces, FileStream stream, List<byte> carry)
    {
        for (var filled = 0; filled < pieces.Length; filled++)
        {
            if (!pieces[filled].Fill(stream, carry))
            {
                return filled;
            }
        }

        return pieces.Length;
    }

    // Reads the header, if there is one, and counts the fields of the first row; the bytes read stay in the carry.
    // Reads on into the carry, dropping a byte order mark at the start; false at the end of the file.
    private static bool ReadMore(FileStream stream, byte[] buffer, List<byte> carry, ref bool checkedMark)
    {
        var read = stream.Read(buffer);
        if (read == 0)
        {
            return false;
        }

        carry.AddRange(buffer.AsSpan(0, read));
        if (!checkedMark && carry.Count >= 3)
        {
            checkedMark = true;
            if (carry[0] == 0xEF && carry[1] == 0xBB && carry[2] == 0xBF)
            {
                carry.RemoveRange(0, 3);
            }
        }

        return true;
    }

    // No line end: the rest is the last line, or there are no rows at all.
    private static (string[]? Header, int Columns, long LinesConsumed) LastLine(List<byte> carry, string[]? header, CsvFormat format, long lines)
    {
        var rest = Encoding.UTF8.GetString([.. carry]).TrimEnd('\r');
        if (rest.Length == 0)
        {
            return (header, header?.Length ?? 1, lines);
        }

        if (header is null && format.HasHeader)
        {
            var only = Split(rest, format.Delimiter);
            carry.Clear();
            return (only, only.Length, lines + 1);
        }

        return (header, Math.Max(header?.Length ?? 0, Split(rest, format.Delimiter).Length), lines);
    }

    private static (string[]? Header, int Columns, long LinesConsumed) Start(FileStream stream, CsvFormat format, List<byte> carry)
    {
        var buffer = new byte[1 << 16];
        string[]? header = null;
        long lines = 0;
        var checkedMark = false;
        while (true)
        {
            var newline = carry.IndexOf((byte)'\n');
            if (newline < 0)
            {
                if (!ReadMore(stream, buffer, carry, ref checkedMark))
                {
                    return LastLine(carry, header, format, lines);
                }

                continue;
            }

            var line = Encoding.UTF8.GetString([.. carry.GetRange(0, newline)]).TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                carry.RemoveRange(0, newline + 1);
                lines++;
                continue;
            }

            if (header is null && format.HasHeader)
            {
                header = Split(line, format.Delimiter);
                carry.RemoveRange(0, newline + 1);
                lines++;
                continue;
            }

            // The first row stays in the carry; it is parsed with the others.
            return (header, Math.Max(header?.Length ?? 0, Split(line, format.Delimiter).Length), lines);
        }
    }

    // Splits a line at the delimiter; a value in quotes keeps delimiters, and two quotes in it stand for one.
    private static string[] Split(string line, char delimiter)
    {
        if (!line.Contains('"', StringComparison.Ordinal))
        {
            return line.Split(delimiter);
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i++];
            if (quoted && c == '"' && i < line.Length && line[i] == '"')
            {
                field.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == delimiter && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return [.. fields];
    }

    // How a row turns into values: per field the place among the kept columns, -2 for X, -1 for nothing.
    private sealed record Layout(byte Delimiter, int[] Slots, int Kept, bool HasX, NumberFormatInfo Numbers, char DelimiterChar);

    // The X of the rows so far, to check that every row follows the step of the first two.
    private sealed class XState(string path)
    {
        private long _rows;
        private double _second;

        public double First { get; private set; }

        public double Last { get; private set; }

        public void Check(Piece piece, long lineBase)
        {
            for (var i = 0; i < piece.Rows; i++)
            {
                var x = piece.Xs[i];
                if (_rows == 0)
                {
                    First = x;
                }
                else if (_rows == 1)
                {
                    _second = x;
                }
                else if (Math.Abs(x - Last - (_second - First)) > Math.Abs(_second - First) * StepTolerance)
                {
                    throw new InvalidDataException($"Line {lineBase + piece.LineOfRow[i] + 1} of {path}: X {x.ToString(CultureInfo.InvariantCulture)} does not follow the step {(_second - First).ToString(CultureInfo.InvariantCulture)}; column files need X in even steps.");
                }

                Last = x;
                _rows++;
            }
        }
    }

    // A piece of the file that ends at a line end, with its rows once parsed.
    private sealed class Piece
    {
        private static double Parse(ReadOnlySpan<byte> field, NumberFormatInfo numbers) =>
            double.TryParse(field.Trim(" \t"u8), NumberStyles.Float | NumberStyles.AllowThousands, numbers, out var value) ? value : double.NaN;

        private static double Parse(string field, NumberFormatInfo numbers) =>
            double.TryParse(field.AsSpan().Trim(), NumberStyles.Float | NumberStyles.AllowThousands, numbers, out var value) ? value : double.NaN;

        private byte[] _bytes = new byte[PieceBytes];
        private int _length;

        public int Rows { get; private set; }

        public int Lines { get; private set; }

        public double[] Values { get; private set; } = [];

        public double[] Xs { get; private set; } = [];

        public int[] LineOfRow { get; private set; } = [];

        // Takes the carry and reads on up to the last line end; what follows it is the carry of the next piece.
        public bool Fill(FileStream stream, List<byte> carry)
        {
            if (carry.Count > _bytes.Length)
            {
                _bytes = new byte[carry.Count * 2];
            }

            carry.CopyTo(_bytes);
            _length = carry.Count;
            carry.Clear();
            while (true)
            {
                var read = stream.Read(_bytes, _length, _bytes.Length - _length);
                _length += read;
                if (read == 0)
                {
                    return _length > 0;
                }

                var end = _bytes.AsSpan(0, _length).LastIndexOf((byte)'\n');
                if (end >= 0)
                {
                    carry.AddRange(_bytes.AsSpan(end + 1, _length - end - 1));
                    _length = end + 1;
                    return true;
                }

                // A line longer than the piece: the piece grows.
                if (_length == _bytes.Length)
                {
                    Array.Resize(ref _bytes, _bytes.Length * 2);
                }
            }
        }

        public void ParseValues(Layout layout)
        {
            var text = _bytes.AsSpan(0, _length);
            var estimate = Math.Max(16, text.Count((byte)'\n') + 1);
            if (Xs.Length < estimate)
            {
                Values = new double[estimate * layout.Kept];
                Xs = new double[estimate];
                LineOfRow = new int[estimate];
            }

            (Rows, Lines) = (0, 0);
            while (!text.IsEmpty)
            {
                var newline = text.IndexOf((byte)'\n');
                var line = newline < 0 ? text : text[..newline];
                text = newline < 0 ? [] : text[(newline + 1)..];
                Lines++;
                if (line.Length > 0 && line[^1] == '\r')
                {
                    line = line[..^1];
                }

                if (line.Trim(" \t"u8).IsEmpty)
                {
                    continue;
                }

                var row = Values.AsSpan(Rows * layout.Kept, layout.Kept);
                row.Fill(double.NaN);
                Xs[Rows] = double.NaN;
                if (line.Contains((byte)'"'))
                {
                    ParseQuoted(line, layout, row);
                }
                else
                {
                    ParsePlain(line, layout, row);
                }

                LineOfRow[Rows] = Lines - 1;
                Rows++;
            }
        }

        // Quotes are rare in numbers: such a line goes the careful way.
        private void ParseQuoted(ReadOnlySpan<byte> line, Layout layout, Span<double> row)
        {
            var fields = Split(Encoding.UTF8.GetString(line), layout.DelimiterChar);
            for (var field = 0; field < fields.Length && field < layout.Slots.Length; field++)
            {
                Store(layout, field, Parse(fields[field], layout.Numbers), row);
            }
        }

        // The fields straight from the bytes, the ones not kept skipped unread.
        private void ParsePlain(ReadOnlySpan<byte> line, Layout layout, Span<double> row)
        {
            for (var field = 0; field < layout.Slots.Length; field++)
            {
                var delimiter = line.IndexOf(layout.Delimiter);
                var value = delimiter < 0 ? line : line[..delimiter];
                if (layout.Slots[field] != -1)
                {
                    Store(layout, field, Parse(value, layout.Numbers), row);
                }

                if (delimiter < 0)
                {
                    return;
                }

                line = line[(delimiter + 1)..];
            }
        }

        private void Store(Layout layout, int field, double value, Span<double> row)
        {
            var slot = layout.Slots[field];
            if (slot >= 0)
            {
                row[slot] = value;
            }
            else if (slot == -2)
            {
                Xs[Rows] = value;
            }
        }
    }
}
