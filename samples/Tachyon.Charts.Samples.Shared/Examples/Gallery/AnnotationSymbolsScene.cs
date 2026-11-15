using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Icons;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Labels of annotations with symbols of the icon package: each in its own color and size, on a plate and without one.
/// </summary>
public sealed class AnnotationSymbolsScene : IScene
{
    /// <inheritdoc />
    public string Name => "AnnotationSymbols";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title("Symbols at annotations")
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").Range(0, 10).Title("Time in s"))
        .YAxis(a => a.Numeric("y").Range(-1.5, 1.5).Title("Level"))
        .AddLine(Wave(), s => s.Name("Wave").Stroke(new Rgba8(80, 170, 240, 255)).Thickness(2))
        .Modifiers(m => m.AnnotationEdit())
        .AddOverlay(chart => new HorizontalLineAnnotation(chart.Panes[0].Axes[0])
        {
            Y = 1.1,
            Label = "Too hot",
            Color = new Rgba8(240, 90, 90, 255),
            Appearance = new AnnotationAppearance { LabelPlace = LabelPlace.End, LabelSymbol = MaterialSymbols.Symbol("thermostat"), LabelColor = new Rgba8(255, 255, 255, 255) },
        })
        .AddOverlay(chart => new VerticalLineAnnotation(chart.HorizontalAxes[0])
        {
            X = 6,
            Label = "Power on",
            Color = new Rgba8(240, 190, 90, 255),
            Appearance = new AnnotationAppearance { LabelSymbol = MaterialSymbols.Symbol("bolt", 20), LabelIconColor = new Rgba8(30, 30, 30, 255) },
        })
        .AddOverlay(chart => new HorizontalLineAnnotation(chart.Panes[0].Axes[0])
        {
            Y = -1.2,
            Label = "Service due",
            Color = new Rgba8(110, 210, 130, 255),
            Appearance = new AnnotationAppearance { LabelPlace = LabelPlace.Start, LabelSymbol = MaterialSymbols.Symbol("build", 24), LabelFill = new Rgba8(0, 0, 0, 0), LabelColor = new Rgba8(110, 210, 130, 255) },
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
