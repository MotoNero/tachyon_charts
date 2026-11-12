using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A noisy, jittering sine after many acquisitions, colored by how often each pixel was hit.</summary>
public sealed class ColorGradeScene : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => "ColorGrade";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = Create(out var channel, out var disposables);
        var random = new Random(1);
        var time = new double[3000];
        var volts = new float[3000];
        chart.Render(device, target);
        for (var pass = 0; pass < 120; pass++)
        {
            var jitter = (random.NextDouble() - 0.5) * 0.3;
            for (var i = 0; i < time.Length; i++)
            {
                time[i] = (i - 1500) * 3e-6;
                var amplitude = 2 + ((pass % 7) * 0.05);
                volts[i] = (float)((amplitude * Math.Sin((2 * Math.PI * 1000 * time[i]) + jitter)) + ((random.NextDouble() - 0.5) * 0.15));
            }

            channel.SetWaveform(time, volts);
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

    private static ChartSurface Create(out ScopeChannel channel, out IReadOnlyList<IDisposable> disposables)
    {
        var display = new ScopeDisplay { TimePerDivision = 5e-4, Persistence = ScopePersistence.ColorGrade };
        channel = display.AddChannel();
        var chart = new ChartBuilder().Oscilloscope(display).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        disposables = [.. chart.Series.OfType<IDisposable>()];
        return chart;
    }
}
