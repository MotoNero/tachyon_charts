using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Candles with drawings fixed to days and prices; the second set comes from JSON, as saved drawings do.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="add">Adds the drawings.</param>
public sealed class DrawingsScene(string name, Action<CandleData, DrawingLayer> add) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var prices = DrawingDemo.Prices();
        var builder = new ChartBuilder().Title("Drawing tools").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.DateTime("time"))
            .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
            .Candles(prices)
            .Drawings(layer =>
            {
                // Through JSON and back, so that the picture also shows that saving keeps every drawing.
                var scratch = new DrawingLayer(layer.XAxis, layer.YAxis);
                add(prices, scratch);
                layer.Load(scratch.Save());
            });
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
