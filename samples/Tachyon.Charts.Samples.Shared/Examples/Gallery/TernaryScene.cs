using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Mixtures of three parts in a ternary diagram.</summary>
public sealed class TernaryScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Ternary";

    /// <inheritdoc />
    public override PixelSize Size { get; } = new(700, 640);

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 1), new DoubleRange(0, 1), out var x, out var y);
        x.IsHidden = true;
        y.IsHidden = true;
        var a = new double[60];
        var b = new double[60];
        var c = new double[60];
        for (var i = 0; i < a.Length; i++)
        {
            var t = i / 59.0;
            a[i] = 0.2 + (0.5 * t);
            b[i] = 0.6 - (0.4 * t) + (0.1 * Math.Sin(i * 0.9));
            c[i] = 0.2 + (0.1 * Math.Cos(i * 1.7));
        }

        var points = new TernarySeries(a, b, c, x, y) { Color = new Rgba8(240, 130, 90, 255) };
        chart.AddSeries(points);
        chart.AddOverlay(new TernaryGridOverlay("A", "B", "C"));
        disposables = [points];
        return chart;
    }
}
