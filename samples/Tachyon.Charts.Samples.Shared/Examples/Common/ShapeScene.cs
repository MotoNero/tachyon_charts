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
/// Common ground of the scenes of filled shapes.
/// </summary>
public abstract class ShapeScene : IScene, IChartScene
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

    /// <inheritdoc />
    ChartSurface IChartScene.CreateChart(out IReadOnlyList<IDisposable> disposables) => CreateChart(out disposables);

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
