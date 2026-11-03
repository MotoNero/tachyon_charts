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
/// A chart with line series in one scene.
/// </summary>
public abstract class LineScene : IScene
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

/// <summary>
/// Solid lines of several widths and an unsorted curve.
/// </summary>
public sealed class LineBasicScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineBasic";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var thin = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 1.5 };
        var thick = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Cos(value * 1.3)), x, y) { Color = Orange, Thickness = 4 };

        var circle = new DataSource<double, double>();
        for (var i = 0; i <= 120; i++)
        {
            var angle = i * Math.Tau / 120;
            circle.Append(5 + (2 * Math.Cos(angle)), Math.Sin(angle));
        }

        var unsorted = new LineSeries<double, double>(circle, x, y) { Color = Green, Thickness = 2 };
        chart.AddSeries(thin);
        chart.AddSeries(thick);
        chart.AddSeries(unsorted);
        return [thin, thick, unsorted];
    }
}

/// <summary>
/// Dash patterns, along a curve and on a line that leaves the plot area.
/// </summary>
public sealed class LineDashScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineDash";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var dashes = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y)
        {
            Color = Blue,
            Thickness = 2,
            DashPattern = [10, 6],
        };
        var dots = new LineSeries<double, double>(CreateData(value => 0.5 * Math.Cos(value * 2)), x, y)
        {
            Color = Orange,
            Thickness = 3,
            DashPattern = [1, 7],
        };
        var mixed = new LineSeries<double, double>(CreateData(value => (value * 0.6) - 3.5), x, y)
        {
            Color = Green,
            Thickness = 2,
            DashPattern = [16, 4, 4, 4],
        };
        chart.AddSeries(dashes);
        chart.AddSeries(dots);
        chart.AddSeries(mixed);
        return [dashes, dots, mixed];
    }
}

/// <summary>
/// A step line over a smooth line through the same points.
/// </summary>
public sealed class LineStepScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineStep";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        static double Curve(double value) => Math.Sin(value * 1.1);

        var smooth = new LineSeries<double, double>(CreateData(Curve, 25), x, y) { Color = Blue, Thickness = 1 };
        var step = new LineSeries<double, double>(CreateData(Curve, 25), x, y) { Color = Orange, Thickness = 2, IsStep = true };
        chart.AddSeries(smooth);
        chart.AddSeries(step);
        return [smooth, step];
    }
}

/// <summary>
/// NaN and ±∞ as gaps, with a single point between two gaps.
/// </summary>
public sealed class LineGapsScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "LineGaps";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        static double Holes(double value) => value switch
        {
            > 2 and < 3 => double.NaN,
            > 4.5 and < 5.0 => double.PositiveInfinity,
            > 5.1 and < 5.6 => double.NegativeInfinity,
            > 7 and < 7.2 => double.NaN,
            _ => 0.9 * Math.Sin(value * 1.3),
        };

        var line = new LineSeries<double, double>(CreateData(Holes), x, y) { Color = Blue, Thickness = 2 };
        var step = new LineSeries<double, double>(CreateData(value => Holes(value) * -0.8, 60), x, y) { Color = Orange, Thickness = 2, IsStep = true };
        chart.AddSeries(line);
        chart.AddSeries(step);
        return [line, step];
    }
}

/// <summary>
/// Ring buffers after several wraps: few points drawn one by one, and many points reduced to buckets.
/// </summary>
public sealed class LineFifoWrapScene : LineScene
{
    private const int Laps = 3;

    /// <inheritdoc />
    public override string Name => "LineFifoWrap";

    /// <summary>
    /// Fills a ring, so that it holds the X range 0 to 10 after some laps.
    /// </summary>
    /// <param name="capacity">The size of the ring.</param>
    /// <param name="function">The values.</param>
    /// <returns>The ring.</returns>
    public static FifoDataSource<double, double> CreateRing(int capacity, Func<double, double> function)
    {
        var ring = new FifoDataSource<double, double>(capacity);
        var total = capacity * Laps;
        for (var i = 0; i < total; i++)
        {
            var x = (i - total + capacity) * 10.0 / (capacity - 1);
            ring.Append(x, function(x));
        }

        return ring;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var few = new LineSeries<double, double>(CreateRing(301, value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        var many = new LineSeries<double, double>(
            CreateRing(200_000, value => (0.3 * Math.Sin(value * 2)) - 0.6 + (Math.Sin(value * 4000) * 0.1)),
            x,
            y)
        {
            Color = Orange,
            Thickness = 1,
        };
        chart.AddSeries(few);
        chart.AddSeries(many);
        return [few, many];
    }
}

/// <summary>
/// The preview of a rubber band zoom over a line.
/// </summary>
public sealed class ZoomPreviewScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "ZoomPreview";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);
        chart.ZoomRectangle = new DipRect(200, 120, 320, 200);
        return [line];
    }
}

/// <summary>
/// Three panes on logarithmic axes: a decay with a gap at values below zero, a clamped series with
/// markers, and a signal of both signs on a symmetric scale.
/// </summary>
public sealed class LogAxisScene : IScene
{
    private const int PointCount = 200;

    /// <inheritdoc />
    public string Name => "LogAxis";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom)
        {
            VisibleRange = new DoubleRange(0, 10),
            LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
            Title = "Time",
        };
        var hide = new LogAxis("hide", AxisPosition.Left) { VisibleRange = new DoubleRange(0.01, 10000) };
        var clamp = new LogAxis("clamp", AxisPosition.Left) { Base = 2, NonPositive = NonPositiveValues.Clamp, Floor = 1, VisibleRange = new DoubleRange(1, 1024) };
        var sym = new LogAxis("sym", AxisPosition.Left) { NonPositive = NonPositiveValues.SymLog, VisibleRange = new DoubleRange(-1000, 1000) };
        var chart = new ChartSurface([x], [new LayoutPane([hide]), new LayoutPane([clamp]), new LayoutPane([sym])], SkiaTextEngine.Default) { Title = Name };

        var decay = new DataSource<double, double>(PointCount);
        var steps = new DataSource<double, double>(PointCount);
        var both = new DataSource<double, double>(PointCount);
        for (var i = 0; i < PointCount; i++)
        {
            var t = i * 10.0 / (PointCount - 1);
            decay.Append(t, t is > 4 and < 5 ? -1 : 5000 * Math.Exp(-t * 1.2));
            steps.Append(t, 64 * Math.Sin(t) * Math.Sin(t) * Math.Sign(Math.Sin(t * 0.7)));
            both.Append(t, 800 * Math.Sin(t * 1.5) * Math.Exp(-t * 0.4));
        }

        using var first = new LineSeries<double, double>(decay, x, hide) { Color = new Rgba8(80, 170, 240, 255), Thickness = 2 };
        using var second = new ScatterSeries<double, double>(steps, x, clamp) { Color = new Rgba8(240, 130, 90, 255), Size = 4 };
        using var third = new LineSeries<double, double>(both, x, sym) { Color = new Rgba8(110, 210, 130, 255), Thickness = 2 };
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.AddSeries(third);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
