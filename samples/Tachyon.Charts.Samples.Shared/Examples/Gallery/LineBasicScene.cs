using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Solid lines of several widths and an unsorted curve.
/// </summary>
public sealed class LineBasicScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineBasic";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var thin = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 1.5 };
        var thick = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Cos(value * 1.3)), x, y) { Color = Orange, Thickness = 4 };

        var circle = new DataSource<double, double>();
        for (var i = 0; i <= 120; i++)
        {
            var angle = i * Math.Tau / 120;
            circle.Append(5 + (2 * Math.Cos(angle)), Math.Sin(angle));
        }

        var unsorted = new LineSeries<double, double>(circle, x, y) { Color = Green, Thickness = 2 };
        chart.AddSeries(thin);
        chart.AddSeries(thick);
        chart.AddSeries(unsorted);
        return [thin, thick, unsorted];
    }
}
