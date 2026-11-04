using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „OHLC bars“ of the showroom, with colors of its own.</summary>
public static class OhlcBarsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "OHLC bars";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = CandlesPage.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Share price as OHLC bars")
        .XAxis(a => a.DateTime("time"))
        .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
        .Candles(CandleDemo.Create(120, TimeSpan.FromDays(1), seed: 3), s =>
        {
            s.Style = CandleStyle.Ohlc;
            s.Rising = new CandleLook(new Rgba8(0, 114, 178, 255), new Rgba8(0, 114, 178, 255));
            s.Falling = new CandleLook(new Rgba8(230, 159, 0, 255), new Rgba8(230, 159, 0, 255));
        })
        .Modifiers(m => m.ZoomPan().Crosshair());
}
