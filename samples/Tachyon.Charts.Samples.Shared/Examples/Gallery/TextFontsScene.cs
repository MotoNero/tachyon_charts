using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Fonts beside the default: the title in Roboto Slab bold, the axes in italic, and labels of annotations in bold,
/// italic and Roboto Slab bold italic. The annotations can be moved with the mouse.
/// </summary>
public sealed class TextFontsScene : IScene
{
    private static readonly Rgba8 _blue = new(80, 170, 240, 255);

    /// <inheritdoc />
    public string Name => "TextFonts";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title("Fonts: Roboto Slab, bold and italic")
        .TitleFont(new TextFont("Roboto Slab", Bold: true))
        .AxisFont(new TextFont(Italic: true))
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").Range(0, 10).Title("Time in s"))
        .YAxis(a => a.Numeric("y").Range(-1.5, 1.5).Title("Level"))
        .AddLine(Wave(), s => s.Name("Wave").Stroke(_blue).Thickness(2))
        .Modifiers(m => m.AnnotationEdit())
        .AddOverlay(chart => new HorizontalLineAnnotation(chart.Panes[0].Axes[0])
        {
            Y = 1.1,
            Label = "Bold label",
            Color = new Rgba8(240, 90, 90, 255),
            Appearance = new AnnotationAppearance { LabelPlace = LabelPlace.End, LabelFont = new TextFont(Bold: true) },
        })
        .AddOverlay(chart => new VerticalLineAnnotation(chart.HorizontalAxes[0])
        {
            X = 6,
            Label = "Italic label",
            Color = new Rgba8(110, 210, 130, 255),
            Appearance = new AnnotationAppearance { LabelFont = new TextFont(Italic: true) },
        })
        .AddOverlay(chart => new HorizontalLineAnnotation(chart.Panes[0].Axes[0])
        {
            Y = -1.2,
            Label = "Roboto Slab, bold italic",
            Color = new Rgba8(240, 160, 60, 255),
            Appearance = new AnnotationAppearance { LabelPlace = LabelPlace.Start, LabelFont = new TextFont("Roboto Slab", Bold: true, Italic: true) },
        });

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var builder = new ChartBuilder();
        Configure(builder);
        var built = builder.ToDefinition().Build(SkiaTextEngine.Default);
        built.Surface.Render(device, target);
        device.WaitForIdle();
    }

    // The wave of the annotation pages: 240 points from 0 to 10.
    private static DataSource<double, double> Wave()
    {
        const int Count = 240;
        var data = new DataSource<double, double>(Count);
        for (var i = 0; i < Count; i++)
        {
            var x = i * 10.0 / (Count - 1);
            data.Append(x, 0.9 * Math.Sin(x * 1.3));
        }

        return data;
    }
}
