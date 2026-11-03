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
/// All six SDF marker shapes, including their borders.
/// </summary>
public sealed class ScatterMarkersScene : IScene
{
    private static readonly Rgba8[] _colors =
    [
        new(80, 170, 240, 255),
        new(240, 130, 90, 255),
        new(110, 210, 130, 255),
        new(220, 180, 70, 255),
        new(180, 120, 230, 255),
        new(80, 210, 205, 255),
    ];

    /// <inheritdoc />
    public string Name => "ScatterMarkers";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 540);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = CreateAxis("x", AxisPosition.Bottom, 0, 10);
        var y = CreateAxis("y", AxisPosition.Left, 0, 7);
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        var series = new ScatterSeries<double, double>[6];
        for (var shape = 0; shape < series.Length; shape++)
        {
            var source = new DataSource<double, double>(9);
            for (var point = 0; point < 9; point++)
            {
                var px = 1 + point;
                source.Append(px, 6 - shape + (Math.Sin(px * 1.7) * 0.16));
            }

            series[shape] = new ScatterSeries<double, double>(source, x, y)
            {
                Shape = (MarkerShape)shape,
                Color = _colors[shape],
                Size = 16,
                BorderColor = new Rgba8(10, 15, 24, 255),
                BorderThickness = shape == (int)MarkerShape.Circle ? 2 : 0,
            };
            chart.AddSeries(series[shape]);
        }

        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    private static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
    };
}

/// <summary>
/// A scatter field with an individual color and size for every point.
/// </summary>
public sealed class ScatterPerPointColorScene : IScene
{
    /// <inheritdoc />
    public string Name => "ScatterPerPointColor";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 540);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        const int columns = 18;
        const int rows = 10;
        var source = new DataSource<double, double>(columns * rows);
        var colors = new Rgba8[columns * rows];
        var sizes = new float[columns * rows];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = (row * columns) + column;
                source.Append(column + 1, row + 1 + (Math.Sin(column * 0.9 + row) * 0.12));
                var hue = column / (float)(columns - 1);
                colors[index] = HeatColor(hue, row / (float)(rows - 1));
                sizes[index] = 6 + (row * 1.1f);
            }
        }

        var x = CreateAxis("x", AxisPosition.Bottom, 0, columns + 1);
        var y = CreateAxis("y", AxisPosition.Left, 0, rows + 1);
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default) { Title = Name };
        using var series = new ScatterSeries<double, double>(source, x, y)
        {
            Shape = MarkerShape.Circle,
            PointColors = colors,
            PointSizes = sizes,
            BorderThickness = 0,
        };
        chart.AddSeries(series);
        chart.Render(device, target);
        device.WaitForIdle();
    }

    private static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
    };

    private static Rgba8 HeatColor(float x, float y)
    {
        var red = (byte)MathF.Round(50 + (205 * x));
        var green = (byte)MathF.Round(210 - (130 * MathF.Abs((2 * x) - 1)));
        var blue = (byte)MathF.Round(235 - (180 * x));
        var alpha = (byte)MathF.Round(150 + (105 * y));
        return new Rgba8(red, green, blue, alpha);
    }
}
