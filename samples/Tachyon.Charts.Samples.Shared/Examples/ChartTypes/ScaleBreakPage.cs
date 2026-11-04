using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Scale break“ of the showroom: small values and a high peak, with the empty range between left out.</summary>
public static class ScaleBreakPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Scale break";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = BreakDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("A peak and the small values beside it")
        .XAxis(a => a.Numeric("x").Title("Time in s").Range(0, 20))
        .YAxis(a => a.Numeric("y").Title("Counts").Range(0, 1010).Breaks(new DoubleRange(45, 960)))
        .AddLine(BreakDemo.Peak(), s => s.Name("Counts").Thickness(1.5))
        .Modifiers(m => m.ZoomPan().Crosshair().Tooltip());
}
