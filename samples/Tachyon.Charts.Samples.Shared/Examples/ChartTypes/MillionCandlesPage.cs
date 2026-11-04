using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „1 million candles“ of the showroom: minute candles over about two years.</summary>
public static class MillionCandlesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "1 million candles";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = CandlesPage.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var data = CandleDemo.Create(1_000_000, TimeSpan.FromMinutes(1));
        c.Title("1 million minute candles")
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .YAxis(a => a.Numeric("volume").Title("Volume").AutoRange().Pane(1, 0.25))
            .Candles(data, s => s.Rising = s.Rising with { IsHollow = true }, yAxis: "price")
            .Volume(data, yAxis: "volume")
            .Modifiers(m => m.ZoomPan().Crosshair());
    }
}
