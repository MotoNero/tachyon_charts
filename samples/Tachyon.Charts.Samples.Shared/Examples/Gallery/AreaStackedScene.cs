using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Three layers on top of each other. The middle one has a gap, and the upper one keeps its place.</summary>
public sealed class AreaStackedScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "AreaStacked";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(0, 9), out var x, out var y);
        IReadOnlyList<IDataSource<double, double>> layers =
        [
            Sample(t => 2 + Math.Sin(t)),
            Sample(t => t is > 4 and < 5 ? double.NaN : 2 + Math.Cos(t * 0.7)),
            Sample(t => 1.5 + (0.5 * Math.Sin(t * 2))),
        ];
        chart.AddSeries(new StackedAreaSeries(layers, x, y)
        {
            Fills = [new SolidFill(new Rgba8(76, 201, 240, 64)), new SolidFill(new Rgba8(247, 37, 133, 64)), new SolidFill(new Rgba8(124, 252, 154, 64))],
        });
        disposables = [];
        return chart;
    }
}
