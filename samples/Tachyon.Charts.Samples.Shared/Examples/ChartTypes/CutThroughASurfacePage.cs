using Tachyon.Charts.Core.Fluent;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Cut through a surface“ of the showroom.</summary>
public static class CutThroughASurfacePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Cut through a surface";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Points;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, -1, 1, -1, 1).AddLine(Cut(), s => s.Name("Cut").Thickness(2)).Modifiers(WithValues);
}
