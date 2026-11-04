using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Examples.Gallery;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// All scenes known to sample apps, snapshot tool and tests.
/// </summary>
public static class SceneCatalog
{
    /// <summary>
    /// Gets the scenes that are not compared between the backends: Skia simplifies the glow, and colored mesh edges have no anti-aliasing, so single edge pixels differ. Each backend keeps its own golden image.
    /// </summary>
    public static IReadOnlySet<string> SimplifiedInSkia { get; } = new HashSet<string> { "GlowNeon", "Gradients", "ThemeNeon", "Ternary", "ParallelCoordinates", "Oscilloscope", "ColorGrade", "Spectrum", "Network" };

    /// <summary>
    /// Gets all scenes.
    /// </summary>
    public static IReadOnlyList<IScene> All { get; } =
    [
        new SmokeScene(),
        new PrimitivesScene(),
        new LineSpikeScene("LineSpike1M", 1_000_000),
        new LineSpikeScene("LineSpike100M", 100_000_000),
        new LineThinScene(),
        new LinePrecisionScene(),
        new LayoutMultiAxisScene(),
        new LayoutStackedPanesScene(),
        new TextSizesScene(),
        new ChartFrameScene(),
        new LineBasicScene(),
        new LineDashScene(),
        new LineStepScene(),
        new LineGapsScene(),
        new LineFifoWrapScene(),
        new ZoomPreviewScene(),
        new CrosshairScene(),
        new CrosshairSnapScene(),
        new RolloverScene(),
        new AnnotationsScene(),
        new SelectionScene(),
        new MeasureCursorsScene(),
        new LegendScene(),
        new GroupStackedScene(),
        new OverviewScene(),
        new LogAxisScene(),
        new ColormapsScene(),
        new Heatmap4kScene(),
        new HeatmapNonUniformScene(),
        new SpectrogramScene(),
        new ContourScene(),
        new PointCloud50MScene(),
        new OscilloscopeScene(),
        new ColorGradeScene(),
        new SpectrumScene(),
        new NetworkScene(),
        new SmithScene(),
        new TernaryScene(),
        new ParallelCoordinatesScene(),
        new ErrorBarsScene(),
        new StemScene(),
        new SplineScene(),
        new BubbleScene(),
        new AreaScene(),
        new AreaStackedScene(),
        new BandScene(),
        new BarsScene("BarsGrouped", BarLayout.Grouped, false),
        new BarsScene("BarsStacked", BarLayout.Stacked, false),
        new BarsScene("BarsPercent", BarLayout.Percent, false),
        new BarsScene("BarsHorizontal", BarLayout.Stacked, true),
        new HistogramScene(),
        new CategoryAxisScene(),
        new CategoryThinningScene(),
        new BoxViolinScene(),
        new PolarScene(),
        new RadarScene(),
        new PieScene("Pie", BuiltInThemes.TachyonDark, 0),
        new PieScene("DonutLight", BuiltInThemes.TachyonLight, 0.55),
        new CandleScene("Candles", BuiltInThemes.TachyonDark, CandleStyle.Candles),
        new CandleScene("OhlcLight", BuiltInThemes.TachyonLight, CandleStyle.Ohlc),
        new MarketProfileScene(),
        new DrawingsScene("Drawings", DrawingDemo.First),
        new DrawingsScene("DrawingPatterns", DrawingDemo.Second),
        new GanttScene("Gantt", BuiltInThemes.TachyonDark, null),
        new GanttScene("GanttCollapsedLight", BuiltInThemes.TachyonLight, "Vibration"),
        new SweepScene("Sweep", Tachyon.Charts.Core.Data.SweepMode.Sweep),
        new SweepScene("Stepping", Tachyon.Charts.Core.Data.SweepMode.Stepping),
        new AxisBreakScene("ScaleBreak", Examples.ChartTypes.ScaleBreakPage.Configure),
        new AxisBreakScene("CandlesWithoutWeekends", Examples.ChartTypes.CandlesWithoutWeekendsPage.Configure),
        new AxisBreakScene("ContourLabels", Examples.ChartTypes.LabelledContoursPage.Configure),
        new GlowNeonScene(),
        new GradientsScene(),
        new ThemeGalleryScene("ThemeHighContrast", BuiltInThemes.HighContrast),
        new ThemeGalleryScene("ThemeNeon", BuiltInThemes.Neon),
        new ThemeGalleryScene("ThemePaper", BuiltInThemes.Paper),
        new ThemeGalleryScene("ThemeInstrument", BuiltInThemes.Instrument),
        new ThemeGalleryScene("ThemeSignal", BuiltInThemes.Signal),
        new ScatterMarkersScene(),
        new ScatterPerPointColorScene(),
        new GridBandsScene(),
        new ThemeScene("ThemeDark", BuiltInThemes.TachyonDark),
        new ThemeScene("ThemeLight", BuiltInThemes.TachyonLight),
    ];

    /// <summary>
    /// Finds a scene by its case-insensitive name.
    /// </summary>
    /// <param name="name">The scene name.</param>
    /// <returns>The scene, or <see langword="null"/> if there is none.</returns>
    public static IScene? Find(string name) =>
        All.FirstOrDefault(scene => scene.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
