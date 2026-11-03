using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// Major and minor grid lines over alternating bands and a vertical plot gradient.
/// </summary>
public sealed class GridBandsScene : IScene
{
    /// <inheritdoc />
    public string Name => "GridBands";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 520);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = CreateAxis("x", AxisPosition.Bottom, 0, 12, "Major and minor grid");
        var y = CreateAxis("y", AxisPosition.Left, 0, 8, null);
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default)
        {
            Title = Name,
            Style = new ChartSurfaceStyle
            {
                BackgroundColor = new Rgba8(11, 14, 22, 255),
                PlotGradientStart = new Rgba8(32, 45, 66, 255),
                PlotGradientEnd = new Rgba8(12, 18, 31, 255),
                MajorGridColor = new Rgba8(170, 190, 220, 72),
                MinorGridColor = new Rgba8(170, 190, 220, 28),
                BandColor = new Rgba8(110, 160, 220, 20),
            },
        };

        chart.Render(device, target);
        device.WaitForIdle();
    }

    private static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max, string? title) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
        Title = title,
    };
}
