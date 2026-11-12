using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ContourData;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A field with hills, a saddle and a missing corner: filled bands with isolines.
/// </summary>
public sealed class ContourScene : IScene, IChartScene
{
    private const int Length = 128;

    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Contour";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 600);

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

    /// <inheritdoc />
    public ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 6), LabelFormat = _labels };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(0, 6), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Viridis };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        using var contour = new ContourSeries(CreateField(Length), x, y, color) { IsFilled = true, XStep = 6.0 / Length, YStep = 6.0 / Length, Levels = [-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1, 1.25] };
        chart.AddSeries(contour);
        disposables = [];
        return chart;
    }
}
