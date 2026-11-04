using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Donut“ of the showroom.</summary>
public static class DonutPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Donut";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = PieData.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Power mix").Pie(PieData.Values, PieData.Names, pie => pie.InnerRadius = 0.55);
}
