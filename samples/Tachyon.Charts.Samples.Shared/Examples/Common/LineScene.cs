using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A chart with line series in one scene.
/// </summary>
public abstract class LineScene : IScene, IChartScene
{
    private const int PointCount = 240;

    /// <summary>
    /// Gets the first line color.
    /// </summary>
    protected static Rgba8 Blue { get; } = new(80, 170, 240, 255);

    /// <summary>
    /// Gets the second line color.
    /// </summary>
    protected static Rgba8 Orange { get; } = new(240, 130, 90, 255);

    /// <summary>
    /// Gets the third line color.
    /// </summary>
    protected static Rgba8 Green { get; } = new(110, 210, 130, 255);

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <inheritdoc />
    public virtual void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, series) = CreateChart();
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc />
    ChartSurface IChartScene.CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        (var chart, disposables) = CreateChart();
        return chart;
    }

    /// <summary>
    /// Creates the chart of this scene.
    /// </summary>
    /// <returns>The chart and its series, which hold GPU buffers and must be disposed.</returns>
    public (ChartSurface Chart, IReadOnlyList<IDisposable> Series) CreateChart()
    {
        var x = CreateAxis("x", AxisPosition.Bottom, 0, 10, "Time");
        var y = CreateAxis("y", AxisPosition.Left, -1.5, 1.5, null);
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        return (chart, AddSeries(chart, x, y));
    }

    /// <summary>
    /// Creates an axis with a fixed range and culture, so that snapshots are reproducible.
    /// </summary>
    protected static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max, string? title) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
        Title = title,
    };

    /// <summary>
    /// Creates a data source from a function over the X range 0 to 10.
    /// </summary>
    protected static DataSource<double, double> CreateData(Func<double, double> function, int count = PointCount)
    {
        var source = new DataSource<double, double>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i * 10.0 / (count - 1);
            source.Append(x, function(x));
        }

        return source;
    }

    /// <summary>
    /// Adds the series of this scene.
    /// </summary>
    protected abstract IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y);
}
