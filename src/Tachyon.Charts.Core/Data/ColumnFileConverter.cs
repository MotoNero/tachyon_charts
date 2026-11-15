using System.Globalization;

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
/// of any size are read as a stream, row by row, and never held in memory.
/// </summary>
public static class ColumnFileConverter
{
    // A row of X may differ from an even step by this share of the step, for the rounding of printed numbers.
    private const double StepTolerance = 1e-6;
    private const int RowsPerWrite = 8_192;

    /// <summary>Converts a CSV file of numbers.</summary>
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
        using var reader = new StreamReader(stream, bufferSize: 1 << 20);
        var length = Math.Max(1, stream.Length);
        string[]? header = null;
        int[]? kept = null;
        ColumnFileWriter<double>? writer = null;
        double[] buffer = [];
        var inBuffer = 0;
        long rows = 0;
        double firstX = 0, secondX = 0, lastX = 0;
        var lineNumber = 0;
        var ranges = new Range[16];
        try
        {
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if (line.Length == 0)
                {
                    continue;
                }

                if (header is null && format.HasHeader && kept is null)
                {
                    header = Split(line, format.Delimiter);
                    continue;
                }

                // Lines without quotes, the usual case, are read in place; others are split.
                var span = line.AsSpan();
                var quoted = span.Contains('"');
                var fields = quoted ? Split(line, format.Delimiter) : [];
                var fieldCount = quoted ? fields.Length : Ranges(span, format.Delimiter, ref ranges);

                if (kept is null)
                {
                    var count = Math.Max(fieldCount, header?.Length ?? 0);
                    kept = [.. (columns.Columns ?? Enumerable.Range(0, count).Where(c => c != columns.XColumn).ToArray())];
                    if (kept.Length == 0)
                    {
                        throw new InvalidDataException($"{csvPath} has no column to keep beside X.");
                    }

                    var missing = kept.Concat(columns.XColumn is { } x ? [x] : []).FirstOrDefault(c => c < 0 || c >= count, -1);
                    if (missing >= 0)
                    {
                        throw new InvalidDataException($"{csvPath} has {count} columns; there is no column {missing}.");
                    }

                    writer = new ColumnFileWriter<double>(columnPath, kept.Length, columns.XStart, columns.XStep, ColumnFileWriter<double>.DefaultBlockRows);
                    buffer = new double[RowsPerWrite * kept.Length];
                }

                for (var c = 0; c < kept.Length; c++)
                {
                    buffer[(inBuffer * kept.Length) + c] = quoted ? Number(fields, kept[c], numbers) : Number(span, ranges, fieldCount, kept[c], numbers);
                }

                if (columns.XColumn is { } xColumn)
                {
                    var x = quoted ? Number(fields, xColumn, numbers) : Number(span, ranges, fieldCount, xColumn, numbers);
                    (firstX, secondX) = rows == 0 ? (x, secondX) : rows == 1 ? (firstX, x) : (firstX, secondX);
                    if (rows >= 2 && Math.Abs(x - lastX - (secondX - firstX)) > Math.Abs(secondX - firstX) * StepTolerance)
                    {
                        throw new InvalidDataException($"Line {lineNumber} of {csvPath}: X {x.ToString(CultureInfo.InvariantCulture)} does not follow the step {(secondX - firstX).ToString(CultureInfo.InvariantCulture)}; column files need X in even steps.");
                    }

                    lastX = x;
                }

                rows++;
                if (++inBuffer == RowsPerWrite)
                {
                    writer!.Append(buffer);
                    inBuffer = 0;
                    cancellation.ThrowIfCancellationRequested();
                    progress?.Report((double)stream.Position / length);
                }
            }

            writer ??= new ColumnFileWriter<double>(columnPath, 1, columns.XStart, columns.XStep);
            writer.Append(buffer.AsSpan(0, inBuffer * (kept?.Length ?? 1)));
            var (xStart, xStep) = columns.XColumn is not null && rows >= 2 ? (firstX, (lastX - firstX) / (rows - 1)) : (columns.XStart, columns.XStep);
            writer.SetX(xStart, xStep);
            writer.Dispose();
            writer = null;
            progress?.Report(1);
            var names = (kept ?? []).Select(c => header is not null && c < header.Length && header[c].Trim().Length > 0 ? header[c].Trim() : "Column " + (c + 1).ToString(CultureInfo.InvariantCulture)).ToArray();
            return new ColumnFileSummary(rows, names, xStart, xStep);
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static double Number(ReadOnlySpan<char> line, Range[] ranges, int count, int index, NumberFormatInfo numbers) =>
        index < count && double.TryParse(line[ranges[index]].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, numbers, out var value) ? value : double.NaN;

    // The places of the fields of a line, growing the list for long lines.
    private static int Ranges(ReadOnlySpan<char> line, char delimiter, ref Range[] ranges)
    {
        var count = 0;
        foreach (var range in line.Split(delimiter))
        {
            if (count == ranges.Length)
            {
                Array.Resize(ref ranges, count * 2);
            }

            ranges[count++] = range;
        }

        return count;
    }

    private static double Number(string[] fields, int index, NumberFormatInfo numbers) =>
        index < fields.Length && double.TryParse(fields[index].AsSpan().Trim(), NumberStyles.Float | NumberStyles.AllowThousands, numbers, out var value) ? value : double.NaN;

    // Splits a line at the delimiter; a value in quotes keeps delimiters, and two quotes in it stand for one.
    private static string[] Split(string line, char delimiter)
    {
        if (!line.Contains('"', StringComparison.Ordinal))
        {
            return line.Split(delimiter);
        }

        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
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
}
