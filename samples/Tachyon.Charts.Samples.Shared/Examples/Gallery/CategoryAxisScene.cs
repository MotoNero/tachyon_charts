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

/// <summary>Named places with rotated labels above a pane with too many names, which get thinned out.</summary>
public sealed class CategoryAxisScene : IScene, IChartScene
{
    private static readonly string[] _months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    /// <inheritdoc />
    public string Name => "CategoryAxis";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 400);

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
        var x = new CategoryAxis("x", AxisPosition.Bottom, _months) { LabelRotation = -45 };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 10), LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture } };
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        chart.AddSeries(new BarSeries([[3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4]], x, y));
        disposables = [];
        return chart;
    }
}
