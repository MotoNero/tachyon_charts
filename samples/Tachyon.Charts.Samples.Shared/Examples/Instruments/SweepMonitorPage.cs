using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>The chart type „Sweep monitor“ of the showroom: four channels write over themselves like a patient monitor.</summary>
public static class SweepMonitorPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Sweep monitor";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = SweepDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Monitor, 4 channels × 25 000 samples per second")
        .Sweep(SweepDemo.Live(SweepMode.Sweep), SweepDemo.Ranges, SweepDemo.Names)
        .Modifiers(m => m.AxisDrag());
}
