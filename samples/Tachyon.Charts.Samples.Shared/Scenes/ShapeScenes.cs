using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// Common ground of the scenes of filled shapes.
/// </summary>
public abstract class ShapeScene : IScene
{
    /// <summary>The label format, fixed so that images do not depend on the system language.</summary>
    protected static readonly NumericLabelFormat Labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public virtual PixelSize Size { get; } = new(800, 500);

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

    /// <summary>Creates the chart.</summary>
    /// <param name="disposables">Receives what ends with the scene.</param>
    /// <returns>The chart.</returns>
    protected abstract ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables);

    /// <summary>Creates a chart with numeric axes.</summary>
    /// <param name="x">The horizontal range.</param>
    /// <param name="y">The vertical range.</param>
    /// <param name="xAxis">Receives the horizontal axis.</param>
    /// <param name="yAxis">Receives the vertical axis.</param>
    /// <returns>The chart.</returns>
    protected ChartSurface CreateAxes(DoubleRange x, DoubleRange y, out NumericAxis xAxis, out NumericAxis yAxis)
    {
        xAxis = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = x, LabelFormat = Labels };
        yAxis = new NumericAxis("y", AxisPosition.Left) { VisibleRange = y, LabelFormat = Labels };
        return new ChartSurface([xAxis], [new LayoutPane([yAxis])], SkiaTextEngine.Default) { Title = Name };
    }

    /// <summary>Samples a curve.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The points.</returns>
    protected static DataSource<double, double> Sample(Func<double, double> curve, int count = 300, double length = 10)
    {
        var source = new DataSource<double, double>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i * length / (count - 1);
            source.Append(x, curve(x));
        }

        return source;
    }
}

/// <summary>A resonant circuit over frequency in a Smith chart, with a marker at its resonance.</summary>
public sealed class SmithScene : IScene
{
    /// <inheritdoc />
    public string Name => "Smith";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(700, 700);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, x, y, _) = SmithChart.Create(SkiaTextEngine.Default);
        chart.Title = Name;
        const int Count = 200;
        var resistance = new double[Count];
        var reactance = new double[Count];
        for (var i = 0; i < Count; i++)
        {
            // A series circuit: 20 ohms, reactance from capacitive to inductive.
            var detuning = (i - (Count / 2.0)) / (Count / 2.0);
            resistance[i] = 20 + (10 * detuning * detuning);
            reactance[i] = 120 * detuning;
        }

        using var series = new SmithSeries(resistance, reactance, x, y);
        chart.AddSeries(series);
        chart.AddOverlay(new SmithMarkerOverlay(series, Count / 2));
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>Four channels with fixed waveforms on an oscilloscope screen.</summary>
public sealed class OscilloscopeScene : IScene
{
    /// <inheritdoc />
    public string Name => "Oscilloscope";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <summary>Fills a channel with a test signal.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="kind">The signal: 0 sine, 1 square, 2 ringing pulse, 3 triangle.</param>
    /// <param name="phase">The shift of the signal in radians.</param>
    public static void Fill(ScopeChannel channel, int kind, double phase = 0)
    {
        ArgumentNullException.ThrowIfNull(channel);
        const int Count = 5000;
        const double Duration = 0.01;
        var time = new double[Count];
        var volts = new float[Count];
        for (var i = 0; i < Count; i++)
        {
            var t = (i * Duration / Count) - (Duration / 2);
            time[i] = t;
            volts[i] = (float)Signal(kind, t, phase);
        }

        channel.SetWaveform(time, volts);
    }

    /// <summary>Calculates a test signal.</summary>
    /// <param name="kind">The signal: 0 sine, 1 square, 2 ringing pulse, 3 triangle.</param>
    /// <param name="time">The time in seconds.</param>
    /// <param name="phase">The shift in radians.</param>
    /// <returns>The value in volts.</returns>
    public static double Signal(int kind, double time, double phase)
    {
        var angle = (2 * Math.PI * 1000 * time) + phase;
        return kind switch
        {
            0 => 1.5 * Math.Sin(angle),
            1 => Math.Sin(angle) >= 0 ? 1 : -1,
            2 => 2 * Math.Exp(-Math.Abs(((time + 0.01) * 1000 % 1) * 6)) * Math.Cos(angle * 12),
            _ => 2 / Math.PI * Math.Asin(Math.Sin(angle)),
        };
    }

    /// <summary>Creates the display of this scene.</summary>
    /// <returns>The display with four channels.</returns>
    public static ScopeDisplay CreateDisplay()
    {
        var display = new ScopeDisplay { TimePerDivision = 5e-4, TriggerLevel = 0.5 };
        for (var i = 0; i < 4; i++)
        {
            var channel = display.AddChannel();
            channel.VoltsPerDivision = i == 0 ? 1 : 0.5;
            channel.Position = 3 - (i * 2);
            Fill(channel, i);
        }

        return display;
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = new ChartBuilder().Oscilloscope(CreateDisplay()).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}

/// <summary>A noisy, jittering sine after many acquisitions, colored by how often each pixel was hit.</summary>
public sealed class ColorGradeScene : IScene
{
    /// <inheritdoc />
    public string Name => "ColorGrade";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var display = new ScopeDisplay { TimePerDivision = 5e-4, Persistence = ScopePersistence.ColorGrade };
        var channel = display.AddChannel();
        var chart = new ChartBuilder().Oscilloscope(display).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        var random = new Random(1);
        var time = new double[3000];
        var volts = new float[3000];
        chart.Render(device, target);
        for (var pass = 0; pass < 120; pass++)
        {
            var jitter = (random.NextDouble() - 0.5) * 0.3;
            for (var i = 0; i < time.Length; i++)
            {
                time[i] = (i - 1500) * 3e-6;
                var amplitude = 2 + ((pass % 7) * 0.05);
                volts[i] = (float)((amplitude * Math.Sin((2 * Math.PI * 1000 * time[i]) + jitter)) + ((random.NextDouble() - 0.5) * 0.15));
            }

            channel.SetWaveform(time, volts);
            chart.Render(device, target);
        }

        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}

/// <summary>Three carriers over noise with their density, the highest values and a mask.</summary>
public sealed class SpectrumScene : IScene
{
    /// <inheritdoc />
    public string Name => "Spectrum";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <summary>Delivers one spectrum.</summary>
    /// <param name="display">The display.</param>
    /// <param name="frequencies">The frequencies.</param>
    /// <param name="levels">The levels, overwritten.</param>
    /// <param name="random">The noise.</param>
    public static void Deliver(SpectrumDisplay display, double[] frequencies, float[] levels, Random random)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(random);
        for (var i = 0; i < levels.Length; i++)
        {
            var level = -70 + (6 * random.NextDouble());
            foreach (var (center, height) in new[] { (2e6, -12.0), (4.5e6, -30.0), (7e6, -22.0) })
            {
                var distance = (frequencies[i] - center) / 8e4;
                level = Math.Max(level, height - (distance * distance * 6) + random.NextDouble());
            }

            levels[i] = (float)level;
        }

        display.SetTrace(frequencies, levels);
    }

    /// <summary>Creates the display of this scene.</summary>
    /// <param name="passing">Whether the signal stays below the mask. The test scene violates it.</param>
    /// <returns>The display.</returns>
    public static SpectrumDisplay CreateDisplay(bool passing = false)
    {
        var display = new SpectrumDisplay { Mask = new FrequencyMask([0, 3e6, 3.2e6, 1e7], passing ? [-5, -5, -9, -9] : [-5, -5, -25, -25]), ShowsDensity = true };
        display.SetView(0, 1e7, 0);
        return display;
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var display = CreateDisplay();
        var chart = new ChartBuilder().Spectrum(display).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        var frequencies = Enumerable.Range(0, 1500).Select(i => i * 1e7 / 1499).ToArray();
        var levels = new float[frequencies.Length];
        var random = new Random(1);
        chart.Render(device, target);
        for (var pass = 0; pass < 60; pass++)
        {
            Deliver(display, frequencies, levels, random);
            chart.Render(device, target);
        }

        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}

/// <summary>A filter measured over frequency: two traces, markers and limits, one trace fails.</summary>
public sealed class NetworkScene : IScene
{
    /// <inheritdoc />
    public string Name => "Network";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var display = Examples.Common.NetworkData.CreateDisplay(passing: false);
        var chart = new ChartBuilder().NetworkAnalyzer(display, 1e9, 2e9, -70, 5).Theme(BuiltInThemes.Instrument).ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }

}

/// <summary>Mixtures of three parts in a ternary diagram.</summary>
public sealed class TernaryScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Ternary";

    /// <inheritdoc />
    public override PixelSize Size { get; } = new(700, 640);

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 1), new DoubleRange(0, 1), out var x, out var y);
        x.IsHidden = true;
        y.IsHidden = true;
        var a = new double[60];
        var b = new double[60];
        var c = new double[60];
        for (var i = 0; i < a.Length; i++)
        {
            var t = i / 59.0;
            a[i] = 0.2 + (0.5 * t);
            b[i] = 0.6 - (0.4 * t) + (0.1 * Math.Sin(i * 0.9));
            c[i] = 0.2 + (0.1 * Math.Cos(i * 1.7));
        }

        var points = new TernarySeries(a, b, c, x, y) { Color = new Rgba8(240, 130, 90, 255) };
        chart.AddSeries(points);
        chart.AddOverlay(new TernaryGridOverlay("A", "B", "C"));
        disposables = [points];
        return chart;
    }
}

/// <summary>Two thousand rows over six measures, with one correlated pair.</summary>
public sealed class ParallelCoordinatesScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "ParallelCoordinates";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 1), new DoubleRange(0, 1), out var x, out var y);
        x.IsHidden = true;
        y.IsHidden = true;
        const int Rows = 2000;
        var columns = new double[6][];
        for (var column = 0; column < columns.Length; column++)
        {
            columns[column] = new double[Rows];
            for (var row = 0; row < Rows; row++)
            {
                var noise = ((row * 2654435761L * (column + 3)) % 1000) / 1000.0;
                columns[column][row] = column == 1 ? 100 - (columns[0][row] * 0.8) - (noise * 20) : noise * 100 * (column + 1);
            }
        }

        var rows = new ParallelCoordinatesSeries(columns, x, y) { Color = new Rgba8(80, 170, 240, 50) };
        chart.AddSeries(rows);
        chart.AddOverlay(new ParallelAxesOverlay(rows, ["Volt", "Strom", "Temp", "Druck", "Hz", "dB"]));
        disposables = [rows];
        return chart;
    }
}

/// <summary>Measured values with their uncertainty, one point without a value.</summary>
public sealed class ErrorBarsScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "ErrorBars";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(-0.5, 12.5), new DoubleRange(0, 4), out var x, out var y);
        var points = Sample(t => t is > 5.5 and < 6.5 ? double.NaN : 2 + Math.Sin(t * 0.8), 13, 12);
        var errors = Enumerable.Range(0, 13).Select(i => 0.15 + (0.05 * (i % 5))).ToArray();
        chart.AddSeries(new ErrorBarSeries(points, errors, errors, x, y) { Color = new Rgba8(80, 170, 240, 255), Thickness = 1.5 });
        disposables = [];
        return chart;
    }
}

/// <summary>A spectrum as stems with a raised baseline.</summary>
public sealed class StemScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Stem";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(-1, 41), new DoubleRange(-0.5, 3), out var x, out var y);
        var points = Sample(t => 2.5 * Math.Exp(-0.1 * t) * Math.Abs(Math.Cos(t * 0.6)), 41, 40);
        chart.AddSeries(new StemSeries(points, x, y) { Color = new Rgba8(240, 130, 90, 255), HeadSize = 6 });
        disposables = [];
        return chart;
    }
}

/// <summary>The same points as a smooth curve and as straight lines, with a gap.</summary>
public sealed class SplineScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Spline";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-0.5, 3.5), out var x, out var y);
        var points = Sample(t => t is > 6.5 and < 7.5 ? double.NaN : 1.5 + (1.4 * Math.Sign(Math.Sin(t * 1.3))) * Math.Abs(Math.Sin(t * 1.3)), 13, 10);
        var straight = new LineSeries<double, double>(points, x, y) { Color = new Rgba8(140, 150, 170, 255), Thickness = 1 };
        var smooth = new LineSeries<double, double>(points, x, y) { Color = new Rgba8(80, 200, 160, 255), Thickness = 2, IsSpline = true };
        chart.AddSeries(straight);
        chart.AddSeries(smooth);
        disposables = [straight, smooth];
        return chart;
    }
}

/// <summary>A bubble chart: position, size and color show one value each.</summary>
public sealed class BubbleScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Bubble";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(0, 10), out var x, out var y);
        var points = new DataSource<double, double>(40);
        var sizes = new float[40];
        for (var i = 0; i < sizes.Length; i++)
        {
            points.Append(0.5 + (9 * ((i * 0.618) % 1)), 0.5 + (9 * ((i * 0.7549) % 1)));
            sizes[i] = 6 + (i % 7 * 6);
        }

        var bubbles = new ScatterSeries<double, double>(points, x, y) { Color = new Rgba8(80, 170, 240, 170), BorderThickness = 0, PointSizes = sizes };
        chart.AddSeries(bubbles);
        disposables = [bubbles];
        return chart;
    }
}

/// <summary>An area fading out towards the baseline, with a gap of missing values, and its line.</summary>
public sealed class AreaScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Area";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1, 3), out var x, out var y);
        var curve = Sample(t => t is > 6 and < 6.8 ? double.NaN : 1.2 + Math.Sin(t) + (0.4 * Math.Sin(t * 5)));
        chart.AddSeries(new AreaSeries(curve, x, y) { Fill = new PositionGradientFill(new Rgba8(80, 170, 240, 230), new Rgba8(80, 170, 240, 20)) });
        var line = new LineSeries<double, double>(curve, x, y) { Color = new Rgba8(170, 220, 255, 255), Thickness = 2 };
        chart.AddSeries(line);
        disposables = [line];
        return chart;
    }
}

/// <summary>Three layers on top of each other. The middle one has a gap, and the upper one keeps its place.</summary>
public sealed class AreaStackedScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "AreaStacked";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(0, 9), out var x, out var y);
        IReadOnlyList<IDataSource<double, double>> layers =
        [
            Sample(t => 2 + Math.Sin(t)),
            Sample(t => t is > 4 and < 5 ? double.NaN : 2 + Math.Cos(t * 0.7)),
            Sample(t => 1.5 + (0.5 * Math.Sin(t * 2))),
        ];
        chart.AddSeries(new StackedAreaSeries(layers, x, y)
        {
            Fills = [new SolidFill(new Rgba8(76, 201, 240, 64)), new SolidFill(new Rgba8(247, 37, 133, 64)), new SolidFill(new Rgba8(124, 252, 154, 64))],
        });
        disposables = [];
        return chart;
    }
}

/// <summary>A band between two curves, green where the first one is on top, red elsewhere.</summary>
public sealed class BandScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Band";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1.5, 1.5), out var x, out var y);
        var first = Sample(Math.Sin);
        var second = Sample(t => 0.8 * Math.Cos(t * 0.8));
        chart.AddSeries(new BandSeries(first, second, x, y));
        var a = new LineSeries<double, double>(first, x, y) { Color = new Rgba8(110, 210, 130, 255), Thickness = 1.5 };
        var b = new LineSeries<double, double>(second, x, y) { Color = new Rgba8(240, 100, 100, 255), Thickness = 1.5 };
        chart.AddSeries(a);
        chart.AddSeries(b);
        disposables = [a, b];
        return chart;
    }
}

/// <summary>Bars in the ways they can share a place. The scene exists once per layout.</summary>
public sealed class BarsScene(string name, BarLayout layout, bool isHorizontal) : ShapeScene
{
    private static readonly double[][] _rows =
    [
        [3, 5, 2, 6, 4, 7],
        [2, 1, 4, 3, 5, 2],
        [1, 3, double.NaN, 2, 2, 3],
    ];

    /// <inheritdoc />
    public override string Name => name;

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var values = layout switch
        {
            BarLayout.Percent => new DoubleRange(0, 100),
            BarLayout.Stacked => new DoubleRange(0, 14),
            _ => new DoubleRange(0, 8),
        };
        var places = new DoubleRange(-0.5, 5.5);
        var chart = CreateAxes(isHorizontal ? values : places, isHorizontal ? places : values, out var x, out var y);
        chart.AddSeries(new BarSeries(_rows, x, y) { Layout = layout, IsHorizontal = isHorizontal });
        disposables = [];
        return chart;
    }
}

/// <summary>A histogram of a skewed sample, with the bins of Freedman and Diaconis.</summary>
public sealed class HistogramScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "Histogram";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var random = new Random(3);
        var values = new double[5000];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = -Math.Log(1 - random.NextDouble()) * 2;
        }

        var histogram = Histogram.Compute(values, BinningRule.FreedmanDiaconis);
        var chart = CreateAxes(new DoubleRange(0, 12), new DoubleRange(0, 1200), out var x, out var y);
        chart.AddSeries(BarSeries.FromHistogram(histogram, x, y));
        disposables = [];
        return chart;
    }
}

/// <summary>Named places with rotated labels above a pane with too many names, which get thinned out.</summary>
public sealed class CategoryAxisScene : IScene
{
    private static readonly string[] _months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    /// <inheritdoc />
    public string Name => "CategoryAxis";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 400);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new CategoryAxis("x", AxisPosition.Bottom, _months) { LabelRotation = -45 };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 10), LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture } };
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        chart.AddSeries(new BarSeries([[3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4]], x, y));
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>Far more names than room: only every n-th one stays, without any overlap.</summary>
public sealed class CategoryThinningScene : IScene
{
    /// <inheritdoc />
    public string Name => "CategoryThinning";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 300);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var names = Enumerable.Range(0, 200).Select(i => $"Sensor {i}").ToArray();
        var x = new CategoryAxis("x", AxisPosition.Bottom, names);
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 2), LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture } };
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        chart.AddSeries(new BarSeries([[.. Enumerable.Range(0, 200).Select(i => 1 + Math.Sin(i * 0.1))]], x, y));
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>Three samples as box plots with outliers, the same samples as violins in a second pane.</summary>
public sealed class BoxViolinScene : IScene
{
    /// <inheritdoc />
    public string Name => "BoxViolin";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
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
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>A spiral line, a rose as an area and markers on a polar grid.</summary>
public sealed class PolarScene : IScene
{
    /// <inheritdoc />
    public string Name => "Polar";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(600, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, angle, radial) = PolarChart.Create(SkiaTextEngine.Default, new DoubleRange(0, 10));
        chart.Title = Name;
        var degrees = Enumerable.Range(0, 361).Select(i => (double)i).ToArray();
        using var rose = new PolarSeries(degrees, [.. degrees.Select(d => 5 + (3 * Math.Sin(d * Math.PI / 45)))], angle, radial) { Parts = PolarParts.Area | PolarParts.Line, IsClosed = true };
        using var spiral = new PolarSeries(degrees, [.. degrees.Select(d => d / 40)], angle, radial) { Color = new Rgba8(255, 183, 3, 255), Thickness = 2 };
        double[] pointAngles = [30, 110, 200, 300];
        using var points = new PolarSeries(pointAngles, [9, 7, 8.5, 6], angle, radial) { Parts = PolarParts.Markers, Color = new Rgba8(247, 37, 133, 255) };
        chart.AddSeries(rose);
        chart.AddSeries(spiral);
        chart.AddSeries(points);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>Two products compared on six qualities.</summary>
public sealed class RadarScene : IScene
{
    /// <inheritdoc />
    public string Name => "Radar";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(600, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        string[] names = ["Tempo", "Preis", "Qualität", "Service", "Design", "Reichweite"];
        var (chart, angle, radial) = PolarChart.Create(SkiaTextEngine.Default, new DoubleRange(0, 100), names);
        chart.Title = Name;
        using var first = PolarSeries.Radar([80, 60, 90, 70, 50, 75], angle, radial);
        using var second = PolarSeries.Radar([50, 90, 60, 85, 80, 40], angle, radial);
        second.Color = new Rgba8(247, 37, 133, 255);
        second.Fill = new SolidFill(new Rgba8(247, 37, 133, 60));
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>Bright lines with a glow on a dark plot. Skia shows a simplified glow.</summary>
public sealed class GlowNeonScene : ShapeScene
{
    /// <inheritdoc />
    public override string Name => "GlowNeon";

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1.5, 1.5), out var x, out var y);
        var first = new LineSeries<double, double>(Sample(Math.Sin), x, y) { Color = new Rgba8(76, 201, 240, 255), Thickness = 2, GlowRadius = 10, GlowIntensity = 0.8 };
        var second = new LineSeries<double, double>(Sample(t => Math.Cos(t * 1.3) * 0.8), x, y) { Color = new Rgba8(247, 37, 133, 255), Thickness = 2, GlowRadius = 6, GlowIntensity = 0.6 };
        chart.AddSeries(first);
        chart.AddSeries(second);
        disposables = [first, second];
        return chart;
    }
}

/// <summary>An area colored by its values above round bars with soft shadows.</summary>
public sealed class GradientsScene : IScene
{
    /// <inheritdoc />
    public string Name => "Gradients";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var labels = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture };
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(-0.5, 9.5), LabelFormat = labels };
        var top = new NumericAxis("top", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 40), LabelFormat = labels };
        var bottom = new NumericAxis("bottom", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 10), LabelFormat = labels };
        var chart = new ChartSurface([x], [new LayoutPane([top]), new LayoutPane([bottom])], SkiaTextEngine.Default) { Title = Name };
        var curve = new DataSource<double, double>(200);
        for (var i = 0; i < 200; i++)
        {
            var t = -0.5 + (i * 10.0 / 199);
            curve.Append(t, 20 + (15 * Math.Sin(t * 0.9)));
        }

        chart.AddSeries(new AreaSeries(curve, x, top) { Fill = new ValueGradientFill(Colormaps.Turbo, new DoubleRange(0, 40)) });
        chart.AddSeries(new BarSeries([[3, 5, 7, 4, 8, 6, 9, 5, 4, 6]], x, bottom)
        {
            CornerRadius = 8,
            BarWidth = 0.6,
            Fills = [new PositionGradientFill(new Rgba8(255, 183, 3, 255), new Rgba8(247, 37, 133, 255))],
            ShadowColor = new Rgba8(0, 0, 0, 60),
        });
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>Lines and a band in a built-in theme, one scene per theme.</summary>
public sealed class ThemeGalleryScene(string name, Theme theme) : ShapeScene
{
    /// <inheritdoc />
    public override string Name => name;

    /// <inheritdoc />
    protected override ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var chart = CreateAxes(new DoubleRange(0, 10), new DoubleRange(-1.5, 1.5), out var x, out var y);
        chart.Theme = theme;
        var palette = theme.Palette!;
        var first = new LineSeries<double, double>(Sample(Math.Sin), x, y) { Color = palette[0].Value, Thickness = 2 };
        var second = new LineSeries<double, double>(Sample(t => Math.Cos(t * 1.3) * 0.8), x, y) { Color = palette[1].Value, Thickness = 2 };
        chart.AddSeries(first);
        chart.AddSeries(second);
        disposables = [first, second];
        return chart;
    }
}
