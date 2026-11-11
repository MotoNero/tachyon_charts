using System.Diagnostics;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>
/// Opens a showroom page in the configurator: each page names the chart type of the configurator that shows it, or the
/// option of a chart, and the configurator starts with that type and its sample data. Medical pages start with the
/// phantoms that come with the configurator, never with files the showroom loaded.
/// </summary>
public static class ConfiguratorLink
{
    /// <summary>
    /// Gets what shows each page in the configurator: a type, <c>option:…</c> for an option of a chart, or
    /// <c>excluded</c> for a page with nothing to configure (see <see cref="Excluded"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Targets { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Line"] = "line",
        ["Step line"] = "line",
        ["Spline"] = "line",
        ["Last value at the axis"] = "line",
        ["LineSpike1M"] = "line",
        ["LineSpike100M"] = "line",
        ["LineThin"] = "line",
        ["LinePrecision"] = "line",
        ["LayoutMultiAxis"] = "line",
        ["LineBasic"] = "line",
        ["LineStep"] = "line",
        ["LineGaps"] = "line",
        ["LastValue"] = "line",
        ["ValueLabels"] = "bars",
        ["Scatter"] = "scatter",
        ["PointCloud50M"] = "scatter",
        ["ScatterMarkers"] = "scatter",
        ["Bubble"] = "bubbles",
        ["Area"] = "area",
        ["Stacked area"] = "stackedarea",
        ["AreaStacked"] = "stackedarea",
        ["Band"] = "band",
        ["Bars"] = "bars",
        ["Stacked bars"] = "bars",
        ["Horizontal bars"] = "bars",
        ["BarsGrouped"] = "bars",
        ["BarsStacked"] = "bars",
        ["BarsPercent"] = "bars",
        ["BarsHorizontal"] = "bars",
        ["Error bars"] = "errorbars",
        ["ErrorBars"] = "errorbars",
        ["Stems"] = "stems",
        ["Stem"] = "stems",
        ["Histogram"] = "histogram",
        ["Box plot"] = "boxplot",
        ["BoxViolin"] = "boxplot",
        ["Violin"] = "violin",
        ["Pie"] = "pie",
        ["Donut"] = "donut",
        ["DonutLight"] = "donut",
        ["Colormaps"] = "heatmap",
        ["Heatmap4k"] = "heatmap",
        ["Crosshair"] = "crosshair",
        ["CrosshairSnap"] = "crosshair",
        ["CrosshairValues"] = "crosshair",
        ["CrosshairValuesTurned"] = "crosshair",
        ["Rollover"] = "rollover",
        ["MeasureCursors"] = "measure",
        ["Legend"] = "option:legend",
        ["Gruppe (3 Charts)"] = "option:group",
        ["Logarithmic axis"] = "option:axis-kind",
        ["LogAxis"] = "option:axis-kind",
        ["Depth profile"] = "option:x-down",
        ["DepthProfile"] = "option:x-down",
        ["ThemeHighContrast"] = "option:theme",
        ["ThemeNeon"] = "option:theme",
        ["ThemePaper"] = "option:theme",
        ["ThemeInstrument"] = "option:theme",
        ["ThemeSignal"] = "option:theme",
        ["ThemeDark"] = "option:theme",
        ["ThemeLight"] = "option:theme",
        ["Waterfall, live"] = "waterfall3d",
        ["Path, live"] = "line3d",
        ["Path of 200 000 points"] = "line3d",
        ["Scatter of 3000 points"] = "scatter3d",
        ["Scatter of 10 million points"] = "scatter3d",
        ["Cut planes"] = "option:scene",
        ["One plane, all kinds"] = "option:scene",
        ["Cloud, live"] = "cloud3d",
        ["Cloud of 20 million points"] = "cloud3d",
        ["Cloud of 100 million points"] = "cloud3d",
        ["Volume slices 512³"] = "volume3d",
        ["Through a volume 512³"] = "volume3d",
        ["Objects inside a volume"] = "volume3d",
        ["Maximum projection 512³"] = "volume3d",
        ["Surface of equal value 512³"] = "volume3d",
        ["Columns"] = "columns3d",
        ["1 million columns"] = "columns3d",
        ["Cut, live"] = "excluded",
        ["Pie 3D"] = "pie3d",
        ["Donut 3D"] = "donut3d",
        ["Planes and limits"] = "surface3d",
        ["Bubbles"] = "bubbles3d",
        ["1 million bubbles"] = "bubbles3d",
        ["Model with measuring points"] = "model3d",
        ["Model of 1 million triangles"] = "model3d",
        ["Antenna pattern"] = "antenna3d",
        ["Map from measurements"] = "map3d",
        ["Height lines"] = "surface3d",
        ["Surface 4096 × 4096"] = "surface3d",
        ["Surface with light"] = "surface3d",
        ["Axis box"] = "option:scene",
        ["Video recording"] = "option:export",
        ["Smoke"] = "excluded",
        ["Categories"] = "option:axes",
        ["AxesInTheOrigin"] = "option:axes",
        ["Scale break"] = "option:axes",
        ["Several legends"] = "option:legend",
        ["Axes in the origin"] = "option:axes",
        ["Annotations"] = "option:annotations",
        ["Primitives"] = "excluded",
        ["LayoutStackedPanes"] = "option:axes",
        ["TextSizes"] = "excluded",
        ["ChartFrame"] = "excluded",
        ["LineDash"] = "line",
        ["ZoomPreview"] = "rubberband",
        ["Selection"] = "selection",
        ["GroupStacked"] = "option:group",
        ["Overview"] = "excluded",
        ["CategoryAxis"] = "option:axes",
        ["CategoryThinning"] = "option:axes",
        ["ScaleBreak"] = "option:axes",
        ["Legends"] = "option:legend",
        ["GlowNeon"] = "line",
        ["Gradients"] = "area",
        ["ScatterPerPointColor"] = "bubbles",
        ["GridBands"] = "excluded",
        ["Live-Chart (interaktiv)"] = "option:live",
        ["LineFifoWrap"] = "option:live",
        ["Medical image"] = "medicalImage",
        ["Medical measurements"] = "medicalImage",
        ["Medical MPR"] = "medicalMpr",
        ["Medical analysis"] = "medicalAnalysis",
        ["Medical CT and MRI"] = "medicalSlice",
        ["Medical oblique and curved"] = "medicalReformat",
        ["MedicalAxial"] = "medicalImage",
        ["MedicalCoronal"] = "medicalImage",
        ["MedicalSagittal"] = "medicalImage",
        ["MedicalMeasurements"] = "medicalImage",
        ["MedicalAnalysisSlice"] = "medicalAnalysis",
        ["MedicalProfile"] = "medicalAnalysis",
        ["MedicalHistogram"] = "medicalAnalysis",
        ["MedicalThoraxAxial"] = "medicalSlice",
        ["MedicalThoraxCoronal"] = "medicalSlice",
        ["MedicalReformatSlice"] = "medicalReformat",
        ["MedicalOblique"] = "medicalReformat",
        ["MedicalCurved"] = "medicalReformat",
        ["Raster (interaktiv)"] = "heatmap",
        ["Smith (interaktiv)"] = "smith",
        ["Parallel (interaktiv)"] = "parallel",
        ["Polygons"] = "polygons",
        ["10 000 polygons"] = "polygons",
        ["Heatmap in an area"] = "maskedheatmap",
        ["Uneven heatmap 4096²"] = "unevenheatmap",
        ["Labelled contours"] = "contour",
        ["Polar"] = "polar",
        ["Antenna cut"] = "polar",
        ["Cut through a surface"] = "line",
        ["Radar"] = "radar",
        ["Ternary"] = "ternary",
        ["HeatmapNonUniform"] = "unevenheatmap",
        ["Spectrogram"] = "spectrogram",
        ["Contour"] = "contour",
        ["Smith"] = "smith",
        ["ParallelCoordinates"] = "parallel",
        ["ContourLabels"] = "contour",
        ["HeatmapInAnArea"] = "maskedheatmap",
        ["Candles"] = "candles",
        ["OHLC bars"] = "candles",
        ["1 million candles"] = "candles",
        ["Candles without weekends"] = "option:weekends",
        ["Market profile"] = "candles",
        ["Drawing tools"] = "candles",
        ["Gantt"] = "gantt",
        ["Gantt, 100 000 tasks"] = "gantt",
        ["OhlcLight"] = "candles",
        ["MarketProfile"] = "candles",
        ["Drawings"] = "candles",
        ["DrawingPatterns"] = "candles",
        ["GanttCollapsedLight"] = "gantt",
        ["CandlesWithoutWeekends"] = "option:weekends",
        ["Oszilloskop (interaktiv)"] = "oscilloscope",
        ["Spektrum (interaktiv)"] = "spectrum",
        ["Network (interaktiv)"] = "network",
        ["Sweep monitor"] = "monitor",
        ["Stepping monitor"] = "monitor",
        ["Oscilloscope"] = "oscilloscope",
        ["ColorGrade"] = "oscilloscope",
        ["Spectrum"] = "spectrum",
        ["Network"] = "network",
        ["Sweep"] = "monitor",
        ["Stepping"] = "monitor",
    };

    /// <summary>Gets the pages with nothing to configure, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> Excluded { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Smoke"] = "A check of the showroom itself, not a chart feature.",
        ["Primitives"] = "A test of the render API, not a chart feature.",
        ["TextSizes"] = "A test of text at several scalings, not a chart feature.",
        ["ChartFrame"] = "A test of one complete frame of the pipeline; its parts (panes, axes, grid) are configurable.",
        ["Overview"] = "Needs a second chart whose window it marks; the configurator describes one chart.",
        ["Cut, live"] = "Needs a second chart that shows the cut as a curve; the configurator describes one chart, its cut plane is an option of the scene.",
        ["GridBands"] = "Grid bands and plot gradients belong to themes; the built-in themes have none, own themes are not part of the configurator yet.",
    };

    // The type an option of a chart starts with: options of 3D scenes on a surface, the weekends on candles, the others on a line.
    private static readonly Dictionary<string, string> _optionTypes = new(StringComparer.Ordinal)
    {
        ["option:scene"] = "surface3d",
        ["option:weekends"] = "candles",
    };

    // Pages that show an interaction start with a line and that interaction switched on.
    private static readonly HashSet<string> _interactions = new(StringComparer.Ordinal) { "crosshair", "rollover", "measure", "selection", "rubberband" };

    /// <summary>Gets the interaction a page switches on in the configurator.</summary>
    /// <param name="page">The identifier of the page.</param>
    /// <returns>The interaction, or <see langword="null"/>.</returns>
    public static string? InteractionOf(string? page) =>
        page is not null && Targets.TryGetValue(page, out var target) && _interactions.Contains(target) ? target : null;

    /// <summary>Gets the type of the configurator a page starts with.</summary>
    /// <param name="page">The identifier of the page.</param>
    /// <returns>The type, or <see langword="null"/> for a page with nothing to configure.</returns>
    public static string? TypeOf(string? page)
    {
        if (page is null || !Targets.TryGetValue(page, out var target) || target == "excluded")
        {
            return null;
        }

        return target.StartsWith("option:", StringComparison.Ordinal) ? _optionTypes.GetValueOrDefault(target, "line")
            : _interactions.Contains(target) ? "line"
            : target;
    }

    /// <summary>
    /// Finds the configurator: beside the showroom, as in the downloads; then as built in this repository; then as a
    /// tool on the path (<c>dotnet tool install -g Tachyon.Charts.Configurator.Tool</c>).
    /// </summary>
    /// <param name="app">The name of the application without extension, such as <c>Tachyon.Charts.Configurator.Wpf</c>.</param>
    /// <returns>The program, or <see langword="null"/> when there is none.</returns>
    public static string? Find(string app)
    {
        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var beside = Path.Combine(AppContext.BaseDirectory, app + extension);
        if (File.Exists(beside))
        {
            return beside;
        }

        // In the repository the showroom runs from samples/<app>/bin/<configuration>/<framework>/; the configurator
        // builds into src/<app>/bin with the same configuration.
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var project = Path.Combine(folder.FullName, "src", app, "bin");
            if (Directory.Exists(project))
            {
                return Directory.EnumerateFiles(project, app + extension, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
        }

        var tool = "tachyon-configurator" + extension;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(folder, tool);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Gets why a page cannot open in the configurator, for the tooltip of the greyed out button.</summary>
    /// <param name="page">The identifier of the page.</param>
    /// <returns>The reason, or <see langword="null"/> when it can.</returns>
    public static string? WhyNot(string? page) =>
        TypeOf(page) is not null ? null : page is not null && Excluded.TryGetValue(page, out var reason) ? reason : "This page has nothing to configure.";

    /// <summary>Starts the configurator with the type of a page.</summary>
    /// <param name="app">The name of the application without extension.</param>
    /// <param name="page">The identifier of the page.</param>
    /// <returns>A text for the status line: what was opened, or why nothing was.</returns>
    public static string Open(string app, string? page)
    {
        if (TypeOf(page) is not { } type)
        {
            return page is not null && Excluded.TryGetValue(page, out var reason) ? "Nothing to configure here: " + reason : "Nothing to configure here.";
        }

        if (Find(app) is not { } program)
        {
            return "The configurator was not found beside the showroom, in the repository or as a tool on the path.";
        }

        // The page opens completely where the configurator describes it; the type is the fallback of older versions.
        var start = new ProcessStartInfo(program) { ArgumentList = { "--page", page!, "--type", type }, UseShellExecute = false };
        if (InteractionOf(page) is { } interaction)
        {
            start.ArgumentList.Add("--modifier");
            start.ArgumentList.Add(interaction);
        }

        using var process = Process.Start(start);
        return "The configurator opens with " + type + ".";
    }
}
