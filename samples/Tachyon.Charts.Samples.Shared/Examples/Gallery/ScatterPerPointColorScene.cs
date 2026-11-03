using System.Globalization;
using Tachyon.Charts.Core.Axes;
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
