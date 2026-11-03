namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// A series that writes its data as a table of its own when the chart is exported as CSV, for data that does not fit
/// the columns series, x and y, such as points in space.
/// </summary>
public interface ICsvExport
{
    /// <summary>Writes the table: a header line, then one line per point, numbers with a dot.</summary>
    /// <param name="writer">Receives the text.</param>
    void WriteCsv(TextWriter writer);
}
