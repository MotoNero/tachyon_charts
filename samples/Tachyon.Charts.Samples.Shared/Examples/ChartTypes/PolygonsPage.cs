using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Polygons“ of the showroom: shapes with holes.</summary>
public static class PolygonsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Polygons";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = PolygonDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Polygons with holes")
        .XAxis(a => a.Numeric("x").Range(0, 10))
        .YAxis(a => a.Numeric("y").Range(0, 10))
        .Polygons(PolygonDemo.Shapes(), s => s.Thickness = 1.5)
        .Modifiers(m => m.ZoomPan().Crosshair().Tooltip());
}
