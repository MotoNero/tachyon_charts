using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.PointCloudData;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// 50 million points in clusters. Their number switches the series to a density map by itself.
/// </summary>
public sealed class PointCloud50MScene : IScene, IChartScene
{
    private const int PointCount = 50_000_000;

    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "PointCloud50M";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 600);

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
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 10), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 10), LabelFormat = _labels };
        var color = new ColorAxis("color") { Colormap = Colormaps.Inferno, IsLogarithmic = true, BelowColor = default(Rgba8) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var scatter = new ScatterSeries<float, float>(CreateCloud(PointCount), x, y) { DensityAxis = color };
        chart.AddSeries(scatter);

        // The range of the colors follows the first count.
        disposables = [];
        return chart;
    }

}
