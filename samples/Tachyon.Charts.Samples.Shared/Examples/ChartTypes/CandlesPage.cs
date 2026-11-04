using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Candles“ of the showroom: daily candles with their volume, the last day trades live.</summary>
public static class CandlesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Candles";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Pointer: open, high, low, close and volume | Double click: all";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var data = CandleDemo.Live();
        c.Title("Share price, the last day trades live")
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .YAxis(a => a.Numeric("volume").Title("Volume").AutoRange().Pane(1, 0.3))
            .Candles(data, yAxis: "price")
            .Volume(data, yAxis: "volume")
            .Modifiers(m => m.ZoomPan().Crosshair());
    }
}
