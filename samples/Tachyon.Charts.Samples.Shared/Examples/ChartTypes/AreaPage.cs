using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Area“ of the showroom.</summary>
public static class AreaPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Area";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, -1, 3).AddSeries((x, y) => new AreaSeries(Sample(t => 1.2 + Math.Sin(t)), x, y)).Modifiers(Navigate);
}
