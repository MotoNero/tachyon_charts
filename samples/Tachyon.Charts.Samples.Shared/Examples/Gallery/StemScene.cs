using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A spectrum as stems with a raised baseline.</summary>
public sealed class StemScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Stem";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(-1, 41), new DoubleRange(-0.5, 3), out var x, out var y);
        var points = Sample(t => 2.5 * Math.Exp(-0.1 * t) * Math.Abs(Math.Cos(t * 0.6)), 41, 40);
        chart.AddSeries(new StemSeries(points, x, y) { Color = new Rgba8(240, 130, 90, 255), HeadSize = 6 });
        disposables = [];
        return chart;
    }
}
