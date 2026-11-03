using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>A clean picture for the documentation. Scenes for tests show gaps and failures on purpose, these do not.</summary>
/// <param name="name">The file name.</param>
/// <param name="configure">Describes the chart.</param>
public sealed class ShowcaseScene(string name, Action<ChartBuilder> configure) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1200, 760);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var builder = new ChartBuilder();
        configure(builder);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}

/// <summary>The pictures of the README.</summary>
public static class ShowcaseScenes
{
    /// <summary>Gets the pictures.</summary>
    public static IReadOnlyList<IScene> All { get; } =
    [
        new ShowcaseScene("network", c => c.NetworkAnalyzer(Tachyon.Charts.Samples.Shared.Examples.Common.NetworkData.CreateDisplay(passing: true), 1e9, 2e9, -70, 5).Title("Band-pass filter, S21 and S11").Theme(BuiltInThemes.Instrument)),
        new ShowcaseScene("heatmap", c => c.Title("Heatmap, 16 million cells").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("x").Range(0, 8))
            .YAxis(a => a.Numeric("y").Range(0, 8))
            .AddHeatmap(Heatmap4kData.CreateData(withGap: false), s => s.Cells(0, 8.0 / 4096, 0, 8.0 / 4096).ColorAxis(a => a.Colormap(Colormaps.Inferno)))),
        new ShowcaseScene("contour", c => c.Title("Filled contour").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("x").Range(0, 6))
            .YAxis(a => a.Numeric("y").Range(0, 6))
            .AddContour(ContourData.CreateField(256, withGap: false), s => s.Filled().Levels(-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1, 1.25).Cells(0, 6.0 / 256, 0, 6.0 / 256))),
        new ShowcaseScene("point-cloud", c => c.Title("50 million points as density").Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("x").Range(0, 10))
            .YAxis(a => a.Numeric("y").Range(0, 10))
            .AddScatter(PointCloudData.CreateCloud(50_000_000), s => s.Size(4).Density(1, a => a.Colormap(Colormaps.Inferno)))),
        Type("bars", "Bars", "Grouped bars"),
        Type("stacked-area", "Stacked area", "Stacked areas"),
        Type("bubble", "Bubble", "Bubbles"),
        Type("violin", "Violin", "Violin plot"),
        Type("polar", "Polar", "Polar chart"),
        Type("radar", "Radar", "Radar chart"),
        Type("ternary", "Ternary", "Ternary plot"),
        Type("error-bars", "Error bars", "Measurements with error bars"),
        Theme("theme-neon", "Neon", BuiltInThemes.Neon),
        Theme("theme-paper", "Paper", BuiltInThemes.Paper),
        Theme("theme-instrument", "Instrument", BuiltInThemes.Instrument),
    ];

    private static ShowcaseScene Type(string name, string page, string title) => new(name, c =>
    {
        ChartTypePages.All.First(type => type.Name == page).Configure(c);
        c.Title(title);
    });

    private static ShowcaseScene Theme(string name, string title, Theme theme) => new(name, c => c.Title(title).Theme(theme)
        .XAxis(a => a.Numeric("x").Title("Time in s").Range(0, 10))
        .YAxis(a => a.Numeric("y").Title("Voltage in V").Range(-1.5, 1.5))
        .AddLine(Examples.Common.ChartTypeData.Sample(Math.Sin), s => s.Name("Input").Thickness(2))
        .AddLine(Examples.Common.ChartTypeData.Sample(t => 0.8 * Math.Cos(t * 1.3)), s => s.Name("Output").Thickness(2)));
}
