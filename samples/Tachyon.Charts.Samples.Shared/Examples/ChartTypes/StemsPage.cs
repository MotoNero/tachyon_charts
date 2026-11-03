using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Stems“ of the showroom.</summary>
public static class StemsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Stems";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, -1, 41, -0.5, 3).AddSeries((x, y) => new StemSeries(Sample(t => 2.5 * Math.Exp(-0.1 * t) * Math.Abs(Math.Cos(t * 0.6)), 41, 40), x, y) { HeadSize = 6 }).Modifiers(Navigate);
}
