using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>An area fading out towards the baseline, with a gap of missing values, and its line.</summary>
public sealed class AreaScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Area";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1, 3), out var x, out var y);
        var curve = Sample(t => t is > 6 and < 6.8 ? double.NaN : 1.2 + Math.Sin(t) + (0.4 * Math.Sin(t * 5)));
        chart.AddSeries(new AreaSeries(curve, x, y) { Fill = new PositionGradientFill(new Rgba8(80, 170, 240, 230), new Rgba8(80, 170, 240, 20)) });
        var line = new LineSeries<double, double>(curve, x, y) { Color = new Rgba8(170, 220, 255, 255), Thickness = 2 };
        chart.AddSeries(line);
        disposables = [line];
        return chart;
    }
}
