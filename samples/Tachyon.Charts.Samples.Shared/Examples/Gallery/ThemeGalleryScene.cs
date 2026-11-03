using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

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
