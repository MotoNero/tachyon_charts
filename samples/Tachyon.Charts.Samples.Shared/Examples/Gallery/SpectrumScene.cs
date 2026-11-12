using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.SpectrumData;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Three carriers over noise with their density, the highest values and a mask.</summary>
public sealed class SpectrumScene : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => "Spectrum";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = Create(out var display, out var disposables);
        var frequencies = Enumerable.Range(0, 1500).Select(i => i * 1e7 / 1499).ToArray();
        var levels = new float[frequencies.Length];
        var random = new Random(1);
        chart.Render(device, target);
        for (var pass = 0; pass < 60; pass++)
        {
            Deliver(display, frequencies, levels, random);
            chart.Render(device, target);
        }

        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in disposables)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc/>
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables) => Create(out _, out disposables);

    private static ChartSurface Create(out SpectrumDisplay display, out IReadOnlyList<IDisposable> disposables)
    {
        display = CreateDisplay();
        var chart = new ChartBuilder().Spectrum(display).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        disposables = [.. chart.Series.OfType<IDisposable>()];
        return chart;
    }
}
