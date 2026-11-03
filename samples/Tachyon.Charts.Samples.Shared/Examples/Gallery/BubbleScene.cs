using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A bubble chart: position, size and color show one value each.</summary>
public sealed class BubbleScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Bubble";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(0, 10), out var x, out var y);
        var points = new DataSource<double, double>(40);
        var sizes = new float[40];
        for (var i = 0; i < sizes.Length; i++)
        {
            points.Append(0.5 + (9 * ((i * 0.618) % 1)), 0.5 + (9 * ((i * 0.7549) % 1)));
            sizes[i] = 6 + (i % 7 * 6);
        }

        var bubbles = new ScatterSeries<double, double>(points, x, y) { Color = new Rgba8(80, 170, 240, 170), BorderThickness = 0, PointSizes = sizes };
        chart.AddSeries(bubbles);
        disposables = [bubbles];
        return chart;
    }
}
