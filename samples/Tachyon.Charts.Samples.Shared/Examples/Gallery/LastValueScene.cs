using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>Three temperatures whose newest values lie close together: their marks at the axis move apart.</summary>
public sealed class LastValueScene : IScene
{
    /// <inheritdoc />
    public string Name => "LastValue";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var lines = LastValueDemo.Recorded(285);
        var builder = new ChartBuilder().Title("Temperatures").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("t").Title("Time in s").Range(0, 28.4))
            .YAxis(a => a.Numeric("temperature").Title("Temperature in °C").Range(45, 75).Position(AxisPosition.Right));
        for (var i = 0; i < lines.Length; i++)
        {
            var name = LastValueDemo.Names[i];
            var index = i;
            builder.AddLine(lines[i], s => s.Name(name).Stroke(LastValueDemo.Colors[index]).Thickness(1.5).LastValue());
        }

        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
