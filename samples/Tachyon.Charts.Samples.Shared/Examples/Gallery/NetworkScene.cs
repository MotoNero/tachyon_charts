using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A filter measured over frequency: two traces, markers and limits, one trace fails.</summary>
public sealed class NetworkScene : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => "Network";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = CreateChart(out var disposables);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in disposables)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc/>
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = new ChartBuilder().NetworkAnalyzer(Examples.Common.NetworkData.CreateDisplay(passing: false), 1e9, 2e9, -70, 5).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        disposables = [.. chart.Series.OfType<IDisposable>()];
        return chart;
    }

}
