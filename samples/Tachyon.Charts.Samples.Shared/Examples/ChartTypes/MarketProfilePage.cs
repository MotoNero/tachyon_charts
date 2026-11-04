using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Market profile“ of the showroom: the volume per price level of the visible days, over the candles.</summary>
public static class MarketProfilePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Market profile";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan, the profile follows | Pointer on a bar: price and volume | Double click: all";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var data = CandleDemo.Create(400, TimeSpan.FromDays(1), seed: 11);
        c.Title("Volume per price level of the visible days")
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .Candles(data, yAxis: "price")
            .VolumeProfile(data, profile => profile.Levels = 40, yAxis: "price")
            .Modifiers(m => m.ZoomPan().Crosshair());
    }
}
