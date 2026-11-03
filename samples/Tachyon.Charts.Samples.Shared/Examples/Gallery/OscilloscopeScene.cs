using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ScopeData;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Four channels with fixed waveforms on an oscilloscope screen.</summary>
public sealed class OscilloscopeScene : IScene
{
    /// <inheritdoc />
    public string Name => "Oscilloscope";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = new ChartBuilder().Oscilloscope(CreateDisplay()).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}
