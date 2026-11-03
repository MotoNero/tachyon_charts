using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A band between two curves, green where the first one is on top, red elsewhere.</summary>
public sealed class BandScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Band";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1.5, 1.5), out var x, out var y);
        var first = Sample(Math.Sin);
        var second = Sample(t => 0.8 * Math.Cos(t * 0.8));
        chart.AddSeries(new BandSeries(first, second, x, y));
        var a = new LineSeries<double, double>(first, x, y) { Color = new Rgba8(110, 210, 130, 255), Thickness = 1.5 };
        var b = new LineSeries<double, double>(second, x, y) { Color = new Rgba8(240, 100, 100, 255), Thickness = 1.5 };
        chart.AddSeries(a);
        chart.AddSeries(b);
        disposables = [a, b];
        return chart;
    }
}
