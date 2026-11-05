using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Heatmap in an area“ of the showroom: a measurement shown only inside a region, not on its lake.</summary>
public static class HeatmapInAnAreaPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Heatmap in an area";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = PolygonDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var region = PolygonDemo.Region();
        c.Title("A measurement inside a region")
            .XAxis(a => a.Numeric("x").Range(0, 10))
            .YAxis(a => a.Numeric("y").Range(0, 10))
            .AddHeatmap(ContourData.CreateField(400, withGap: false), s => s.Cells(0, 10.0 / 400, 0, 10.0 / 400).Mask([region]).ColorAxis(a => a.Colormap(Colormaps.Turbo)))
            .Polygons([region], s => s.FillAlpha = 0)
            .Modifiers(m => m.ZoomPan().Tooltip());
    }
}
