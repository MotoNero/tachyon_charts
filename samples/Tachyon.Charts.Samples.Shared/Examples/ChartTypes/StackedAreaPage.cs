using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Stacked area“ of the showroom.</summary>
public static class StackedAreaPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Stacked area";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, 0, 9).AddSeries((x, y) => new StackedAreaSeries([Sample(t => 2 + Math.Sin(t)), Sample(t => 2 + Math.Cos(t * 0.7)), Sample(t => 1.5 + (0.5 * Math.Sin(t * 2)))], x, y)).Modifiers(Navigate);
}
