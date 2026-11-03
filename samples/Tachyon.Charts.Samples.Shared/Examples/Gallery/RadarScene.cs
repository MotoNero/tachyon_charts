using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Two products compared on six qualities.</summary>
public sealed class RadarScene : IScene
{
    /// <inheritdoc />
    public string Name => "Radar";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(600, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        string[] names = ["Tempo", "Preis", "Qualität", "Service", "Design", "Reichweite"];
        var (chart, angle, radial) = PolarChart.Create(SkiaTextEngine.Default, new DoubleRange(0, 100), names);
        chart.Title = Name;
        using var first = PolarSeries.Radar([80, 60, 90, 70, 50, 75], angle, radial);
        using var second = PolarSeries.Radar([50, 90, 60, 85, 80, 40], angle, radial);
        second.Color = new Rgba8(247, 37, 133, 255);
        second.Fill = new SolidFill(new Rgba8(247, 37, 133, 60));
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
