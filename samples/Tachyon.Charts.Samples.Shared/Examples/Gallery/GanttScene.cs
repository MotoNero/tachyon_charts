using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A test plan as a Gantt chart; with a collapsed group, its line shows the span of all its tasks.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="theme">The theme.</param>
/// <param name="collapsed">The group to collapse, or <see langword="null"/>.</param>
public sealed class GanttScene(string name, Theme theme, string? collapsed) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 520);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var plan = GanttDemo.TestPlan();
        if (collapsed is not null)
        {
            plan.SetCollapsed(collapsed, true);
        }

        var builder = new ChartBuilder().Title("Test plan").Theme(theme)
            .XAxis(a => a.DateTime("time").AutoRange())
            .Gantt(plan);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
