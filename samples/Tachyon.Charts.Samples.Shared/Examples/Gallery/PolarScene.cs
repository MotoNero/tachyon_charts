using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A spiral line, a rose as an area and markers on a polar grid.</summary>
public sealed class PolarScene : IScene
{
    /// <inheritdoc />
    public string Name => "Polar";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(600, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, angle, radial) = PolarChart.Create(SkiaTextEngine.Default, new DoubleRange(0, 10));
        chart.Title = Name;
        var degrees = Enumerable.Range(0, 361).Select(i => (double)i).ToArray();
        using var rose = new PolarSeries(degrees, [.. degrees.Select(d => 5 + (3 * Math.Sin(d * Math.PI / 45)))], angle, radial) { Parts = PolarParts.Area | PolarParts.Line, IsClosed = true };
        using var spiral = new PolarSeries(degrees, [.. degrees.Select(d => d / 40)], angle, radial) { Color = new Rgba8(255, 183, 3, 255), Thickness = 2 };
        double[] pointAngles = [30, 110, 200, 300];
        using var points = new PolarSeries(pointAngles, [9, 7, 8.5, 6], angle, radial) { Parts = PolarParts.Markers, Color = new Rgba8(247, 37, 133, 255) };
        chart.AddSeries(rose);
        chart.AddSeries(spiral);
        chart.AddSeries(points);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
