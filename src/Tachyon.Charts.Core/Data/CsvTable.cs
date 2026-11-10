using System.Globalization;
using System.Text;

namespace Tachyon.Charts.Core.Data;

/// <summary>How a CSV file is written.</summary>
/// <param name="Delimiter">The character between the values, such as a comma, a semicolon or a tab.</param>
/// <param name="HasHeader">Whether the first line holds the names of the columns.</param>
/// <param name="DecimalComma">Whether numbers use a comma before their decimals, as in 3,14, instead of a point.</param>
public sealed record CsvFormat(char Delimiter = ',', bool HasHeader = true, bool DecimalComma = false);

/// <summary>
/// The columns of a CSV file as numbers, to show them in a chart. Values in quotes may hold the delimiter; a value
/// that is no number, and one missing at the end of a short line, reads as NaN, which a chart shows as a gap.
/// </summary>
public sealed class CsvTable
{
    private readonly double[][] _columns;

    private CsvTable(IReadOnlyList<string> names, double[][] columns) => (Names, _columns) = (names, columns);

    /// <summary>Gets the names of the columns: from the header, otherwise "Column 1", "Column 2" and so on.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Gets the number of rows with values.</summary>
    public int RowCount => _columns.Length == 0 ? 0 : _columns[0].Length;

    /// <summary>Reads a CSV file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="format">How it is written; <see langword="null"/> for commas, a header and decimal points.</param>
    /// <returns>The table.</returns>
    public static CsvTable Read(string path, CsvFormat? format = null)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader, format);
    }

    /// <summary>Reads CSV text.</summary>
    /// <param name="reader">The text.</param>
    /// <param name="format">How it is written; <see langword="null"/> for commas, a header and decimal points.</param>
    /// <returns>The table.</returns>
    public static CsvTable Read(TextReader reader, CsvFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        format ??= new CsvFormat();
        var numbers = new NumberFormatInfo { NumberDecimalSeparator = format.DecimalComma ? "," : ".", NumberGroupSeparator = format.DecimalComma ? "." : "," };
        var rows = new List<string[]>();
        string[]? header = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = Split(line, format.Delimiter);
            if (header is null && format.HasHeader)
            {
                header = fields;
                continue;
            }

            rows.Add(fields);
        }

        var count = Math.Max(header?.Length ?? 0, rows.Count == 0 ? 0 : rows.Max(row => row.Length));
        var columns = new double[count][];
        for (var c = 0; c < count; c++)
        {
            columns[c] = new double[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                columns[c][r] = c < rows[r].Length && double.TryParse(rows[r][c].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, numbers, out var value) ? value : double.NaN;
            }
        }

        var names = new string[count];
        for (var c = 0; c < count; c++)
        {
            names[c] = header is not null && c < header.Length && header[c].Trim().Length > 0 ? header[c].Trim() : "Column " + (c + 1).ToString(CultureInfo.InvariantCulture);
        }

        return new CsvTable(names, columns);
    }

    /// <summary>Gets a column by its number.</summary>
    /// <param name="index">The number, from 0.</param>
    /// <returns>The values, one per row.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such column.</exception>
    public double[] Column(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _columns.Length);
        return _columns[index];
    }

    /// <summary>Gets a column by its name.</summary>
    /// <param name="name">The name, as in <see cref="Names"/>.</param>
    /// <returns>The values, one per row.</returns>
    /// <exception cref="KeyNotFoundException">There is no such column.</exception>
    public double[] Column(string name)
    {
        for (var c = 0; c < Names.Count; c++)
        {
            if (string.Equals(Names[c], name, StringComparison.Ordinal))
            {
                return _columns[c];
            }
        }

        throw new KeyNotFoundException($"The table has no column {name}.");
    }

    // Splits a line at the delimiter; a value in quotes keeps delimiters, and two quotes in it stand for one.
    private static string[] Split(string line, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
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
