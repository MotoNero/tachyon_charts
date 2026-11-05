using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „10 000 polygons“ of the showroom: a field of hexagons.</summary>
public static class TenThousandPolygonsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "10 000 polygons";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = PolygonDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("10 000 hexagons")
        .XAxis(a => a.Numeric("x").Range(-1, 150))
        .YAxis(a => a.Numeric("y").Range(-1, 44))
        .Polygons(PolygonDemo.Hexagons(), s => s.Thickness = 0.5)
        .Modifiers(m => m.ZoomPan().Tooltip());
}
