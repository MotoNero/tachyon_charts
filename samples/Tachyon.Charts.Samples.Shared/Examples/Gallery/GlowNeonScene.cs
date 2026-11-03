using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Bright lines with a glow on a dark plot. Skia shows a simplified glow.</summary>
public sealed class GlowNeonScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "GlowNeon";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1.5, 1.5), out var x, out var y);
        var first = new LineSeries<double, double>(Sample(Math.Sin), x, y) { Color = new Rgba8(76, 201, 240, 255), Thickness = 2, GlowRadius = 10, GlowIntensity = 0.8 };
        var second = new LineSeries<double, double>(Sample(t => Math.Cos(t * 1.3) * 0.8), x, y) { Color = new Rgba8(247, 37, 133, 255), Thickness = 2, GlowRadius = 6, GlowIntensity = 0.6 };
        chart.AddSeries(first);
        chart.AddSeries(second);
        disposables = [first, second];
        return chart;
    }
}
