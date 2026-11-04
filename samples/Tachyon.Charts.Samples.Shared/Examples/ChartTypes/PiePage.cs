using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Pie“ of the showroom.</summary>
public static class PiePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Pie";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = PieData.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Power mix").Pie(PieData.Values, PieData.Names, pie => pie.ExplodedIndex = 1);
}
