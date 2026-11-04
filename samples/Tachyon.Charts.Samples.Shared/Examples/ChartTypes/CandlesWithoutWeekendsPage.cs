using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Candles without weekends“ of the showroom: the time axis hides Saturdays and Sundays.</summary>
public static class CandlesWithoutWeekendsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Candles without weekends";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = BreakDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var data = BreakDemo.Workdays(60);
        c.Title("Working days only, the weekends take no room")
            .XAxis(a => a.DateTime("time").AutoRange().Gaps(BreakDemo.Weekends(data)))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .Candles(data, yAxis: "price")
            .Modifiers(m => m.ZoomPan().Crosshair());
    }
}
