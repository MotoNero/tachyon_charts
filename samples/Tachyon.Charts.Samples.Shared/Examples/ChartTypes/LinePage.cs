using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Line“ of the showroom.</summary>
public static class LinePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Line";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Points;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, -1.5, 1.5).AddLine(Sample(Math.Sin), s => s.Name("Sine").Thickness(2)).Modifiers(WithValues);
}
