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

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// One strip per colormap, a custom map with a missing part, and a strip with out-of-range colors.
/// The color bar on the right shows Turbo.
/// </summary>
public sealed class ColormapsScene : IScene
{
    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Colormaps";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 100), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 8), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Turbo, VisibleRange = new DoubleRange(0, 100) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var strips = new StripSeries(x, y);
        chart.AddSeries(strips);
        chart.Render(device, target);
        device.WaitForIdle();
    }

    private sealed class StripSeries(AxisBase x, AxisBase y) : ChartSeries(x, y), IDisposable
    {
        private const int Width = 256;

        private readonly List<(ColormapTexture Table, Colormap Map)> _tables = [];
        private IRenderDevice _device = null!;
        private IGpuTexture? _values;

        protected override long Version => 0;

        public void Dispose()
        {
            _values?.Dispose();
            foreach (var (table, _) in _tables)
            {
                table.Dispose();
            }
        }

        protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
        {
            // The tables are written while drawing is prepared, so the frame only binds them.
            _device = device;
            if (_values is not null)
            {
                return;
            }

            var values = new float[Width];
            for (var i = 0; i < Width; i++)
            {
                values[i] = i is >= 120 and < 136 ? float.NaN : i / (float)(Width - 1);
            }

            _values = device.CreateTexture(new TextureDesc(new PixelSize(Width, 1), TextureFormat.R32Float));
            _values.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
            Colormap custom = Colormap.FromColors("Custom", [new Rgba8(20, 40, 120, 255), new Rgba8(240, 240, 240, 255), new Rgba8(170, 30, 30, 255)]);
            foreach (var map in Colormaps.All.Append(custom).Append(Colormaps.Viridis))
            {
                _tables.Add((new ColormapTexture(), map));
                _tables[^1].Table.Update(device, map);
            }
        }

        protected override void Draw(ICommandList commands, in SeriesFrame frame)
        {
            for (var i = 0; i < _tables.Count; i++)
            {
                var (table, map) = _tables[i];
                var style = i == _tables.Count - 1
                    ? new RasterStyle(0.25f, 0.75f) { BelowColor = new Rgba8(255, 255, 255, 255), AboveColor = new Rgba8(255, 0, 255, 255), MissingColor = new Rgba8(128, 128, 128, 255) }
                    : new RasterStyle(0, 1) { MissingColor = new Rgba8(128, 128, 128, 255) };
                var top = (float)frame.Y.ToPixel(8 - i - 0.1);
                var bottom = (float)frame.Y.ToPixel(8 - i - 0.9);
                commands.DrawRaster(_values!, table.Update(_device, map), new PixelRect(frame.Bounds.X, top, frame.Bounds.Width, bottom - top), style);
            }
        }

    }
}

/// <summary>
/// A field of 4096 by 4096 values, with a missing rectangle.
/// </summary>
public sealed class Heatmap4kScene : IScene
{
    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    private const int Length = 4096;

    /// <inheritdoc />
    public string Name => "Heatmap4k";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 700);

    /// <summary>Creates the grid.</summary>
    /// <param name="withGap">Whether a part of the values is missing, to test how gaps look.</param>
    /// <returns>The grid.</returns>
    public static HeatmapData CreateData(bool withGap = true)
    {
        var values = new float[Length * Length];
        Parallel.For(0, Length, row =>
        {
            var y = row * 8.0 / Length;
            for (var column = 0; column < Length; column++)
            {
                var x = column * 8.0 / Length;
                var missing = withGap && x is > 5 and < 6.5 && y is > 1 and < 2.5;
                values[(row * Length) + column] = missing ? float.NaN : (float)(Math.Sin(x * x * 0.6) * Math.Cos(y * 2.1) + Math.Exp(-(((x - 3) * (x - 3)) + ((y - 5) * (y - 5)))));
            }
        });

        var data = new HeatmapData(Length, Length);
        data.SetValues(values);
        return data;
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 8), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 8), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Inferno, MissingColor = new Rgba8(90, 200, 90, 255) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var heatmap = new HeatmapSeries(CreateData(), x, y, color) { XStep = 8.0 / Length, YStep = 8.0 / Length };
        chart.AddSeries(heatmap);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>
/// Cells of different sizes on a logarithmic Y axis, colored by the logarithm of their values.
/// </summary>
public sealed class HeatmapNonUniformScene : IScene
{
    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "HeatmapNonUniform";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
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
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>
/// A ring of 256 rows after 400 appended rows: a tone that sweeps up and down, and a pause of missing
/// rows. The ring has wrapped, and the rows must still run without a seam.
/// </summary>
public sealed class SpectrogramScene : IScene
{
    private const int Columns = 256;
    private const int Rows = 256;
    private const int Appended = 400;

    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Spectrogram";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

    /// <summary>Appends a row of the signal.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="index">The number of the row.</param>
    /// <param name="row">A buffer for one row.</param>
    /// <param name="withPauses">Whether the signal has pauses of missing rows.</param>
    public static void AppendSignal(SpectrogramData data, long index, Span<float> row, bool withPauses = true)
    {
        var center = (row.Length / 2.0) + ((row.Length / 2.5) * Math.Sin(index * 0.03));
        for (var i = 0; i < row.Length; i++)
        {
            var distance = (i - center) / 6;
            var overtone = (i - (center / 2)) / 10;
            row[i] = withPauses && index % 100 is >= 60 and < 66 ? float.NaN : (float)(Math.Exp(-distance * distance) + (0.4 * Math.Exp(-overtone * overtone)) + (0.05 * Math.Sin(i * 0.9)));
        }

        data.AppendRow(row);
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, Columns), LabelFormat = _labels, Title = "Frequency" };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(-Rows, 0), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Plasma, VisibleRange = new DoubleRange(-0.1, 1.1), MissingColor = new Rgba8(40, 40, 40, 255) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        var data = new SpectrogramData(Columns, Rows);
        using var series = new SpectrogramSeries(data, x, y, color);
        chart.AddSeries(series);

        // The first half arrives before the first frame, the rest row by row.
        var row = new float[Columns];
        for (var i = 0; i < Appended; i++)
        {
            AppendSignal(data, i, row);
            if (i >= Appended / 2)
            {
                chart.Render(device, target);
            }
        }

        device.WaitForIdle();
    }
}

/// <summary>
/// A field with hills, a saddle and a missing corner: filled bands with isolines.
/// </summary>
public sealed class ContourScene : IScene
{
    private const int Length = 128;

    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Contour";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 600);

    /// <summary>Creates the field.</summary>
    /// <param name="length">The number of samples per side.</param>
    /// <param name="withGap">Whether a part of the values is missing, to test how gaps look.</param>
    /// <returns>The grid.</returns>
    public static HeatmapData CreateField(int length, bool withGap = true)
    {
        var values = new float[length * length];
        for (var row = 0; row < length; row++)
        {
            var y = row * 6.0 / length;
            for (var column = 0; column < length; column++)
            {
                var x = column * 6.0 / length;
                var missing = withGap && x > 4.8 && y > 4.8;
                values[(row * length) + column] = missing ? float.NaN : (float)(Math.Sin(x) * Math.Cos(y * 1.3) + (0.6 * Math.Exp(-(((x - 2) * (x - 2)) + ((y - 4) * (y - 4))))));
            }
        }

        var data = new HeatmapData(length, length);
        data.SetValues(values);
        return data;
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 6), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 6), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Viridis };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var contour = new ContourSeries(CreateField(Length), x, y, color) { IsFilled = true, XStep = 6.0 / Length, YStep = 6.0 / Length, Levels = [-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1, 1.25] };
        chart.AddSeries(contour);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}

/// <summary>
/// 50 million points in clusters. Their number switches the series to a density map by itself.
/// </summary>
public sealed class PointCloud50MScene : IScene
{
    private const int PointCount = 50_000_000;

    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "PointCloud50M";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 600);

    /// <summary>Creates a cloud of points.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The points.</returns>
    public static DataSource<float, float> CreateCloud(int count)
    {
        const int Parts = 16;
        var x = new float[count];
        var y = new float[count];
        Parallel.For(0, Parts, part =>
        {
            var random = new Random(part + 1);
            var end = (int)((part + 1L) * count / Parts);
            for (var i = (int)((long)part * count / Parts); i < end; i++)
            {
                var cluster = random.Next(4);
                var (centerX, centerY, spread) = cluster switch
                {
                    0 => (3f, 3f, 0.9f),
                    1 => (7f, 6.5f, 0.6f),
                    2 => (2.5f, 7.5f, 0.35f),
                    _ => (5f, 5f, 2.2f),
                };

                // Twelve uniform values make a good bell curve, cheaper than logarithms.
                x[i] = centerX + (Bell(random) * spread);
                y[i] = centerY + (Bell(random) * spread);
            }
        });

        var cloud = new DataSource<float, float>(count);
        cloud.Append(x, y);
        return cloud;
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 10), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 10), LabelFormat = _labels };
        var color = new ColorAxis("color") { Colormap = Colormaps.Inferno, IsLogarithmic = true, BelowColor = default(Rgba8) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var scatter = new ScatterSeries<float, float>(CreateCloud(PointCount), x, y) { DensityAxis = color };
        chart.AddSeries(scatter);

        // The range of the colors follows the first count.
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }

    private static float Bell(Random random)
    {
        var sum = -6f;
        for (var i = 0; i < 12; i++)
        {
            sum += random.NextSingle();
        }

        return sum;
    }
}
