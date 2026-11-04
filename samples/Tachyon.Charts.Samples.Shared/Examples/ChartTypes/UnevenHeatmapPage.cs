using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Uneven heatmap 4096²“ of the showroom: columns logarithmically spaced, rows growing towards the top.</summary>
public static class UnevenHeatmapPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Uneven heatmap 4096²";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Pointer: the cell and its value | Double click: all";

    /// <summary>Gets the edges of the columns: 4096 cells from 1 to 10 000, evenly on a logarithmic scale.</summary>
    /// <returns>The edges.</returns>
    public static double[] LogEdges() => [.. Enumerable.Range(0, 4097).Select(i => Math.Pow(10, i * 4.0 / 4096))];

    /// <summary>Gets the edges of the rows: 4096 cells from 0 to 100, each a little taller than the one below.</summary>
    /// <returns>The edges.</returns>
    public static double[] GrowingEdges() => [.. Enumerable.Range(0, 4097).Select(i => 100 * Math.Pow(i / 4096.0, 1.6))];

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("4096 × 4096 cells of their own sizes")
        .XAxis(a => a.Log("frequency").Title("Frequency in Hz").Range(1, 10_000))
        .YAxis(a => a.Numeric("depth").Title("Depth in m").Range(0, 100))
        .AddHeatmap(Heatmap4kData.CreateData(withGap: false), s => s.Edges(LogEdges(), GrowingEdges()).ColorAxis(a => a.Colormap(Colormaps.Turbo)))
        .Modifiers(m => m.ZoomPan().Crosshair().Tooltip());
}
