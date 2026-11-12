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
/// All six SDF marker shapes, including their borders.
/// </summary>
public sealed class ScatterMarkersScene : IScene, IChartScene
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
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
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

        disposables = [.. series];
        return chart;
    }

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
