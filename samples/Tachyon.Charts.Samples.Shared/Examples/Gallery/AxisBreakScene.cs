using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A page with an axis break, drawn as the showroom builds it.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="configure">Builds the chart.</param>
public sealed class AxisBreakScene(string name, Action<ChartBuilder> configure) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <summary>Gets the description of the chart, the same as on its page among the chart types.</summary>
    public Action<ChartBuilder> Configure => configure;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 520);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var builder = new ChartBuilder();
        configure(builder);
        builder.Theme(BuiltInThemes.TachyonDark);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
