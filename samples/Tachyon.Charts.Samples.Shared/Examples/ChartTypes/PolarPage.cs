using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Polar“ of the showroom.</summary>
public static class PolarPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Polar";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Radial;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Polar(c, 10, Degrees, (x, y) => new PolarSeries(Angles(), [.. Angles().Select(d => 5 + (3 * Math.Sin(d * Math.PI / 45)))], x, y) { Parts = PolarParts.Line });
}
