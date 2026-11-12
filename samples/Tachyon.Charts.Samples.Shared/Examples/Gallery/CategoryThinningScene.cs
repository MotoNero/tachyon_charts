using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Far more names than room: only every n-th one stays, without any overlap.</summary>
public sealed class CategoryThinningScene : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => "CategoryThinning";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 300);

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

    /// <inheritdoc />
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var names = Enumerable.Range(0, 200).Select(i => $"Sensor {i}").ToArray();
        var x = new CategoryAxis("x", AxisPosition.Bottom, names);
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 2), LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture } };
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        chart.AddSeries(new BarSeries([[.. Enumerable.Range(0, 200).Select(i => 1 + Math.Sin(i * 0.1))]], x, y));
        disposables = [];
        return chart;
    }
}
