using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>An area colored by its values above round bars with soft shadows.</summary>
public sealed class GradientsScene : IScene
{
    /// <inheritdoc />
    public string Name => "Gradients";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var labels = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture };
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(-0.5, 9.5), LabelFormat = labels };
        var top = new NumericAxis("top", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 40), LabelFormat = labels };
        var bottom = new NumericAxis("bottom", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 10), LabelFormat = labels };
        var chart = new ChartSurface([x], [new LayoutPane([top]), new LayoutPane([bottom])], SkiaTextEngine.Default) { Title = Name };
        var curve = new DataSource<double, double>(200);
        for (var i = 0; i < 200; i++)
        {
            var t = -0.5 + (i * 10.0 / 199);
            curve.Append(t, 20 + (15 * Math.Sin(t * 0.9)));
        }

        chart.AddSeries(new AreaSeries(curve, x, top) { Fill = new ValueGradientFill(Colormaps.Turbo, new DoubleRange(0, 40)) });
        chart.AddSeries(new BarSeries([[3, 5, 7, 4, 8, 6, 9, 5, 4, 6]], x, bottom)
        {
            CornerRadius = 8,
            BarWidth = 0.6,
            Fills = [new PositionGradientFill(new Rgba8(255, 183, 3, 255), new Rgba8(247, 37, 133, 255))],
            ShadowColor = new Rgba8(0, 0, 0, 60),
        });
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
