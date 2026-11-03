using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Band“ of the showroom.</summary>
public static class BandPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Band";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, -1.5, 1.5).AddSeries((x, y) => new BandSeries(Sample(Math.Sin), Sample(t => 0.8 * Math.Cos(t * 0.8)), x, y)).Modifiers(Navigate);
}
