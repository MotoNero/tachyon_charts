using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A small chart with the window of another one marked.
/// </summary>
public sealed class OverviewScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "Overview";

    /// <inheritdoc />
    public override void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, series) = CreateChart();
        var mainAxis = new NumericAxis("main", AxisPosition.Bottom) { VisibleRange = new DoubleRange(3, 6) };
        var main = new ChartSurface([mainAxis], [new Tachyon.Charts.Core.Layout.LayoutPane([new NumericAxis("y", AxisPosition.Left)])], SkiaTextEngine.Default);
        var interaction = new ChartInteraction(main);
        var pipeline = new ModifierPipeline { Interaction = new ChartInteraction(chart) };
        pipeline.Add(new OverviewModifier(() => interaction));
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);
        return [line];
    }
}
