using System.Globalization;
using PureHDF;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Files;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// The 3D types of the configurator. They go into one 3D scene per chart, written as <c>.Scene3D(s => s …)</c>;
/// the apps load this module only when its assembly is there.
/// </summary>
public sealed class ThreeDModule : IChartModule, IDataLoaders
{
    /// <summary>The version of the 3D package the generated projects refer to.</summary>
    public const string PackageVersion = "1.0.0-preview.3";

    private static readonly string[] _colormaps = ["Viridis", "Inferno", "Magma", "Plasma", "Turbo", "Grayscale"];

    /// <inheritdoc />
    public IReadOnlyList<ChartTypeInfo> Types { get; } =
    [
        Type("surface3d", "Surface 3D", DataShape.Grid, "hill", typeof(Surface3D), nameof(Chart3DBuilder.AddSurface), (-1, 1),
            [
                Colormap("Viridis"), Lit(), Opacity(), Extent(),
                new("heightLines", "Height lines", ParameterKind.Integer, 0.0) { Minimum = 0, Maximum = 50, Description = "Lines of equal height, evenly over the Z range; 0 for none." },
                new("onWalls", "Height lines on the walls", ParameterKind.Flag, false) { Description = "The cuts through the surface also show on the walls of the box." },
                new("limit", "Limit plane", ParameterKind.Flag, false) { Description = "A red plane at a limit; where the surface crosses it is easy to see." },
                new("limitValue", "Limit at", ParameterKind.Number, 0.5),
                new("limitLine", "Line at the limit", ParameterKind.Flag, false) { Description = "A red line on the surface where it crosses the limit, instead of height lines." },
            ],
            [("colormap", nameof(Surface3D.Colormap)), ("lit", "Material.IsLit"), ("opacity", "Material.Opacity")],
            data => new Surface3D((HeatmapData)data, Box, Box), data => $"new Surface3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))",
            make: (data, values) => new Surface3D((HeatmapData)data, Span(values), Span(values)),
            write: (data, series) => $"new Surface3D({data}, {SpanCode(series.Parameters)}, {SpanCode(series.Parameters)})",
            extra: new SceneExtra(
                (created, values) =>
                {
                    if (values.GetFlag("limitLine", false))
                    {
                        typeof(Surface3D).GetProperty(nameof(Surface3D.Contours))!.SetValue(created, new Contours3D([values.GetNumber("limitValue", 0.5)]) { Color = LimitColor, Width = 2.5, OnFloor = false });
                    }
                    else if (values.GetNumber("heightLines", 0) > 0)
                    {
                        // Contours can only be given when the surface is made; the preview sets it the same way.
                        typeof(Surface3D).GetProperty(nameof(Surface3D.Contours))!.SetValue(created, HeightLines(values));
                    }
                },
                values => values.GetFlag("limitLine", false) ? $"Contours = new Contours3D([{CodeText.Number(values.GetNumber("limitValue", 0.5))}]) {{ Color = new Rgba8(255, 70, 70, 255), Width = 2.5, OnFloor = false }}"
                    : values.GetNumber("heightLines", 0) > 0 ? $"Contours = Contours3D.Even(new DoubleRange({CodeText.Number(values.GetNumber("zFrom", -1))}, {CodeText.Number(values.GetNumber("zTo", 1))}), {(int)values.GetNumber("heightLines", 0)}){(values.GetFlag("onWalls", false) ? " with { OnWalls = true }" : string.Empty)}" : null,
                (scene, values) =>
                {
                    if (values.GetFlag("limit", false))
                    {
                        scene.AddMesh(Shapes3D.Limit(Span(values), Span(values), values.GetNumber("limitValue", 0.5), LimitColor));
                    }
                },
                values => values.GetFlag("limit", false) ? $".AddMesh(Shapes3D.Limit({SpanCode(values)}, {SpanCode(values)}, {CodeText.Number(values.GetNumber("limitValue", 0.5))}, new Rgba8(255, 70, 70, 255)))" : string.Empty)),
        Type("columns3d", "Columns 3D", DataShape.Grid, "blocks", typeof(Bars3D), nameof(Chart3DBuilder.AddBars), (0, 1),
            [Colormap("Viridis"), new("width", "Width", ParameterKind.Number, 0.8) { Minimum = 0.05, Maximum = 1 }, Lit(), Opacity(), new("gloss", "Gloss", ParameterKind.Number, 0.18) { Minimum = 0, Maximum = 1, Description = "The strength of the highlights." }, Extent(), new("start", "Start", ParameterKind.Number, 0.0) { Description = "Where the grid starts across and along; it ends at the start plus the extent." }, new("baseline", "Base of the columns", ParameterKind.Number, 0.0)],
            [("colormap", nameof(Bars3D.Colormap)), ("width", nameof(Bars3D.Width)), ("lit", "Material.IsLit"), ("opacity", "Material.Opacity"), ("gloss", "Material.Gloss"), ("baseline", nameof(Bars3D.Baseline))],
            data => new Bars3D((HeatmapData)data, Box, Box), data => $"new Bars3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))",
            make: (data, values) => new Bars3D((HeatmapData)data, Span(values), Span(values)),
            write: (data, series) => $"new Bars3D({data}, {SpanCode(series.Parameters)}, {SpanCode(series.Parameters)})"),
        Type("waterfall3d", "Waterfall 3D", DataShape.Spectrogram, "waves", typeof(Waterfall3D), nameof(Chart3DBuilder.AddWaterfall), (0, 1.2),
            [Colormap("Inferno"), new("style", "Style", ParameterKind.Choice, nameof(WaterfallStyle.Surface)) { Choices = Enum.GetNames<WaterfallStyle>() }, new("spacing", "Rows between lines", ParameterKind.Integer, 8.0) { Minimum = 1, Maximum = 64 }],
            [("colormap", nameof(Waterfall3D.Colormap)), ("style", nameof(Waterfall3D.Style)), ("spacing", nameof(Waterfall3D.LineSpacing))],
            data => new Waterfall3D((SpectrogramData)data, Box, Box), data => $"new Waterfall3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))"),
        Type("line3d", "Line 3D", DataShape.Points3D, "helix", typeof(Line3D), nameof(Chart3DBuilder.AddLine), (-1, 1),
            [Color(), new("width", "Width", ParameterKind.Number, 1.5) { Minimum = 0.5, Maximum = 10 }],
            [("color", nameof(Line3D.Color)), ("width", nameof(Line3D.Width))],
            data => new Line3D(Points(data)), data => $"new Line3D(Points3D.From({data}.X, {data}.Y, {data}.Z))",
            make: (data, _) => new Line3D(data as Points3D ?? Points(data)),
            write: (data, series) => ShowroomSamples3D.IsReady(series) ? $"new Line3D({data})" : $"new Line3D(Points3D.From({data}.X, {data}.Y, {data}.Z))",
            usings: ["Tachyon.Charts.Core.Colors"]),
        Type("scatter3d", "Scatter 3D", DataShape.Points3D, "scattered", typeof(Scatter3D), nameof(Chart3DBuilder.AddScatter), (-1, 1),
            [Color(), new("size", "Size", ParameterKind.Number, 3.0) { Minimum = 1, Maximum = 30 }],
            [("color", nameof(Scatter3D.Color)), ("size", nameof(Scatter3D.Size))],
            data => new Scatter3D(Points(data)), data => $"new Scatter3D(Points3D.From({data}.X, {data}.Y, {data}.Z))",
            make: (data, _) => new Scatter3D(data as Points3D ?? Points(data)),
            write: (data, series) => ShowroomSamples3D.IsReady(series) ? $"new Scatter3D({data})" : $"new Scatter3D(Points3D.From({data}.X, {data}.Y, {data}.Z))",
            usings: ["Tachyon.Charts.Core.Colors"]),
        Type("bubbles3d", "Bubbles 3D", DataShape.Bubbles3D, "balls", typeof(Bubbles3D), nameof(Chart3DBuilder.AddBubbles), (-1, 1),
            [Color(), new("sizeUnit", "Size in", ParameterKind.Choice, nameof(BubbleSizeUnit.Data)) { Choices = Enum.GetNames<BubbleSizeUnit>(), Description = "Data: the diameters are in the units of the axes; Screen: in screen points, the same at every distance." }],
            [("color", nameof(Bubbles3D.Color)), ("sizeUnit", nameof(Bubbles3D.SizeUnit))],
            data => Balls(data), data => $"new Bubbles3D({data}.X, {data}.Y, {data}.Z, {data}.Diameters)",
            make: (data, _) => data is Bubbles3D ready ? new Bubbles3D(ready.X, ready.Y, ready.Z, ready.Diameters) { Colors = ready.Colors, Values = ready.Values } : Balls(data),
            readyInit: data => $"Colors = {data}.Colors, Values = {data}.Values",
            usings: ["Tachyon.Charts.Core.Colors", "Tachyon.Charts.Rendering"]),
        Type("cloud3d", "Point cloud", DataShape.Points3D, "dust", typeof(PointCloud3D), nameof(Chart3DBuilder.AddCloud), (-1, 1),
            [Color(), new("pointSize", "Point size", ParameterKind.Number, 2.5) { Minimum = 1, Maximum = 20 }],
            [("color", nameof(PointCloud3D.Color)), ("pointSize", nameof(PointCloud3D.PointSize))],
            data => Cloud(data), data => $"new PointCloud3D({data}.X, {data}.Y, {data}.Z)"),
        Type("map3d", "Map from measurements", DataShape.Points3D, "scattered", typeof(MeasuredMap3D), nameof(Chart3DBuilder.AddMap), (-1, 1),
            [Colormap("Viridis"), new("points", "Show the points", ParameterKind.Flag, true), Lit()],
            [("colormap", nameof(MeasuredMap3D.Colormap)), ("points", nameof(MeasuredMap3D.ShowsPoints)), ("lit", "Material.IsLit")],
            data => Map(data), data => $"new MeasuredMap3D({data}.X, {data}.Y, {data}.Z)"),
        Type("volume3d", "Volume", DataShape.Volume, "blobs", typeof(Volume3D), nameof(Chart3DBuilder.AddVolume), (0, 10),
            [new("mode", "Show", ParameterKind.Choice, nameof(VolumeMode.Transfer)) { Choices = Enum.GetNames<VolumeMode>() }],
            [("mode", nameof(Volume3D.Mode))],
            data => new Volume3D((VolumeData)data, Box, Box, Box), data => $"new Volume3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10), new DoubleRange(0, 10))"),
        Type("antenna3d", "Antenna pattern", DataShape.Pattern, "dipole", typeof(AntennaPattern3D), nameof(Chart3DBuilder.AddAntenna), null,
            [Colormap("Viridis"), new("range", "Dynamic range in dB", ParameterKind.Number, 40.0) { Minimum = 3, Maximum = 120 }, Lit()],
            [("colormap", nameof(AntennaPattern3D.Colormap)), ("range", nameof(AntennaPattern3D.DynamicRange)), ("lit", "Material.IsLit")],
            data => Antenna(data), data => $"new AntennaPattern3D({data}.Azimuths, {data}.Elevations, {data}.Gains)"),
        Pie3DType("pie3d", "Pie 3D", 0),
        Pie3DType("donut3d", "Donut 3D", 0.5),
        Model(),
        AxisBox(),
        SpectrumWaterfall(),
    ];

    /// <inheritdoc />
    public IReadOnlyList<SampleInfo> Samples { get; } = [.. Samples3D.Create(), .. ShowroomSamples3D.Create(), .. ShowroomModels3D.Create()];

    /// <inheritdoc />
    public IReadOnlyList<PageInfo> Pages => ShowroomPages3D.All;

    // A spectrum analyzer and the same measurement as a waterfall in 3D, side by side or one above the other; both show
    // the same display, so the reference level, the mask and new spectra show in both.
    private static ChartTypeInfo SpectrumWaterfall() => new("spectrumWaterfall", "Spectrum with 3D waterfall", ChartCategory.Instrument, DataShape.Spectrum, "drift",
        [
            new("layout", "Arrangement", ParameterKind.Choice, "Side by side") { Choices = ["Side by side", "One above the other"] },
            .. InstrumentTypes.SpectrumSettings(),
            Colormap("Inferno"),
            new("style", "Style", ParameterKind.Choice, nameof(WaterfallStyle.Surface)) { Choices = Enum.GetNames<WaterfallStyle>() },
            new("live", "Live spectra", ParameterKind.Flag, false) { Description = "New spectra keep coming, as from a measurement, and run back in the waterfall." },
            new("rate", "Spectra per second", ParameterKind.Number, 20.0) { Minimum = 1, Maximum = 200 },
        ],
        (chart, series, data, _) => chart.Spectrum((SpectrumDisplay)data),
        (_, _, _) => string.Empty)
    {
        Usings = ["Tachyon.Charts.Core.Axes", "Tachyon.Charts.Core.Instruments", "Tachyon.Charts.Core.Numerics", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.ThreeD", "Tachyon.Charts.ThreeD.Scene"],
        OwnsAxes = true,
        Packages = [("Tachyon.Charts.ThreeD", PackageVersion)],
        Region = (_, data) => InstrumentTypes.SpectrumRegion((SpectrumDisplay)data),
        Views = series =>
        [
            new ViewInfo("Spectrum", (chart, _, data) => chart.Spectrum((SpectrumDisplay)data), (_, data) => $"chart.Spectrum({data});"),
            new ViewInfo("Waterfall", (chart, s, data) => chart.Scene3D(scene => Waterfall(scene, (SpectrumDisplay)data, s.Parameters)), (s, data) => $"chart.Scene3D(scene => Waterfall(scene, {data}, Colormaps.{s.Parameters.GetText("colormap", "Inferno")}, WaterfallStyle.{s.Parameters.GetText("style", nameof(WaterfallStyle.Surface))}));"),
        ],
        Columns = series => series.Parameters.GetText("layout", "Side by side") == "Side by side" ? 2 : 1,
        Prepare = (series, data) => InstrumentTypes.PrepareSpectrum(series.Parameters, (SpectrumDisplay)data),
        PrepareCode = (series, data) =>
        [
            .. InstrumentTypes.PrepareSpectrumCode(series.Parameters, data),
            .. series.Parameters.GetFlag("live", false) ? [$"StartFeed({data}, {CodeText.Number(series.Parameters.GetNumber("rate", 20))});"] : Array.Empty<string>(),
        ],
        Start = (series, data) => series.Parameters.GetFlag("live", false) ? SpectrumFeed.Start((SpectrumDisplay)data, series.Parameters.GetNumber("rate", 20)) : null,
        Helpers = WaterfallCode + SpectrumFeed.StartCode,
    };

    // The waterfall of a spectrum: frequencies across, the spectra so far into the depth, the levels of the spectrum up.
    private static void Waterfall(Chart3DBuilder scene, SpectrumDisplay display, SpecValues values)
    {
        var (frequencies, levels) = InstrumentTypes.SpectrumRegion(display);
        scene.Title("Waterfall")
            .XAxis(new NumericAxis("x3d", AxisPosition.Bottom) { VisibleRange = frequencies, Title = "Frequency", LabelFormat = new NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz" } })
            .YAxis(0, 400, "Spectra ago")
            .ZAxis(levels.Min, levels.Max, "dBm")
            .AddWaterfall(new Waterfall3D(display.History, new DoubleRange(display.HistoryStart, display.HistoryStop), new DoubleRange(0, 400))
            {
                Display = display,
                Colormap = ColormapOf(values.GetText("colormap", "Inferno")),
                Style = Enum.Parse<WaterfallStyle>(values.GetText("style", nameof(WaterfallStyle.Surface))),
                ColorRange = levels,
            });
    }

    private static Colormap ColormapOf(string name) => name switch
    {
        "Viridis" => Colormaps.Viridis,
        "Magma" => Colormaps.Magma,
        "Plasma" => Colormaps.Plasma,
        "Turbo" => Colormaps.Turbo,
        "Grayscale" => Colormaps.Grayscale,
        _ => Colormaps.Inferno,
    };

    private const string WaterfallCode = """

            // The waterfall of a spectrum: frequencies across, the spectra so far into the depth, the levels of the spectrum up.
            private static void Waterfall(Chart3DBuilder scene, SpectrumDisplay display, Colormap colormap, WaterfallStyle style)
            {
                var levels = new DoubleRange(display.ReferenceLevel - (ScopeDisplay.VerticalDivisions * display.PerDivision), display.ReferenceLevel);
                scene.Title("Waterfall")
                    .XAxis(new NumericAxis("x3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(display.Start, display.Stop), Title = "Frequency", LabelFormat = new NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz" } })
                    .YAxis(0, 400, "Spectra ago")
                    .ZAxis(levels.Min, levels.Max, "dBm")
                    .AddWaterfall(new Waterfall3D(display.History, new DoubleRange(display.HistoryStart, display.HistoryStop), new DoubleRange(0, 400))
                    {
                        Display = display,
                        Colormap = colormap,
                        Style = style,
                        ColorRange = levels,
                    });
            }

        """;

    /// <inheritdoc />
    public IReadOnlyList<TemplateInfo> Templates { get; } =
    [
        new("spectrumWaterfall", "Spectrum with 3D waterfall", catalog =>
        {
            var spec = catalog.CreateSpec("spectrumWaterfall");
            spec.Series[0].Parameters.Set("live", true);
            return spec;
        }),
        new("surfaceLimit", "Surface with height lines and a limit", catalog =>
        {
            var spec = catalog.CreateSpec("surface3d");
            spec.Series[0].Parameters.Set("heightLines", 10.0);
            spec.Series[0].Parameters.Set("limit", true);
            spec.Series[0].Parameters.Set("limitValue", 0.4);
            return spec;
        }),
    ];

    /// <inheritdoc />
    IReadOnlyList<DataLoader> IDataLoaders.Loaders { get; } =
    [
        new(DataKind.File, DataShape.Points3D,
            series => Points(PointFile.Read(series.Data.Path!)),
            (series, method) => FileCode(series, method, "(double[] X, double[] Y, double[] Z)", "var points = PointFile.Read({0});\n        return (points.X, points.Y, points.Z);"),
            FileProblem, _files),
        new(DataKind.File, DataShape.Volume,
            series => VolumeFile.Read(series.Data.Path!).Data,
            (series, method) => FileCode(series, method, "VolumeData", "return VolumeFile.Read({0}).Data;"),
            FileProblem, _files),
        new(DataKind.File, DataShape.Model,
            series => ModelFile.Read(series.Data.Path!),
            (series, method) => FileCode(series, method, "ModelFileData", "return ModelFile.Read({0});"),
            FileProblem, _files),
        new(DataKind.Hdf5, DataShape.Volume, VolumeFromHdf5, VolumeFromHdf5Code, Hdf5Problem, []),
    ];

    private static readonly (string, bool)[] _files = [("Tachyon.Charts.ThreeD.Files", true)];

    private static string? FileProblem(SeriesSpec series) => series.Data.Path is { Length: > 0 } ? null : $"Choose the file of {series.Name}.";

    private static string? Hdf5Problem(SeriesSpec series) => DataSources.Hdf5Problem(series);

    private static (double[], double[], double[]) Points(PointFileData points) => (points.X, points.Y, points.Z);

    private static DataCode FileCode(SeriesSpec series, string method, string type, string body) => new(
        type,
        method,
        method + "()",
        $$"""

                /// <summary>Reads the data of {{System.Security.SecurityElement.Escape(series.Name)}} from a file.</summary>
                /// <returns>The data.</returns>
                public static {{type}} {{method}}()
                {
                    {{string.Format(CultureInfo.InvariantCulture, body, CodeText.String(series.Data.Path ?? string.Empty))}}
                }

            """,
        ["Tachyon.Charts.ThreeD.Files", "Tachyon.Charts.ThreeD.Scene"]);

    // A dataset of three dimensions, slowest first: depth, height, width.
    private static object VolumeFromHdf5(SeriesSpec series)
    {
        using var file = H5File.OpenRead(series.Data.Path!);
        var dataset = file.Dataset(series.Data.Options.GetText("dataset", string.Empty));
        var dimensions = dataset.Space.Dimensions;
        if (dimensions.Length != 3)
        {
            throw new InvalidOperationException($"A volume needs a dataset of three dimensions; this one has {dimensions.Length}.");
        }

        return new VolumeData([.. DataSources.Numbers(dataset).Select(value => (float)value)], (int)dimensions[2], (int)dimensions[1], (int)dimensions[0]);
    }

    private static DataCode VolumeFromHdf5Code(SeriesSpec series, string method) => new(
        "VolumeData",
        method,
        method + "()",
        $$"""

                /// <summary>Reads the volume of {{System.Security.SecurityElement.Escape(series.Name)}} from an HDF5 file.</summary>
                /// <returns>The data.</returns>
                public static VolumeData {{method}}()
                {
                    using var file = H5File.OpenRead({{CodeText.String(series.Data.Path ?? string.Empty)}});
                    var dataset = file.Dataset({{CodeText.String(series.Data.Options.GetText("dataset", string.Empty))}});
                    var dimensions = dataset.Space.Dimensions;
                    return new VolumeData([.. Numbers(dataset).Select(value => (float)value)], (int)dimensions[2], (int)dimensions[1], (int)dimensions[0]);
                }

            """,
        ["PureHDF", "Tachyon.Charts.ThreeD.Scene"]);

    /// <inheritdoc />
    public IReadOnlyList<SceneInfo> Scenes { get; } =
    [
        new(
            ChartCategory.ThreeD,
            [
                new("projection", "Camera", ParameterKind.Choice, nameof(CameraProjection.Perspective)) { Choices = Enum.GetNames<CameraProjection>() },
                new("cut", "Cut plane", ParameterKind.Choice, "None") { Choices = ["None", "X", "Y", "Z"], Description = "A plane across an axis that hides what lies beyond it." },
                new("cutAt", "Cut at", ParameterKind.Number, 5.0) { Description = "Where the plane crosses its axis, in the units of the axis; outside its range the plane cuts through the middle. What lies beyond the value disappears." },
                new("planes", "Further planes", ParameterKind.Text, string.Empty) { Description = "In box coordinates, where every axis spans -1 to 1: the direction to the side that stays, @ and the distance from the middle, such as -1 0 0 @ -0.35; add slice for a plane that only shows its cut. Separated by semicolons." },
                .. AxisParameters("x"),
                .. AxisParameters("y"),
                .. AxisParameters("z"),
            ],
            Apply,
            Code,
            ["Tachyon.Charts.ThreeD", "Tachyon.Charts.ThreeD.Scene", "Tachyon.Charts.Core.Numerics"],
            "Tachyon.Charts.ThreeD",
            PackageVersion),
    ];

    private static DoubleRange Box => new(0, 10);

    // The scenes whose box an earlier series set up; the following series go into it and leave the axes alone, the
    // same as the code does (see SeriesAxes.InSharedBox).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Chart3DBuilder, object> _boxes = [];

    private static bool TakesTheBox(Chart3DBuilder scene) => _boxes.TryAdd(scene, scene);

    // A model from triangles: a torus as sample, or a file of STL or OBJ; the axes follow its corners.
    private static ChartTypeInfo Model() => new(
        "model3d",
        "Model 3D",
        ChartCategory.ThreeD,
        DataShape.Model,
        "torus",
        [Color(), Lit(), Opacity(), new("gloss", "Gloss", ParameterKind.Number, 0.18) { Minimum = 0, Maximum = 1, Description = "The strength of the highlights." }, new("sharpness", "Sharpness of the highlights", ParameterKind.Number, 64.0) { Minimum = 1, Maximum = 256 },
            new("colorBy", "Color by", ParameterKind.Choice, ModelColors) { Choices = [ModelColors, "Height", "Values"], Description = "The colors of the model, or its height or the values its data bring (such as the temperature of the pipe bend) through the colormap; then the color scale shows beside the box. Data without values color by height." },
            Colormap("Viridis"),
            new("colorsFollow", "Colors follow the values", ParameterKind.Flag, true) { Description = "The colormap spans from the lowest to the highest value of the model; off for the range below." },
            new("scaleFrom", "Colors from", ParameterKind.Number, 0.0) { Description = "The value at the start of the colormap." },
            new("scaleTo", "Colors to", ParameterKind.Number, 1.0) { Description = "The value at the end of the colormap." },
            new("scaleTitle", "Title of the scale", ParameterKind.Text, string.Empty) { Description = "The name or unit of the values, such as °C." }],
        (_, _, _, _) => throw new InvalidOperationException("Model 3D goes into a 3D scene."),
        (series, data, axes) => ModelCode(series, ShowroomModels3D.HasValues(series) ? data + ".Model" : data, data, axes))
    {
        Usings = ["Tachyon.Charts.ThreeD.Files", "Tachyon.Charts.ThreeD.Scene", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.Core.Numerics"],
        OwnsAxes = true,
        Helpers = CubeCode,
        OnNewData = series =>
        {
            // Range and title set for the data before rarely fit others; values the new data do not bring become heights.
            foreach (var name in (ReadOnlySpan<string>)["colorsFollow", "scaleFrom", "scaleTo", "scaleTitle"])
            {
                series.Parameters.Remove(name);
            }

            if (series.Parameters.GetText("colorBy", ModelColors) == "Values" && !ShowroomModels3D.HasValues(series))
            {
                series.Parameters.Set("colorBy", "Height");
            }
        },
        SceneApply = (builder, series, data) =>
        {
            var (scene, values) = ((Chart3DBuilder)builder, series.Parameters);
            var (model, carried) = data is ValueTuple<ModelFileData, double[]> withValues ? (withValues.Item1, withValues.Item2) : ((ModelFileData)data, null);
            if (TakesTheBox(scene))
            {
                scene.XAxis(Cube(model, 0).From, Cube(model, 0).To, "x")
                    .YAxis(Cube(model, 1).From, Cube(model, 1).To, "y")
                    .ZAxis(Cube(model, 2).From, Cube(model, 2).To, "z");
            }

            var material = new Material3D { IsLit = values.GetFlag("lit", true), Opacity = values.GetNumber("opacity", 1), Gloss = values.GetNumber("gloss", 0.18), Sharpness = values.GetNumber("sharpness", 64) };
            scene.AddMesh(values.GetText("colorBy", ModelColors) == ModelColors
                ? model.ToMesh(SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? color : null, material)
                : Colored(model, values.GetText("colorBy", ModelColors) == "Values" && carried is not null ? carried : [.. model.Positions.Select(place => place.Z)], values, material));
        },
    };

    // Only the box with its axes, which the scene sets: for a frame to start from, or to show the axes alone.
    private static ChartTypeInfo AxisBox() => new(
        "box3d",
        "Axis box only",
        ChartCategory.ThreeD,
        DataShape.Points3D,
        "scattered",
        [],
        (_, _, _, _) => throw new InvalidOperationException("Axis box only goes into a 3D scene."),
        (_, _, _) => string.Empty)
    {
        OwnsAxes = true,
        SceneApply = (builder, _, _) => TakesTheBox((Chart3DBuilder)builder),
    };

    // A cube around the model, so that all axes share one scale and the model keeps its shape.
    private static (double From, double To) Cube(ModelFileData model, int axis)
    {
        static double Of(Point3D point, int axis) => axis switch { 0 => point.X, 1 => point.Y, _ => point.Z };
        var size = 0.0;
        for (var a = 0; a < 3; a++)
        {
            size = Math.Max(size, model.Positions.Max(p => Of(p, a)) - model.Positions.Min(p => Of(p, a)));
        }

        var middle = (model.Positions.Max(p => Of(p, axis)) + model.Positions.Min(p => Of(p, axis))) / 2;
        return (middle - (size / 2), middle + (size / 2));
    }

    private const string CubeCode = """

            // The height of each corner of a model.
            private static double[] Heights(ModelFileData model) => [.. model.Positions.Select(place => place.Z)];

            // From the lowest to the highest value; one value alone gets a little room around it.
            private static DoubleRange ValueRange(IReadOnlyList<double> values)
            {
                var (low, high) = (values.Where(double.IsFinite).DefaultIfEmpty(0).Min(), values.Where(double.IsFinite).DefaultIfEmpty(1).Max());
                return high > low ? new DoubleRange(low, high) : new DoubleRange(low - 0.5, high + 0.5);
            }

            // A cube around the model, so that all axes share one scale and the model keeps its shape.
            private static (double From, double To) Cube(ModelFileData model, int axis)
            {
                static double Of(Point3D point, int axis) => axis switch { 0 => point.X, 1 => point.Y, _ => point.Z };
                var size = 0.0;
                for (var a = 0; a < 3; a++)
                {
                    size = Math.Max(size, model.Positions.Max(p => Of(p, a)) - model.Positions.Min(p => Of(p, a)));
                }

                var middle = (model.Positions.Max(p => Of(p, axis)) + model.Positions.Min(p => Of(p, axis))) / 2;
                return (middle - (size / 2), middle + (size / 2));
            }

        """;

    // The color scale of a model whose colors come from a colormap, or none.
    private const string ModelColors = "Model colors";

    // The model colored by a value per corner, from the lowest to the highest value or over the range chosen.
    private static Mesh3D Colored(ModelFileData model, double[] perPoint, SpecValues values, Material3D material) =>
        model.ToMesh(perPoint, CoreModule.Colormap(values.GetText("colormap", "Viridis")), values.GetFlag("colorsFollow", true) ? ValueRange(perPoint) : new DoubleRange(values.GetNumber("scaleFrom", 0), values.GetNumber("scaleTo", 1)), material, ScaleTitle(values));

    // From the lowest to the highest value; one value alone gets a little room around it.
    private static DoubleRange ValueRange(IReadOnlyList<double> values)
    {
        var (low, high) = (values.Where(double.IsFinite).DefaultIfEmpty(0).Min(), values.Where(double.IsFinite).DefaultIfEmpty(1).Max());
        return high > low ? new DoubleRange(low, high) : new DoubleRange(low - 0.5, high + 0.5);
    }

    // The title of the scale; without one, colors by height name it.
    private static string? ScaleTitle(SpecValues values) =>
        values.GetText("scaleTitle", string.Empty) is { Length: > 0 } title ? title : values.GetText("colorBy", ModelColors) == "Height" ? "Height" : null;

    // The model with its box and its colors: those of the model, or its height or values through the colormap, whose
    // scale then shows beside the box. The same choice as the preview, so that scale and model always agree.
    private static string ModelCode(SeriesSpec series, string model, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var material = $"new Material3D {{ IsLit = {CodeText.Flag(values.GetFlag("lit", true))}, Opacity = {CodeText.Number(values.GetNumber("opacity", 1))}, Gloss = {CodeText.Number(values.GetNumber("gloss", 0.18))}, Sharpness = {CodeText.Number(values.GetNumber("sharpness", 64))} }}";
        var box = axes.InSharedBox ? string.Empty : $".XAxis(Cube({model}, 0).From, Cube({model}, 0).To, \"x\").YAxis(Cube({model}, 1).From, Cube({model}, 1).To, \"y\").ZAxis(Cube({model}, 2).From, Cube({model}, 2).To, \"z\")";
        var colorBy = values.GetText("colorBy", ModelColors);
        if (colorBy == ModelColors)
        {
            return box + $".AddMesh({model}.ToMesh({ModelColorCode(values)}, {material}))";
        }

        var perPoint = colorBy == "Values" && ShowroomModels3D.HasValues(series) ? $"{data}.Values" : $"Heights({model})";
        var title = ScaleTitle(values) is { } named ? CodeText.String(named) : "null";
        var range = values.GetFlag("colorsFollow", true) ? $"ValueRange({perPoint})" : $"new DoubleRange({CodeText.Number(values.GetNumber("scaleFrom", 0))}, {CodeText.Number(values.GetNumber("scaleTo", 1))})";
        if (values.GetFlag("colorsFollow", true) && !perPoint.EndsWith(".Values", StringComparison.Ordinal))
        {
            // The heights are needed twice: for the colors and their range.
            return box + $".AddMesh({model}.ToMesh(Heights({model}), Colormaps.{values.GetText("colormap", "Viridis")}, ValueRange(Heights({model})), {material}, {title}))";
        }

        return box + $".AddMesh({model}.ToMesh({perPoint}, Colormaps.{values.GetText("colormap", "Viridis")}, {range}, {material}, {title}))";
    }

    private static string ModelColorCode(SpecValues values) => SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? CodeText.Color(color) : "null";

    private static Rgba8 LimitColor => new(255, 70, 70, 255);

    // An object initializer ends with " }"; a further assignment goes in before.
    private static string WithExtra(string expression, string? assignment) =>
        assignment is null ? expression : expression[..^2].TrimEnd() + (expression.EndsWith("{  }", StringComparison.Ordinal) ? " " : ", ") + assignment + " }";

    // A pie in 3D; with a hole it is a donut.
    private static ChartTypeInfo Pie3DType(string id, string name, double hole) => Type(id, name, DataShape.Slices, "mix", typeof(Pie3D), nameof(Chart3DBuilder.AddPie), null,
        [new("labels", "Labels", ParameterKind.Choice, nameof(PieLabels.NameAndPercent)) { Choices = Enum.GetNames<PieLabels>() }, new("innerRadius", "Hole", ParameterKind.Number, hole) { Minimum = 0, Maximum = 0.9, Description = "The radius of the hole as a share of the radius; above 0 for a donut." }, new("height", "Height", ParameterKind.Number, 0.3) { Minimum = 0.02, Maximum = 1 }, new("explodeDistance", "Pulled out by", ParameterKind.Number, 0.15) { Minimum = 0, Maximum = 0.5 }, new("exploded", "Piece pulled out", ParameterKind.Integer, -1.0) { Minimum = -1, Maximum = 100, Description = "The number of the piece from 0; -1 for none." }],
        [("labels", nameof(Pie3D.Labels)), ("innerRadius", nameof(Pie3D.InnerRadius)), ("height", nameof(Pie3D.Height)), ("explodeDistance", nameof(Pie3D.ExplodeDistance)), ("exploded", nameof(Pie3D.ExplodedIndex))],
        data => Pie(data), data => $"new Pie3D({data}.Values, {data}.Names)");

    // The camera and the group first, then the series, then the cut plane, the same in the preview and in the code.
    private static void Apply(Core.Fluent.ChartBuilder chart, ChartSpec spec, Action<object> add) => chart.Scene3D(scene =>
    {
        scene.Projection(Enum.Parse<CameraProjection>(spec.Scene.GetText("projection", nameof(CameraProjection.Perspective))));
        if (spec.Group is { Length: > 0 } group)
        {
            scene.Group(group);
        }

        add(scene);
        SetAxes(scene, spec.Scene);
        if (Normal(spec.Scene) is { } normal)
        {
            // The plane lives in box coordinates, where every axis spans -1 to 1; the value is one of the axis.
            var range = normal.X > 0 ? scene.Scene.XAxis.VisibleRange : normal.Y > 0 ? scene.Scene.YAxis.VisibleRange : scene.Scene.ZAxis.VisibleRange;
            scene.AddPlane(new ClipPlane3D(normal, Offset(range, spec.Scene.GetNumber("cutAt", 5))));
        }

        foreach (var (direction, offset, slice) in Planes(spec.Scene))
        {
            scene.AddPlane(new ClipPlane3D(direction, offset) { Hides = !slice });
        }
    });

    private static (string Start, string End) Code(ChartSpec spec)
    {
        var start = $".Scene3D(s => s.Projection(CameraProjection.{spec.Scene.GetText("projection", nameof(CameraProjection.Perspective))})";
        if (spec.Group is { Length: > 0 } group)
        {
            start += $".Group({CodeText.String(group)})";
        }

        var planes = string.Concat(Planes(spec.Scene).Select(plane => $".AddPlane(new ClipPlane3D(new Point3D({CodeText.Number(plane.Direction.X)}, {CodeText.Number(plane.Direction.Y)}, {CodeText.Number(plane.Direction.Z)}), {CodeText.Number(plane.Offset)}){(plane.Slice ? " { Hides = false }" : string.Empty)})"));
        var end = AxesCode(spec.Scene) + (Normal(spec.Scene) is { } normal
            ? $".AddPlane(new ClipPlane3D(new Point3D({CodeText.Number(normal.X)}, {CodeText.Number(normal.Y)}, {CodeText.Number(normal.Z)}), s.Scene.{(normal.X > 0 ? "X" : normal.Y > 0 ? "Y" : "Z")}Axis.VisibleRange is {{ }} range && {CodeText.Number(spec.Scene.GetNumber("cutAt", 5))} >= range.Min && {CodeText.Number(spec.Scene.GetNumber("cutAt", 5))} <= range.Max ? -1 + (2 * ({CodeText.Number(spec.Scene.GetNumber("cutAt", 5))} - range.Min) / (range.Max - range.Min)) : 0))" + planes + ")"
            : planes + ")");
        return (start, end);
    }

    /// <summary>Reads the further planes of a scene; entries that cannot be read are left out.</summary>
    /// <param name="values">The settings of the scene.</param>
    /// <returns>The direction to the side that stays, the distance from the middle and whether the plane only shows its cut.</returns>
    internal static IReadOnlyList<(Point3D Direction, double Offset, bool Slice)> Planes(SpecValues values)
    {
        var planes = new List<(Point3D, double, bool)>();
        foreach (var entry in values.GetText("planes", string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('@', StringSplitOptions.TrimEntries);
            var direction = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rest = parts.Length == 2 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
            if (direction.Length == 3 && rest.Length is 1 or 2
                && double.TryParse(direction[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(direction[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && double.TryParse(direction[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                && double.TryParse(rest[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
                && (x != 0 || y != 0 || z != 0))
            {
                planes.Add((new Point3D(x, y, z), offset, rest.Length == 2 && rest[1].Equals("slice", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return planes;
    }

    // The range and title of an axis of the box, which win over those of the types.
    private static ParameterInfo[] AxisParameters(string axis) =>
    [
        new(axis + "Range", axis.ToUpperInvariant() + " axis from..to", ParameterKind.Text, string.Empty) { Description = "Such as -25..25; empty for the range of the data." },
        new(axis + "Title", axis.ToUpperInvariant() + " axis title", ParameterKind.Text, string.Empty) { Description = "Shows with a range; empty keeps the title of the data." },
    ];

    private static (double From, double To)? Range(SpecValues values, string axis)
    {
        var ends = values.GetText(axis + "Range", string.Empty).Split("..");
        return ends.Length == 2
            && double.TryParse(ends[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var from)
            && double.TryParse(ends[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var to)
            && from < to
                ? (from, to)
                : null;
    }

    private static void SetAxes(Chart3DBuilder scene, SpecValues values)
    {
        foreach (var axis in (ReadOnlySpan<string>)["x", "y", "z"])
        {
            if (Range(values, axis) is not { } range)
            {
                continue;
            }

            var current = axis switch { "x" => scene.Scene.XAxis, "y" => scene.Scene.YAxis, _ => scene.Scene.ZAxis };
            var title = values.GetText(axis + "Title", string.Empty) is { Length: > 0 } chosen ? chosen : current.Title ?? string.Empty;
            _ = axis switch
            {
                "x" => scene.XAxis(range.From, range.To, title),
                "y" => scene.YAxis(range.From, range.To, title),
                _ => scene.ZAxis(range.From, range.To, title),
            };
        }
    }

    // Without a title of its own an axis keeps the one of the type, which the code reads from the scene.
    private static string AxesCode(SpecValues values) => string.Concat(new[] { "x", "y", "z" }.Select(axis => Range(values, axis) is { } range
        ? $".{axis.ToUpperInvariant()}Axis({CodeText.Number(range.From)}, {CodeText.Number(range.To)}, {(values.GetText(axis + "Title", string.Empty) is { Length: > 0 } title ? CodeText.String(title) : $"s.Scene.{axis.ToUpperInvariant()}Axis.Title")})"
        : string.Empty));

    /// <summary>Gets where a plane across an axis stands in box coordinates: the value of the axis, or the middle when it lies outside.</summary>
    /// <param name="range">The range of the axis.</param>
    /// <param name="value">The value of the axis.</param>
    /// <returns>The offset from -1 to 1.</returns>
    internal static double Offset(DoubleRange range, double value) =>
        value >= range.Min && value <= range.Max && range.Max > range.Min ? -1 + (2 * (value - range.Min) / (range.Max - range.Min)) : 0;

    private static Point3D? Normal(SpecValues values) => values.GetText("cut", "None") switch
    {
        "X" => new Point3D(1, 0, 0),
        "Y" => new Point3D(0, 1, 0),
        "Z" => new Point3D(0, 0, 1),
        _ => null,
    };

    private static ParameterInfo Extent() => new("size", "Extent", ParameterKind.Number, 10.0) { Minimum = 1e-6, Description = "The grid covers 0 to this value across and along." };

    private static DoubleRange Span(SpecValues values) => new(values.GetNumber("start", 0), values.GetNumber("start", 0) + values.GetNumber("size", 10));

    private static string SpanCode(SpecValues values) => $"new DoubleRange({CodeText.Number(Span(values).Min)}, {CodeText.Number(Span(values).Max)})";

    private static ParameterInfo Colormap(string standard) => new("colormap", "Colormap", ParameterKind.Choice, standard) { Choices = _colormaps };

    private static ParameterInfo Lit() => new("lit", "Light", ParameterKind.Flag, true);

    private static ParameterInfo Opacity() => new("opacity", "Opacity", ParameterKind.Number, 1.0) { Minimum = 0.05, Maximum = 1 };

    private static ParameterInfo Color() => new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the type." };

    // A type that goes into the scene: the axes of the box (unless the type sets its own), then the object with its properties.
    private static Contours3D HeightLines(SpecValues values) =>
        Contours3D.Even(new DoubleRange(values.GetNumber("zFrom", -1), values.GetNumber("zTo", 1)), (int)values.GetNumber("heightLines", 0)) with { OnWalls = values.GetFlag("onWalls", false) };

    private static ChartTypeInfo Type(
        string id,
        string name,
        DataShape shape,
        string sample,
        Type type,
        string add,
        (double From, double To)? height,
        IReadOnlyList<ParameterInfo> parameters,
        (string, string)[] properties,
        Func<object, object> create,
        Func<string, string> constructor,
        SceneExtra? extra = null,
        Func<object, SpecValues, object>? make = null,
        Func<string, SeriesSpec, string>? write = null,
        Func<string, string>? readyInit = null,
        IReadOnlyList<string>? usings = null)
    {
        make ??= (data, _) => create(data);
        write ??= (data, _) => constructor(data);
        var all = height is { } z ? [.. parameters, new("zFrom", "Z from", ParameterKind.Number, z.From), new("zTo", "Z to", ParameterKind.Number, z.To)] : parameters;
        var recipe = new SeriesRecipe(type, all, properties);
        var method = typeof(Chart3DBuilder).GetMethod(add, [type]) ?? throw new ArgumentException($"The 3D builder has no {add} for {type.Name}.", nameof(add));
        return new ChartTypeInfo(
            id,
            name,
            ChartCategory.ThreeD,
            shape,
            sample,
            recipe.Parameters,
            (_, _, _, _) => throw new InvalidOperationException($"{name} goes into a 3D scene."),
            (series, data, axes) => (axes.InSharedBox ? string.Empty
                : series.Data.Kind != DataKind.Sample && shape == DataShape.Points3D ? $".XAxis({data}.X.Min(), {data}.X.Max(), \"x\").YAxis({data}.Y.Min(), {data}.Y.Max(), \"y\").ZAxis({data}.Z.Min(), {data}.Z.Max(), \"z\")"
                : ShowroomSamples3D.BoxOf(series) is { } box ? BoxCode(box)
                : Axes(series.Parameters, height)) + $".{add}({WithExtra(WithExtra(recipe.Expression(write(data, series), series), extra?.InitCode(series.Parameters)), ShowroomSamples3D.IsReady(series) ? readyInit?.Invoke(data) : null)})" + (extra?.AfterCode(series.Parameters) ?? string.Empty))
        {
            Usings = [.. recipe.Usings, .. usings ?? []],
            OwnsAxes = true,
            SceneApply = (builder, series, data) =>
            {
                var scene = (Chart3DBuilder)builder;
                if (!TakesTheBox(scene))
                {
                    // An earlier series set up the box.
                }
                else if (series.Data.Kind != DataKind.Sample && data is ValueTuple<double[], double[], double[]> points)
                {
                    scene.XAxis(points.Item1.Min(), points.Item1.Max(), "x").YAxis(points.Item2.Min(), points.Item2.Max(), "y").ZAxis(points.Item3.Min(), points.Item3.Max(), "z");
                }
                else if (ShowroomSamples3D.BoxOf(series) is { } box)
                {
                    scene.XAxis(box.X.Min, box.X.Max, "x").YAxis(box.Y.Min, box.Y.Max, "y").ZAxis(box.Z.Min, box.Z.Max, "z");
                }
                else if (height is not null)
                {
                    var values = series.Parameters;
                    scene.XAxis(0, values.GetNumber("size", 10), "x").YAxis(0, values.GetNumber("size", 10), "y").ZAxis(values.GetNumber("zFrom", height.Value.From), values.GetNumber("zTo", height.Value.To), "z");
                }

                var created = make(data, series.Parameters);
                recipe.Set(created, series);
                extra?.Init(created, series.Parameters);
                method.Invoke(scene, [created]);
                extra?.After(scene, series.Parameters);
            },
        };
    }

    private static string BoxCode((DoubleRange X, DoubleRange Y, DoubleRange Z) box) =>
        $".XAxis({CodeText.Number(box.X.Min)}, {CodeText.Number(box.X.Max)}, \"x\").YAxis({CodeText.Number(box.Y.Min)}, {CodeText.Number(box.Y.Max)}, \"y\").ZAxis({CodeText.Number(box.Z.Min)}, {CodeText.Number(box.Z.Max)}, \"z\")";

    private static string Axes(SpecValues values, (double From, double To)? height) => height is { } z
        ? string.Create(CultureInfo.InvariantCulture, $".XAxis(0, {CodeText.Number(values.GetNumber("size", 10))}, \"x\").YAxis(0, {CodeText.Number(values.GetNumber("size", 10))}, \"y\").ZAxis({CodeText.Number(values.GetNumber("zFrom", z.From))}, {CodeText.Number(values.GetNumber("zTo", z.To))}, \"z\")")
        : string.Empty;

    private static Points3D Points(object data)
    {
        var (x, y, z) = ((double[], double[], double[]))data;
        return Points3D.From(x, y, z);
    }

    private static PointCloud3D Cloud(object data)
    {
        var (x, y, z) = ((double[], double[], double[]))data;
        return new PointCloud3D(x, y, z);
    }

    private static MeasuredMap3D Map(object data)
    {
        var (x, y, z) = ((double[], double[], double[]))data;
        return new MeasuredMap3D(x, y, z);
    }

    private static Bubbles3D Balls(object data)
    {
        var (x, y, z, d) = ((double[], double[], double[], double[]))data;
        return new Bubbles3D(x, y, z, d);
    }

    private static AntennaPattern3D Antenna(object data)
    {
        var (azimuths, elevations, gains) = ((double[], double[], double[,]))data;
        return new AntennaPattern3D(azimuths, elevations, gains);
    }

    private static Pie3D Pie(object data)
    {
        var (values, names) = ((double[], string[]))data;
        return new Pie3D(values, names);
    }
}

/// <summary>What a 3D type adds beyond its properties: values only its creation takes, and objects after it.</summary>
/// <param name="Init">Sets what only the creation takes, on the new object.</param>
/// <param name="InitCode">Writes the same as one more assignment of the initializer, or <see langword="null"/> for none.</param>
/// <param name="After">Adds objects to the scene after the type.</param>
/// <param name="AfterCode">Writes the same as calls on the scene.</param>
internal sealed record SceneExtra(Action<object, SpecValues> Init, Func<SpecValues, string?> InitCode, Action<Chart3DBuilder, SpecValues> After, Func<SpecValues, string> AfterCode);
