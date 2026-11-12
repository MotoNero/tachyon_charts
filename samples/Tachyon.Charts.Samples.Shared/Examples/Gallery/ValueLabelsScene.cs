using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Sales per quarter as grouped bars and a target line, each with its values written at the points.</summary>
public sealed class ValueLabelsScene : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => "ValueLabels";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

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
        var target2 = new DataSource<double, double>(8);
        for (var i = 0; i < 8; i++)
        {
            target2.Append(i, 52 + (4 * i));
        }

        var chart = new ChartBuilder().Title("Sales per quarter").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("quarter").Title("Quarter").Range(-0.6, 7.6))
            .YAxis(a => a.Numeric("sales").Title("Sales in k€").Range(0, 110))
            .AddSeries((x, y) => new BarSeries([[48, 55, 61, 58, 70, 76, 81, 90], [35, 38, 42.5, 47, 44, 52, 57.25, 63]], x, y) { Name = "North and South", ShowsValues = true }, "quarter", "sales")
            .AddLine(target2, s => s.Name("Target").Thickness(2).Values())
            .ToDefinition().Build(SkiaTextEngine.Default).Surface;
        disposables = [];
        return chart;
    }
}
