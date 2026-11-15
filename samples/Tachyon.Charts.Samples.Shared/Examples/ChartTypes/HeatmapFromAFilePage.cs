using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „Heatmap from a file“ of the showroom: 268 million cells from a raster file of 1 GB, read through its
/// stages; each stage keeps the highest value, so a single hot cell stays visible however far out the view is. The
/// first visit writes the file and builds the stages in the background.
/// </summary>
public static class HeatmapFromAFilePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Heatmap from a file";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | The first visit writes a test file of 1 GB";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title(SampleText.T("268 million cells from a file"))
        .XAxis(a => a.Numeric("x").Title(SampleText.T("Column")).AutoRange())
        .YAxis(a => a.Numeric("y").Title(SampleText.T("Row")).AutoRange())
        .AddFileRaster(RasterFileDemo.Open(RasterFileDemo.PageSize, RasterFileDemo.PageSize), s => s.Name(SampleText.T("Value")).ColorAxis(a => a.Colormap(Colormaps.Turbo)))
        .AddOverlay(chart => new RefiningOverlay(chart) { Format = SampleText.T("Refining {0:P0}") })
        .Modifiers(m => m.ZoomPan().RubberBand());
}
