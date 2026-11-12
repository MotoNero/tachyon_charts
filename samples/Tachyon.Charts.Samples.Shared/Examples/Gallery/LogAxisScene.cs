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
/// Three panes on logarithmic axes: a decay with a gap at values below zero, a clamped series with
/// markers, and a signal of both signs on a symmetric scale.
/// </summary>
public sealed class LogAxisScene : IScene, IChartScene
{
    private const int PointCount = 200;

    /// <inheritdoc />
    public string Name => "LogAxis";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 600);

    /// <inheritdoc />
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
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

        var first = new LineSeries<double, double>(decay, x, hide) { Color = new Rgba8(80, 170, 240, 255), Thickness = 2 };
        var second = new ScatterSeries<double, double>(steps, x, clamp) { Color = new Rgba8(240, 130, 90, 255), Size = 4 };
        var third = new LineSeries<double, double>(both, x, sym) { Color = new Rgba8(110, 210, 130, 255), Thickness = 2 };
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.AddSeries(third);
        disposables = [first, second, third];
        return chart;
    }

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
