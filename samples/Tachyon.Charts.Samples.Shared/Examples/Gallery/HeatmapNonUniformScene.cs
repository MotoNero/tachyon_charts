using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Cells of different sizes on a logarithmic Y axis, colored by the logarithm of their values.
/// </summary>
public sealed class HeatmapNonUniformScene : IScene, IChartScene
{
    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "HeatmapNonUniform";

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
        double[] xEdges = [0, 1, 3, 4, 8, 9, 10];
        double[] yEdges = [1, 10, 100, 1000, 10000];
        var data = new HeatmapData(6, 4);
        var values = new float[24];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i == 9 ? float.NaN : MathF.Pow(10, i / 6f);
        }

        data.SetValues(values);
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 10), LabelFormat = _labels };
        var y = new LogAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(1, 10000) };
        var color = new ColorAxis("color") { Colormap = Colormaps.Viridis, IsLogarithmic = true, MissingColor = new Rgba8(200, 60, 60, 255) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var heatmap = new HeatmapSeries(data, x, y, color) { XEdges = xEdges, YEdges = yEdges };
        chart.AddSeries(heatmap);
        disposables = [];
        return chart;
    }
}
