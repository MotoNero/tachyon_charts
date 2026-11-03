using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A step line over a smooth line through the same points.
/// </summary>
public sealed class LineStepScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineStep";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        static double Curve(double value) => Math.Sin(value * 1.1);

        var smooth = new LineSeries<double, double>(CreateData(Curve, 25), x, y) { Color = Blue, Thickness = 1 };
        var step = new LineSeries<double, double>(CreateData(Curve, 25), x, y) { Color = Orange, Thickness = 2, IsStep = true };
        chart.AddSeries(smooth);
        chart.AddSeries(step);
        return [smooth, step];
    }
}
