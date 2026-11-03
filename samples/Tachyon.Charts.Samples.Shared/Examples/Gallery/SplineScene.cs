using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>The same points as a smooth curve and as straight lines, with a gap.</summary>
public sealed class SplineScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Spline";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-0.5, 3.5), out var x, out var y);
        var points = Sample(t => t is > 6.5 and < 7.5 ? double.NaN : 1.5 + (1.4 * Math.Sign(Math.Sin(t * 1.3))) * Math.Abs(Math.Sin(t * 1.3)), 13, 10);
        var straight = new LineSeries<double, double>(points, x, y) { Color = new Rgba8(140, 150, 170, 255), Thickness = 1 };
        var smooth = new LineSeries<double, double>(points, x, y) { Color = new Rgba8(80, 200, 160, 255), Thickness = 2, IsSpline = true };
        chart.AddSeries(straight);
        chart.AddSeries(smooth);
        disposables = [straight, smooth];
        return chart;
    }
}
