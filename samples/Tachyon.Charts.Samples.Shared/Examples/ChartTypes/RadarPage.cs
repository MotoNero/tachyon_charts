using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Radar“ of the showroom.</summary>
public static class RadarPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Radar";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Radial;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Polar(c, 100, ["Speed", "Price", "Quality", "Service", "Design", "Range"], (x, y) => PolarSeries.Radar([80, 60, 90, 70, 50, 75], x, y));
}
