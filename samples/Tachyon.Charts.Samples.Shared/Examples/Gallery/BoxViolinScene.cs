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

/// <summary>Three samples as box plots with outliers, the same samples as violins in a second pane.</summary>
public sealed class BoxViolinScene : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => "BoxViolin";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

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
        var random = new Random(5);
        double[][] samples =
        [
            [.. Enumerable.Range(0, 300).Select(_ => (random.NextDouble() + random.NextDouble() + random.NextDouble()) * 2)],
            [.. Enumerable.Range(0, 300).Select(_ => -Math.Log(1 - random.NextDouble()) + 1)],
            [.. Enumerable.Range(0, 300).Select(_ => random.Next(2) == 0 ? 2 + random.NextDouble() : 5 + random.NextDouble())],
        ];
        string[] names = ["Normal", "Schief", "Zweigipflig"];
        var x = new CategoryAxis("x", AxisPosition.Bottom, names);
        var labels = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture };
        var top = new NumericAxis("top", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 8), LabelFormat = labels };
        var bottom = new NumericAxis("bottom", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 8), LabelFormat = labels };
        var chart = new ChartSurface([x], [new LayoutPane([top]), new LayoutPane([bottom])], SkiaTextEngine.Default) { Title = Name };
        chart.AddSeries(new BoxPlotSeries(samples, x, top));
        chart.AddSeries(new ViolinSeries(samples, x, bottom) { Fill = new SolidFill(new Rgba8(247, 37, 133, 64)) });
        disposables = [];
        return chart;
    }
}
