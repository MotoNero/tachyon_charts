using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Drawing tools“ of the showroom: candles with drawings to add, move, delete and undo.</summary>
public static class DrawingToolsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Drawing tools";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = DrawingDemo.Hint;

    /// <summary>Gets the drawings of the page, for example to save them with <see cref="DrawingLayer.Save"/>.</summary>
    public static DrawingLayer? Layer { get; private set; }

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var prices = DrawingDemo.Prices();
        c.Title("Drawing tools")
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .Candles(prices)
            .Drawings(layer =>
            {
                DrawingDemo.First(prices, layer);
                Layer = layer;
            })
            .Modifiers(m => m.ZoomPan().Keyboard());
    }
}
