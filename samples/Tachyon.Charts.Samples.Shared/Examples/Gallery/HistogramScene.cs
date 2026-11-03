using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A histogram of a skewed sample, with the bins of Freedman and Diaconis.</summary>
public sealed class HistogramScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Histogram";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var random = new Random(3);
        var values = new double[5000];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = -Math.Log(1 - random.NextDouble()) * 2;
        }

        var histogram = Histogram.Compute(values, BinningRule.FreedmanDiaconis);
        var chart = CreateAxes(new DoubleRange(0, 12), new DoubleRange(0, 1200), out var x, out var y);
        chart.AddSeries(BarSeries.FromHistogram(histogram, x, y));
        disposables = [];
        return chart;
    }
}
