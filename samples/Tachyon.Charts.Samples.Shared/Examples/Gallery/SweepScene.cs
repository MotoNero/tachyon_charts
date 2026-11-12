using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>The monitor after 8.4 seconds: the sweep writes over the first pass with a gap ahead, stepping shows the second page only.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="mode">How the display runs.</param>
public sealed class SweepScene(string name, SweepMode mode) : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = CreateChart(out _);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }

    /// <inheritdoc/>
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        disposables = [];
        return new ChartBuilder().Title(mode == SweepMode.Sweep ? "Sweep" : "Stepping").Theme(BuiltInThemes.Instrument)
            .Sweep(SweepDemo.Recorded(mode, 8.4), SweepDemo.Ranges, SweepDemo.Names).ToDefinition().Build(SkiaTextEngine.Default).Surface;
    }
}
