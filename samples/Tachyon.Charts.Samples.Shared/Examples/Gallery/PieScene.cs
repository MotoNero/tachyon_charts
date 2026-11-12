using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A pie or a donut of a power mix, with small slices whose labels sit outside on lines.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="theme">The theme.</param>
/// <param name="innerRadius">0 for a pie, a share of the radius for a donut.</param>
public sealed class PieScene(string name, Theme theme, double innerRadius) : IScene, IChartScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 560);

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
        var builder = new ChartBuilder().Title("Power mix").Theme(theme)
            .Pie(PieData.Values, PieData.Names, pie =>
            {
                pie.InnerRadius = innerRadius;
                pie.ExplodedIndex = innerRadius > 0 ? -1 : 1;
            });
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        disposables = [];
        return chart;
    }
}
