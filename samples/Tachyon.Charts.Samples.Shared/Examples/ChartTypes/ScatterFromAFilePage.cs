using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „Scatter from a file“ of the showroom: 200 million points from a file of 1.6 GB as a density. The
/// first visit writes the file and sorts its points into tiles in the background.
/// </summary>
public static class ScatterFromAFilePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Scatter from a file";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | The first visit writes a test file of 1.6 GB";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title(SampleText.T("200 million points from a file"))
        .XAxis(a => a.Numeric("x").Title("X").AutoRange())
        .YAxis(a => a.Numeric("y").Title("Y").AutoRange())
        .AddPointDensity(CloudFileDemo.Open(CloudFileDemo.PageCount), s => s.Name(SampleText.T("Points")).ColorAxis(a => a.Colormap(Colormaps.Inferno).Log()))
        .AddOverlay(chart => new RefiningOverlay(chart) { Format = SampleText.T("Refining {0:P0}") })
        .Modifiers(m => m.ZoomPan().RubberBand());
}
