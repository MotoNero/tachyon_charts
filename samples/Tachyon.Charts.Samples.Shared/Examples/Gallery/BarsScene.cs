using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Bars in the ways they can share a place. The scene exists once per layout.</summary>
public sealed class BarsScene(string name, BarLayout layout, bool isHorizontal) : ShapeScene
{
    private static readonly double[][] _rows =
    [
        [3, 5, 2, 6, 4, 7],
        [2, 1, 4, 3, 5, 2],
        [1, 3, double.NaN, 2, 2, 3],
    ];

    /// <inheritdoc />
    public override string Name => name;

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var values = layout switch
        {
            BarLayout.Percent => new DoubleRange(0, 100),
            BarLayout.Stacked => new DoubleRange(0, 14),
            _ => new DoubleRange(0, 8),
        };
        var places = new DoubleRange(-0.5, 5.5);
        var chart = CreateAxes(isHorizontal ? values : places, isHorizontal ? places : values, out var x, out var y);
        chart.AddSeries(new BarSeries(_rows, x, y) { Layout = layout, IsHorizontal = isHorizontal });
        disposables = [];
        return chart;
    }
}
