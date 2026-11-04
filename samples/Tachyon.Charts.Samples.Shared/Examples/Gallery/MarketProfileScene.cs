using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Daily candles with the volume per price level of the shown months, point of control and value area highlighted.</summary>
public sealed class MarketProfileScene : IScene
{
    /// <inheritdoc />
    public string Name => "MarketProfile";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 560);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var data = CandleDemo.Create(200, TimeSpan.FromDays(1), seed: 11);
        var builder = new ChartBuilder().Title("Market profile").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .Candles(data, yAxis: "price")
            .VolumeProfile(data, profile => profile.Levels = 40, yAxis: "price");
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);

        // The second half of the days only: the profile follows the visible range.
        var axis = chart.HorizontalAxes[0];
        var all = axis.VisibleRange;
        axis.VisibleRange = new DoubleRange(all.Min + (all.Length / 2), all.Max);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
