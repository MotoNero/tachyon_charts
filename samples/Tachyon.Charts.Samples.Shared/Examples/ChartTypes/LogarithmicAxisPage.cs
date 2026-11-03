using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Logarithmic axis“ of the showroom.</summary>
public static class LogarithmicAxisPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Logarithmic axis";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Points;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Theme(BuiltInThemes.Instrument)
        .XAxis(a => a.Numeric("x").Range(0, 10))
        .YAxis(a => a.Log("y").Range(0.01, 10000))
        .AddLine(Sample(t => 5000 * Math.Exp(-t * 1.2)), s => s.Thickness(2))
        .Modifiers(WithValues);
}
