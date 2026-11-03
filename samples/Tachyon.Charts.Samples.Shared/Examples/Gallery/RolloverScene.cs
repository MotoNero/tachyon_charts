using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A vertical line with the values of three series, two of them close together.
/// </summary>
public sealed class RolloverScene : InteractionScene
{
    /// <inheritdoc />
    public override string Name => "Rollover";

    /// <inheritdoc />
    protected override IEnumerable<ChartModifier> CreateModifiers() => [new RolloverModifier()];

    /// <inheritdoc />
    protected override void Simulate(ModifierPipeline pipeline, ChartSurface chart) => MoveTo(pipeline, chart, 0.44, 0.5);

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var first = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2, Name = "Druck" };
        var second = new LineSeries<double, double>(CreateData(value => (0.9 * Math.Sin(value * 1.3)) + 0.05), x, y) { Color = Green, Thickness = 2, Name = "Soll" };
        var third = new LineSeries<double, double>(CreateData(value => 0.6 * Math.Cos(value * 0.8)), x, y) { Color = Orange, Thickness = 2, Name = "Temperatur" };
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.AddSeries(third);
        return [first, second, third];
    }
}
