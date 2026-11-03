using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A resonant circuit over frequency in a Smith chart, with a marker at its resonance.</summary>
public sealed class SmithScene : IScene
{
    /// <inheritdoc />
    public string Name => "Smith";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(700, 700);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, x, y, _) = SmithChart.Create(SkiaTextEngine.Default);
        chart.Title = Name;
        const int Count = 200;
        var resistance = new double[Count];
        var reactance = new double[Count];
        for (var i = 0; i < Count; i++)
        {
            // A series circuit: 20 ohms, reactance from capacitive to inductive.
            var detuning = (i - (Count / 2.0)) / (Count / 2.0);
            resistance[i] = 20 + (10 * detuning * detuning);
            reactance[i] = 120 * detuning;
        }

        using var series = new SmithSeries(resistance, reactance, x, y);
        chart.AddSeries(series);
        chart.AddOverlay(new SmithMarkerOverlay(series, Count / 2));
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
