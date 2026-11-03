using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Box plot“ of the showroom.</summary>
public static class BoxPlotPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Box plot";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Categories(c).AddSeries((x, y) => new BoxPlotSeries(GroupedSamples(), x, y)).Modifiers(Navigate);
}
