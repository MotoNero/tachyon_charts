using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>The instrument page „Parallel (interaktiv)“ of the showroom.</summary>
public static class ParallelPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Parallel (interaktiv)";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag a name: move the axis | Drag along an axis: filter | Double click: all rows";

    /// <summary>Builds the chart.</summary>
    /// <param name="builder">The chart.</param>
    public static void Configure(ChartBuilder builder)
    {
        const int Rows = 20_000;
        var random = new Random(1);
        var columns = new double[6][];
        for (var column = 0; column < columns.Length; column++)
        {
            columns[column] = new double[Rows];
        }

        for (var row = 0; row < Rows; row++)
        {
            var load = random.NextDouble();
            columns[0][row] = 3 + (2 * load) + (random.NextDouble() * 0.3);
            columns[1][row] = 10 * load * load;
            columns[2][row] = 25 + (40 * load) + (random.NextDouble() * 5);
            columns[3][row] = 1000 + (random.NextDouble() * 50);
            columns[4][row] = 50 + (random.NextDouble() * 0.2);
            columns[5][row] = -60 + (30 * load);
        }

        builder.ParallelCoordinates(columns, ["V", "A", "°C", "hPa", "Hz", "dB"]).Theme(BuiltInThemes.Instrument);
    }
}
