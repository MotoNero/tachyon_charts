using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Scatter“ of the showroom.</summary>
public static class ScatterPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Scatter";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Points;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, 0, 10).AddScatter(Cloud(), s => s.Size(6)).Modifiers(WithValues);
}
