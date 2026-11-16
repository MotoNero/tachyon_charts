using System.Diagnostics;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>
/// Opens a showroom page in the configurator: each page names the chart type of the configurator that shows it, or the
/// option of a chart, and the configurator starts with that type and its sample data. Medical pages start with the
/// phantoms that come with the configurator, never with files the showroom loaded.
/// </summary>
public static class ConfiguratorLink
{
    private const string OptionAnnotations = "option:annotations";
    private const string OptionTheme = "option:theme";
    private const string ExcludedKey = "excluded";
    private const string CrosshairKey = "crosshair";
    private const string OptionScene = "option:scene";
    private const string Volume3d = "volume3d";
    private const string Surface3d = "surface3d";
    private const string OptionAxes = "option:axes";
    private const string MedicalImage = "medicalImage";
    private const string MedicalAnalysis = "medicalAnalysis";
    private const string MedicalReformat = "medicalReformat";
    private const string CandlesKey = "candles";
    private const string Monitor = "monitor";

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
        ["LineSpike1M"] = ExcludedKey,
        ["LineSpike100M"] = ExcludedKey,
        ["LineThin"] = ExcludedKey,
        ["LinePrecision"] = ExcludedKey,
        ["LayoutMultiAxis"] = ExcludedKey,
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
        ["Colormaps"] = ExcludedKey,
        ["Heatmap4k"] = "heatmap",
        ["Crosshair"] = CrosshairKey,
        ["CrosshairSnap"] = CrosshairKey,
        ["CrosshairValues"] = CrosshairKey,
        ["CrosshairValuesTurned"] = CrosshairKey,
        ["Rollover"] = "rollover",
        ["MeasureCursors"] = "measure",
        ["Legend"] = "option:legend",
        ["Gruppe (3 Charts)"] = "option:group",
        ["Logarithmic axis"] = "option:axis-kind",
        ["LogAxis"] = "option:axis-kind",
        ["Depth profile"] = "option:x-down",
        ["DepthProfile"] = "option:x-down",
        ["ThemeHighContrast"] = OptionTheme,
        ["ThemeNeon"] = OptionTheme,
        ["ThemePaper"] = OptionTheme,
        ["ThemeInstrument"] = OptionTheme,
        ["ThemeSignal"] = OptionTheme,
        ["ThemeDark"] = OptionTheme,
        ["ThemeLight"] = OptionTheme,
        ["Waterfall, live"] = "spectrumWaterfall",
        ["Path, live"] = "line3d",
        ["Path of 200 000 points"] = "line3d",
        ["Scatter of 3000 points"] = "scatter3d",
        ["Scatter of 10 million points"] = "scatter3d",
        ["Cut planes"] = OptionScene,
        ["One plane, all kinds"] = OptionScene,
        ["Cloud, live"] = "scatter3d",
        ["Cloud of 20 million points"] = "cloud3d",
        ["Cloud of 100 million points"] = "cloud3d",
        ["Cloud from a file"] = "cloudFile3d",
        ["Volume slices 512³"] = Volume3d,
        ["Through a volume 512³"] = Volume3d,
        ["Objects inside a volume"] = Volume3d,
        ["Maximum projection 512³"] = Volume3d,
        ["Surface of equal value 512³"] = Volume3d,
        ["Columns"] = "columns3d",
        ["1 million columns"] = "columns3d",
        ["Cut, live"] = "surfaceCut3d",
        ["Pie 3D"] = "pie3d",
        ["Donut 3D"] = "donut3d",
        ["Planes and limits"] = Surface3d,
        ["Bubbles"] = "bubbles3d",
        ["1 million bubbles"] = "bubbles3d",
        ["Model with measuring points"] = "model3d",
        ["Model of 1 million triangles"] = "model3d",
        ["Antenna pattern"] = "antenna3d",
        ["Map from measurements"] = "map3d",
        ["Height lines"] = Surface3d,
        ["Surface 4096 × 4096"] = Surface3d,
        ["Surface with light"] = Surface3d,
        ["Axis box"] = OptionScene,
        ["Video recording"] = "option:export",
        ["Smoke"] = ExcludedKey,
        ["Categories"] = OptionAxes,
        ["AxesInTheOrigin"] = OptionAxes,
        ["Scale break"] = OptionAxes,
        ["Several legends"] = "option:legend",
        ["Own theme"] = OptionTheme,
        ["Axes in the origin"] = OptionAxes,
        ["Annotations"] = OptionAnnotations,
        ["Primitives"] = ExcludedKey,
        ["LayoutStackedPanes"] = ExcludedKey,
        ["TextSizes"] = ExcludedKey,
        ["ChartFrame"] = ExcludedKey,
        ["LineDash"] = "line",
        ["AnnotationStyles"] = OptionAnnotations,
        ["TextFonts"] = OptionAnnotations,
        ["AnnotationSymbols"] = OptionAnnotations,
        ["ZoomPreview"] = "rubberband",
        ["Selection"] = "selection",
        ["GroupStacked"] = "option:group",
        ["Overview"] = "option:overview",
        ["CategoryAxis"] = OptionAxes,
        ["CategoryThinning"] = OptionAxes,
        ["ScaleBreak"] = OptionAxes,
        ["Legends"] = "option:legend",
        ["GlowNeon"] = "line",
        ["Gradients"] = "area",
        ["ScatterPerPointColor"] = "bubbles",
        ["GridBands"] = OptionTheme,
        ["Live-Chart (interaktiv)"] = "option:live",
        ["LineFifoWrap"] = "option:live",
        ["Medical image"] = MedicalImage,
        ["Medical measurements"] = MedicalImage,
        ["Medical MPR"] = "medicalMpr",
        ["Medical analysis"] = MedicalAnalysis,
        ["Medical CT and MRI"] = "medicalSlice",
        ["Medical oblique and curved"] = MedicalReformat,
        ["MedicalAxial"] = MedicalImage,
        ["MedicalCoronal"] = MedicalImage,
        ["MedicalSagittal"] = MedicalImage,
        ["MedicalMeasurements"] = MedicalImage,
        ["MedicalAnalysisSlice"] = MedicalAnalysis,
        ["MedicalProfile"] = MedicalAnalysis,
        ["MedicalHistogram"] = MedicalAnalysis,
        ["MedicalThoraxAxial"] = "medicalSlice",
        ["MedicalThoraxCoronal"] = "medicalSlice",
        ["MedicalReformatSlice"] = MedicalReformat,
        ["MedicalOblique"] = MedicalReformat,
        ["MedicalCurved"] = MedicalReformat,
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
        ["Candles"] = CandlesKey,
        ["OHLC bars"] = CandlesKey,
        ["1 million candles"] = CandlesKey,
        ["Large file"] = "fileLine",
        ["Recording"] = "fileLine",
        ["Digital channels"] = "digital",
        ["Scatter from a file"] = "pointDensity",
        ["Heatmap from a file"] = "fileRaster",
        ["World map"] = "geoMap",
        ["Tile map"] = "tileMap",
        ["Candles without weekends"] = "option:weekends",
        ["Market profile"] = CandlesKey,
        ["Drawing tools"] = CandlesKey,
        ["Gantt"] = "gantt",
        ["Gantt, 100 000 tasks"] = "gantt",
        ["OhlcLight"] = CandlesKey,
        ["MarketProfile"] = CandlesKey,
        ["Drawings"] = CandlesKey,
        ["DrawingPatterns"] = CandlesKey,
        ["GanttCollapsedLight"] = "gantt",
        ["CandlesWithoutWeekends"] = "option:weekends",
        ["Oszilloskop (interaktiv)"] = "oscilloscope",
        ["Spektrum (interaktiv)"] = "spectrum",
        ["Network (interaktiv)"] = "network",
        ["Sweep monitor"] = Monitor,
        ["Stepping monitor"] = Monitor,
        ["Oscilloscope"] = "oscilloscope",
        ["ColorGrade"] = "oscilloscope",
        ["Spectrum"] = "spectrum",
        ["Network"] = "network",
        ["Sweep"] = Monitor,
        ["Stepping"] = Monitor,
    };

    /// <summary>Gets the pages with nothing to configure, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> Excluded { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Smoke"] = "A check of the showroom itself, not a chart feature.",
        ["Primitives"] = "A test of the render API, not a chart feature.",
        ["TextSizes"] = "A test of text at several scalings, not a chart feature.",
        ["ChartFrame"] = "A test of one complete frame of the pipeline; its parts (panes, axes, grid) are configurable.",
        ["LineSpike1M"] = "A test of drawing a million points with the render API, without a chart; lines with many points are configurable.",
        ["LineSpike100M"] = "A test of drawing 100 million points with the render API, without a chart; lines with many points are configurable.",
        ["LineThin"] = "A test of thin lines with the render API, without a chart; the thickness of lines is configurable.",
        ["LinePrecision"] = "A test of the precision of lines with the render API, without a chart.",
        ["LayoutMultiAxis"] = "A test of the layout of several axes, drawn without a chart; several axes are configurable.",
        ["LayoutStackedPanes"] = "A test of the layout of stacked panes, drawn without a chart; panes are configurable.",
        ["Colormaps"] = "A catalog of all colormaps side by side, drawn without a chart; each type with colors offers the colormaps.",
    };

    // The type an option of a chart starts with: options of 3D scenes on a surface, the weekends on candles, the others on a line.
    private static readonly Dictionary<string, string> _optionTypes = new(StringComparer.Ordinal)
    {
        [OptionScene] = Surface3d,
        ["option:weekends"] = CandlesKey,
    };

    // Pages that show an interaction start with a line and that interaction switched on.
    private static readonly HashSet<string> _interactions = new(StringComparer.Ordinal) { CrosshairKey, "rollover", "measure", "selection", "rubberband" };

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
        if (page is null || !Targets.TryGetValue(page, out var target) || target == ExcludedKey)
        {
            return null;
        }

        if (target.StartsWith("option:", StringComparison.Ordinal))
        {
            return _optionTypes.GetValueOrDefault(target, "line");
        }

        return _interactions.Contains(target) ? "line" : target;
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
        // builds into src/<app>/bin/<configuration>/<framework>/. Only that build counts, the one of the same
        // configuration first: published copies below it, such as bin/Release/<framework>/win-x64, may be old.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Parent?.Name;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var project = Path.Combine(folder.FullName, "src", app, "bin");
            if (Directory.Exists(project))
            {
                return Directory.EnumerateDirectories(project)
                    .SelectMany(Directory.EnumerateDirectories)
                    .Select(build => Path.Combine(build, app + extension))
                    .Where(File.Exists)
                    .OrderByDescending(path => string.Equals(new DirectoryInfo(path).Parent?.Parent?.Name, configuration, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(File.GetLastWriteTimeUtc)
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
    public static string? WhyNot(string? page)
    {
        if (TypeOf(page) is not null)
        {
            return null;
        }

        return page is not null && Excluded.TryGetValue(page, out var reason) ? reason : "This page has nothing to configure.";
    }

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

        // A configurator built before its last change may not know the page yet; "dotnet run" of the showroom does not build it.
        return IsOlderThanItsSources(program)
            ? $"The configurator at {program} is older than its sources and may not open the page completely. Build it: dotnet build src/{app} -c Release"
            : "The configurator opens " + page + ".";
    }

    /// <summary>
    /// Gets a value indicating whether a configurator built in the repository is older than its sources, so that it may
    /// not know the newest pages: one of its sources changed after its libraries were built.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <returns>Whether it is older; a program outside the repository, as in the downloads, never.</returns>
    public static bool IsOlderThanItsSources(string program)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(program))!;
        var src = new DirectoryInfo(folder);
        while (src is not null && src.Name != "src")
        {
            src = src.Parent;
        }

        if (src is null)
        {
            return false;
        }

        var built = Directory.EnumerateFiles(folder, "Tachyon.Charts.Configurator*.dll").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        var changed = Directory.EnumerateDirectories(src.FullName, "Tachyon.Charts.Configurator*")
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        return changed > built;
    }
}
