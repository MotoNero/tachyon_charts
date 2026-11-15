using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „Large file“ of the showroom: a billion values from a column file of 4 GB, which stays on the disk.
/// The first visit writes the file; the picture is coarse at once and refines while the pyramid grows.
/// </summary>
public static class LargeFilePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Large file";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | The first visit writes a test file of 4 GB";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title(SampleText.T("1 billion values from a 4 GB file"))
        .XAxis(a => a.Numeric("x").Title(SampleText.T("Time in s")).AutoRange())
        .YAxis(a => a.Numeric("y").Title(SampleText.T("Value")).AutoRange())
        .AddSeries((x, y) =>
        {
            var (column, pyramid) = LargeFileDemo.Open(LargeFileDemo.PageCount);
            return new ColumnLineSeries<float>(column, pyramid, x, y) { Name = SampleText.T("Measurement"), Color = new Rgba8(80, 170, 240, 255), Thickness = 1 };
        })
        .AddOverlay(chart => new RefiningOverlay(chart) { Format = SampleText.T("Refining {0:P0}") })
        .Modifiers(m => m.ZoomPan().RubberBand());
}
