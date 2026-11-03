using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Heatmap4kData;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A field of 4096 by 4096 values, with a missing rectangle.
/// </summary>
public sealed class Heatmap4kScene : IScene
{
    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Heatmap4k";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 700);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 8), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 8), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Inferno, MissingColor = new Rgba8(90, 200, 90, 255) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var heatmap = new HeatmapSeries(CreateData(), x, y, color) { XStep = 8.0 / Length, YStep = 8.0 / Length };
        chart.AddSeries(heatmap);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
