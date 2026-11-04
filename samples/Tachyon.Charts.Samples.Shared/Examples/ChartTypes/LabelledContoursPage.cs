using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Labelled contours“ of the showroom: height lines with their values along the line.</summary>
public static class LabelledContoursPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Labelled contours";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan, the labels move along | Double click: all";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Height lines with their values")
        .XAxis(a => a.Numeric("x").Range(0, 6))
        .YAxis(a => a.Numeric("y").Range(0, 6))
        .AddContour(ContourData.CreateField(512, withGap: false), s => s.Name("Height").Filled().Labels()
            .Levels(-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1, 1.25).Cells(0, 6.0 / 512, 0, 6.0 / 512))
        .Modifiers(m => m.ZoomPan());
}
