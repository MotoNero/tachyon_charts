using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Measured values with their uncertainty, one point without a value.</summary>
public sealed class ErrorBarsScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "ErrorBars";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(-0.5, 12.5), new DoubleRange(0, 4), out var x, out var y);
        var points = Sample(t => t is > 5.5 and < 6.5 ? double.NaN : 2 + Math.Sin(t * 0.8), 13, 12);
        var errors = Enumerable.Range(0, 13).Select(i => 0.15 + (0.05 * (i % 5))).ToArray();
        chart.AddSeries(new ErrorBarSeries(points, errors, errors, x, y) { Color = new Rgba8(80, 170, 240, 255), Thickness = 1.5 });
        disposables = [];
        return chart;
    }
}
