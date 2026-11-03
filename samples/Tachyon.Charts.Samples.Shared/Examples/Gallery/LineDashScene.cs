using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Dash patterns, along a curve and on a line that leaves the plot area.
/// </summary>
public sealed class LineDashScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineDash";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var dashes = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y)
        {
            Color = Blue,
            Thickness = 2,
            DashPattern = [10, 6],
        };
        var dots = new LineSeries<double, double>(CreateData(value => 0.5 * Math.Cos(value * 2)), x, y)
        {
            Color = Orange,
            Thickness = 3,
            DashPattern = [1, 7],
        };
        var mixed = new LineSeries<double, double>(CreateData(value => (value * 0.6) - 3.5), x, y)
        {
            Color = Green,
            Thickness = 2,
            DashPattern = [16, 4, 4, 4],
        };
        chart.AddSeries(dashes);
        chart.AddSeries(dots);
        chart.AddSeries(mixed);
        return [dashes, dots, mixed];
    }
}
