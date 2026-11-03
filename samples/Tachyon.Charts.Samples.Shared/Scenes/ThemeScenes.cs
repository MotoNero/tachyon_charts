using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// The same chart rendered with one built-in theme.
/// </summary>
public sealed class ThemeScene : IScene
{
    private readonly Theme _theme;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeScene"/> class.
    /// </summary>
    /// <param name="name">The snapshot name.</param>
    /// <param name="theme">The theme.</param>
    public ThemeScene(string name, Theme theme)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 520);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var chart = CreateChart();
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }

    /// <summary>Creates the chart of this scene. The series belong to the caller.</summary>
    /// <returns>The chart.</returns>
    public ChartSurface CreateChart()
    {
        var theme = _theme.Resolve();
        var x = CreateAxis("x", AxisPosition.Bottom, 0, 10, "Time");
        var y = CreateAxis("y", AxisPosition.Left, -1.5, 1.5, "Signal");
        var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default)
        {
            Title = "Theme preview",
            Theme = theme,
        };
        var source = new DataSource<double, double>(180);
        var markers = new DataSource<double, double>(18);
        for (var i = 0; i < 180; i++)
        {
            var value = i * 10.0 / 179;
            source.Append(value, Math.Sin(value * 1.4) * 0.9);
            if (i % 10 == 0)
            {
                markers.Append(value, Math.Cos(value * 1.4) * 0.75);
            }
        }

        var palette = theme.Palette!;
        var line = new LineSeries<double, double>(source, x, y)
        {
            Color = palette[0].Value,
            Thickness = 2.5,
        };
        var scatter = new ScatterSeries<double, double>(markers, x, y)
        {
            Color = palette[1].Value,
            BorderColor = theme.Surface!.Background!.Value.Value,
            BorderThickness = 1.5,
            Size = 9,
        };
        chart.AddSeries(line);
        chart.AddSeries(scatter);
        return chart;
    }

    private static NumericAxis CreateAxis(string id, AxisPosition position, double min, double max, string title) => new(id, position)
    {
        VisibleRange = new DoubleRange(min, max),
        LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
        Title = title,
    };
}
