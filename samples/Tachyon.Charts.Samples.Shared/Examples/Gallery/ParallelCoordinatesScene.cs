using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Two thousand rows over six measures, with one correlated pair.</summary>
public sealed class ParallelCoordinatesScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "ParallelCoordinates";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 1), new DoubleRange(0, 1), out var x, out var y);
        x.IsHidden = true;
        y.IsHidden = true;
        const int Rows = 2000;
        var columns = new double[6][];
        for (var column = 0; column < columns.Length; column++)
        {
            columns[column] = new double[Rows];
            for (var row = 0; row < Rows; row++)
            {
                var noise = ((row * 2654435761L * (column + 3)) % 1000) / 1000.0;
                columns[column][row] = column == 1 ? 100 - (columns[0][row] * 0.8) - (noise * 20) : noise * 100 * (column + 1);
            }
        }

        var rows = new ParallelCoordinatesSeries(columns, x, y) { Color = new Rgba8(80, 170, 240, 50) };
        chart.AddSeries(rows);
        chart.AddOverlay(new ParallelAxesOverlay(rows, ["Volt", "Strom", "Temp", "Druck", "Hz", "dB"]));
        disposables = [rows];
        return chart;
    }
}
