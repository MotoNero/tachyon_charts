using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>The chart type „Stepping monitor“ of the showroom: four channels fill one page after the other.</summary>
public static class SteppingMonitorPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Stepping monitor";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "The trace fills a page from the left, then the next page begins | Wheel on an axis: zoom the values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Monitor, 4 channels × 25 000 samples per second")
        .Sweep(SweepDemo.Live(SweepMode.Stepping), SweepDemo.Ranges, SweepDemo.Names)
        .Modifiers(m => m.AxisDrag());
}
