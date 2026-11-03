using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Interactive;

/// <summary>Four rasters: a heatmap of 16 million cells, a spectrogram that keeps growing, a contour and a dense point cloud.</summary>
public static class RastersPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Raster (interaktiv)";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Wheel over the color bar: color range | List: colormap";

    /// <summary>The rows the spectrogram keeps.</summary>
    public const int SpectrogramRows = 512;

    /// <summary>Builds the heatmap.</summary>
    /// <param name="c">The chart.</param>
    public static void ConfigureHeatmap(ChartBuilder c) => c
        .Title("Heatmap 4096 × 4096")
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").AutoRange())
        .YAxis(a => a.Numeric("y").AutoRange())
        .AddHeatmap(Heatmap4kData.CreateData(withGap: false), s => s.Name(SampleText.T("Value")).Cells(0, 8.0 / 4096, 0, 8.0 / 4096).ColorAxis(a => a.Colormap(Colormaps.Inferno)))
        .Modifiers(m => m.ZoomPan().RubberBand().AxisDrag().Tooltip());

    /// <summary>Builds the spectrogram. A thread of the app appends 60 rows per second.</summary>
    /// <param name="c">The chart.</param>
    /// <param name="rows">The rows.</param>
    public static void ConfigureSpectrogram(ChartBuilder c, SpectrogramData rows) => c
        .Title(SampleText.T("Spectrogram, 60 rows per second"))
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").Range(0, 1024))
        .YAxis(a => a.Numeric("y").Range(-SpectrogramRows, 0))
        .AddSpectrogram(rows, s => s.Name(SampleText.T("Level")).ColorAxis(a => a.Colormap(Colormaps.Plasma).Range(-0.1, 1.1)))
        .Modifiers(m => m.ZoomPan().AxisDrag().Tooltip());

    /// <summary>Builds the filled contour.</summary>
    /// <param name="c">The chart.</param>
    public static void ConfigureContour(ChartBuilder c) => c
        .Title(SampleText.T("Contour, filled"))
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").AutoRange())
        .YAxis(a => a.Numeric("y").AutoRange())
        .AddContour(ContourData.CreateField(256, withGap: false), s => s.Name(SampleText.T("Height")).Filled().Levels(-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1, 1.25).Cells(0, 6.0 / 256, 0, 6.0 / 256))
        .Modifiers(m => m.ZoomPan().RubberBand());

    /// <summary>Builds the cloud of 5 million points, colored by density.</summary>
    /// <param name="c">The chart.</param>
    public static void ConfigureDensity(ChartBuilder c) => c
        .Title(SampleText.T("5 million points: zoom in for markers"))
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").Range(0, 10))
        .YAxis(a => a.Numeric("y").Range(0, 10))
        .AddScatter(PointCloudData.CreateCloud(5_000_000), s => s.Name(SampleText.T("Points")).Size(4).Density(1, a => a.Colormap(Colormaps.Inferno)))
        .Modifiers(m => m.ZoomPan().RubberBand().Tooltip());
}
