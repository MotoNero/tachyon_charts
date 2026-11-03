using Tachyon.Charts.Core.Fluent;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Step line“ of the showroom.</summary>
public static class StepLinePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Step line";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Points;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Numeric(c, 0, 10, -1.5, 1.5).AddLine(Sample(t => Math.Round(Math.Sin(t) * 3) / 3, 60), s => s.Step().Thickness(2)).Modifiers(WithValues);
}
