using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// A complete frame of the pipeline: two panes with grid, axes, text and a simple series each.
/// </summary>
public sealed class ChartFrameScene : IScene
{
    private const int PointCount = 400;

    /// <inheritdoc />
    public string Name => "ChartFrame";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <summary>
    /// Creates the chart of this scene.
    /// </summary>
    /// <returns>The chart and the series, which hold GPU buffers and must be disposed.</returns>
    public static (ChartSurface Chart, IReadOnlyList<LineSeries<double, double>> Series) CreateChart()
    {
        var x = CreateAxis("x", AxisPosition.Bottom, 0, 10, "Time");
        var upper = CreateAxis("upper", AxisPosition.Left, -1.5, 1.5, null);
        var lower = CreateAxis("lower", AxisPosition.Left, 0, 100, null);
        var chart = new ChartSurface([x], [new LayoutPane([upper], 2), new LayoutPane([lower])], SkiaTextEngine.Default)
        {
            Title = "Frame pipeline",
        };

        var first = new LineSeries<double, double>(CreateData(value => Math.Sin(value * 1.7)), x, upper) { Color = new Rgba8(80, 170, 240, 255) };
        var second = new LineSeries<double, double>(CreateData(value => 50 + (40 * Math.Cos(value * 0.9))), x, lower) { Color = new Rgba8(240, 130, 90, 255) };
        chart.AddSeries(first);
        chart.AddSeries(second);
        return (chart, [first, second]);
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, series) = CreateChart();
        chart.Render(device, target);
        device.WaitForIdle();

        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    private static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max, string? title) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
        Title = title,
    };

    private static DataSource<double, double> CreateData(Func<double, double> function)
    {
        var source = new DataSource<double, double>(PointCount);
        for (var i = 0; i < PointCount; i++)
        {
            var x = i * 10.0 / (PointCount - 1);
            source.Append(x, function(x));
        }

        return source;
    }
}
