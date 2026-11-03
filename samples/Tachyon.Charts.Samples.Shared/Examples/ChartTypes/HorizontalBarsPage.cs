using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Horizontal bars“ of the showroom.</summary>
public static class HorizontalBarsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Horizontal bars";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 8, -0.5, 5.5).AddSeries((x, y) => new BarSeries(Bars, x, y) { IsHorizontal = true }).Modifiers(Navigate);
}
