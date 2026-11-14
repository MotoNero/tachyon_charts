using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Major and minor grid lines over alternating bands and a vertical plot gradient, all from a theme of its own: the
/// dark theme with five values changed.
/// </summary>
public sealed class GridBandsScene : IScene
{
    /// <summary>Gets the theme: grid, bands and plot gradient over the dark theme.</summary>
    public static Theme Bands { get; } = ThemeResources.Create("Grid bands", "dark", new Dictionary<string, string>
    {
        ["Tachyon.surface.background"] = "#0B0E16",
        ["Tachyon.surface.plotArea"] = "linear(#202D42, #0C121F)",
        ["Tachyon.grid.major"] = "#AABEDC48",
        ["Tachyon.grid.minor"] = "#AABEDC1C",
        ["Tachyon.grid.band"] = "#6EA0DC14",
    });

    /// <inheritdoc />
    public string Name => "GridBands";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 520);

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title("GridBands")
        .Theme(Bands)
        .XAxis(a => a.Numeric("x").Range(0, 12).Title("Major and minor grid"))
        .YAxis(a => a.Numeric("y").Range(0, 8))
        .AddLine(Wave(), s => s.Name("Wave").Stroke(new Rgba8(92, 200, 255, 255)).Thickness(2));

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var builder = new ChartBuilder();
        Configure(builder);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        device.WaitForIdle();
    }

    // A slow wave through the middle, 300 points from 0 to 12.
    private static DataSource<double, double> Wave()
    {
        const int Count = 300;
        var data = new DataSource<double, double>(Count);
        for (var i = 0; i < Count; i++)
        {
            var x = i * 12.0 / (Count - 1);
            data.Append(x, 4 + (2.5 * Math.Sin(x * 0.8)));
        }

        return data;
    }
}
