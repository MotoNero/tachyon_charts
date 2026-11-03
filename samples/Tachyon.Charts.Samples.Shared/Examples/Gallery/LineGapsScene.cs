using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// NaN and ±∞ as gaps, with a single point between two gaps.
/// </summary>
public sealed class LineGapsScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineGaps";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        static double Holes(double value) => value switch
        {
            > 2 and < 3 => double.NaN,
            > 4.5 and < 5.0 => double.PositiveInfinity,
            > 5.1 and < 5.6 => double.NegativeInfinity,
            > 7 and < 7.2 => double.NaN,
            _ => 0.9 * Math.Sin(value * 1.3),
        };

        var line = new LineSeries<double, double>(CreateData(Holes), x, y) { Color = Blue, Thickness = 2 };
        var step = new LineSeries<double, double>(CreateData(value => Holes(value) * -0.8, 60), x, y) { Color = Orange, Thickness = 2, IsStep = true };
        chart.AddSeries(line);
        chart.AddSeries(step);
        return [line, step];
    }
}
