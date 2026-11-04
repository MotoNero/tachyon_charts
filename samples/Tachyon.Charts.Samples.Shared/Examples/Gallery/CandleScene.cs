using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Daily candles of an invented share with their volume in a pane below, or the same prices as OHLC bars.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="theme">The theme.</param>
/// <param name="style">How a day is drawn.</param>
public sealed class CandleScene(string name, Theme theme, CandleStyle style) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 560);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var data = CandleDemo.Create(90, TimeSpan.FromDays(1));
        var builder = new ChartBuilder().Title("Share price").Theme(theme)
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .YAxis(a => a.Numeric("volume").Title("Volume").AutoRange().Pane(1, 0.3))
            .Candles(data, s => s.Style = style, yAxis: "price")
            .Volume(data, yAxis: "volume");
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
