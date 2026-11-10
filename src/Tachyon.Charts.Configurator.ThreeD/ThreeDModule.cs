using System.Globalization;
using PureHDF;
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
                Colormap("Viridis"), Lit(), Opacity(),
                new("heightLines", "Height lines", ParameterKind.Integer, 0.0) { Minimum = 0, Maximum = 50, Description = "Lines of equal height, evenly over the Z range; 0 for none." },
                new("limit", "Limit plane", ParameterKind.Flag, false) { Description = "A red plane at a limit; where the surface crosses it is easy to see." },
                new("limitValue", "Limit at", ParameterKind.Number, 0.5),
            ],
            [("colormap", nameof(Surface3D.Colormap)), ("lit", "Material.IsLit"), ("opacity", "Material.Opacity")],
            data => new Surface3D((HeatmapData)data, Box, Box), data => $"new Surface3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))",
            new SceneExtra(
                (created, values) =>
                {
                    if (values.GetNumber("heightLines", 0) > 0)
                    {
                        // Contours can only be given when the surface is made; the preview sets it the same way.
                        typeof(Surface3D).GetProperty(nameof(Surface3D.Contours))!.SetValue(created, HeightLines(values));
                    }
                },
                values => values.GetNumber("heightLines", 0) > 0 ? $"Contours = Contours3D.Even(new DoubleRange({CodeText.Number(values.GetNumber("zFrom", -1))}, {CodeText.Number(values.GetNumber("zTo", 1))}), {(int)values.GetNumber("heightLines", 0)})" : null,
                (scene, values) =>
                {
                    if (values.GetFlag("limit", false))
                    {
                        scene.AddMesh(Shapes3D.Limit(Box, Box, values.GetNumber("limitValue", 0.5), LimitColor));
                    }
                },
                values => values.GetFlag("limit", false) ? $".AddMesh(Shapes3D.Limit(new DoubleRange(0, 10), new DoubleRange(0, 10), {CodeText.Number(values.GetNumber("limitValue", 0.5))}, new Rgba8(255, 70, 70, 255)))" : string.Empty)),
        Type("columns3d", "Columns 3D", DataShape.Grid, "blocks", typeof(Bars3D), nameof(Chart3DBuilder.AddBars), (0, 1),
            [Colormap("Viridis"), new("width", "Width", ParameterKind.Number, 0.8) { Minimum = 0.05, Maximum = 1 }, Lit(), Opacity()],
            [("colormap", nameof(Bars3D.Colormap)), ("width", nameof(Bars3D.Width)), ("lit", "Material.IsLit"), ("opacity", "Material.Opacity")],
            data => new Bars3D((HeatmapData)data, Box, Box), data => $"new Bars3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))"),
        Type("waterfall3d", "Waterfall 3D", DataShape.Spectrogram, "waves", typeof(Waterfall3D), nameof(Chart3DBuilder.AddWaterfall), (0, 1.2),
            [Colormap("Inferno"), new("style", "Style", ParameterKind.Choice, nameof(WaterfallStyle.Surface)) { Choices = Enum.GetNames<WaterfallStyle>() }, new("spacing", "Rows between lines", ParameterKind.Integer, 8.0) { Minimum = 1, Maximum = 64 }],
            [("colormap", nameof(Waterfall3D.Colormap)), ("style", nameof(Waterfall3D.Style)), ("spacing", nameof(Waterfall3D.LineSpacing))],
            data => new Waterfall3D((SpectrogramData)data, Box, Box), data => $"new Waterfall3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))"),
        Type("line3d", "Line 3D", DataShape.Points3D, "helix", typeof(Line3D), nameof(Chart3DBuilder.AddLine), (-1, 1),
            [Color(), new("width", "Width", ParameterKind.Number, 1.5) { Minimum = 0.5, Maximum = 10 }],
            [("color", nameof(Line3D.Color)), ("width", nameof(Line3D.Width))],
            data => new Line3D(Points(data)), data => $"new Line3D(Points3D.From({data}.X, {data}.Y, {data}.Z))"),
        Type("scatter3d", "Scatter 3D", DataShape.Points3D, "scattered", typeof(Scatter3D), nameof(Chart3DBuilder.AddScatter), (-1, 1),
            [Color(), new("size", "Size", ParameterKind.Number, 3.0) { Minimum = 1, Maximum = 30 }],
            [("color", nameof(Scatter3D.Color)), ("size", nameof(Scatter3D.Size))],
            data => new Scatter3D(Points(data)), data => $"new Scatter3D(Points3D.From({data}.X, {data}.Y, {data}.Z))"),
        Type("bubbles3d", "Bubbles 3D", DataShape.Bubbles3D, "balls", typeof(Bubbles3D), nameof(Chart3DBuilder.AddBubbles), (-1, 1),
            [Color()], [("color", nameof(Bubbles3D.Color))],
            data => Balls(data), data => $"new Bubbles3D({data}.X, {data}.Y, {data}.Z, {data}.Diameters)"),
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
        SpectrumWaterfall(),
    ];

    /// <inheritdoc />
    public IReadOnlyList<SampleInfo> Samples { get; } = Samples3D.Create();

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
        Usings = ["Tachyon.Charts.Core.Instruments", "Tachyon.Charts.Core.Numerics", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.ThreeD", "Tachyon.Charts.ThreeD.Scene"],
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
            .XAxis(frequencies.Min, frequencies.Max, "Hz")
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
                    .XAxis(display.Start, display.Stop, "Hz")
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
                new("cutAt", "Cut at", ParameterKind.Number, 5.0) { Description = "Where the plane crosses its axis." },
            ],
            Apply,
            Code,
            ["Tachyon.Charts.ThreeD", "Tachyon.Charts.ThreeD.Scene", "Tachyon.Charts.Core.Numerics"],
            "Tachyon.Charts.ThreeD",
            PackageVersion),
    ];

    private static DoubleRange Box => new(0, 10);

    // A model from triangles: a torus as sample, or a file of STL or OBJ; the axes follow its corners.
    private static ChartTypeInfo Model() => new(
        "model3d",
        "Model 3D",
        ChartCategory.ThreeD,
        DataShape.Model,
        "torus",
        [Color(), Lit(), Opacity()],
        (_, _, _, _) => throw new InvalidOperationException("Model 3D goes into a 3D scene."),
        (series, data, _) => $".XAxis(Cube({data}, 0).From, Cube({data}, 0).To, \"x\").YAxis(Cube({data}, 1).From, Cube({data}, 1).To, \"y\").ZAxis(Cube({data}, 2).From, Cube({data}, 2).To, \"z\")"
            + $".AddMesh({data}.ToMesh({ModelColorCode(series.Parameters)}, new Material3D {{ IsLit = {CodeText.Flag(series.Parameters.GetFlag("lit", true))}, Opacity = {CodeText.Number(series.Parameters.GetNumber("opacity", 1))} }}))")
    {
        Usings = ["Tachyon.Charts.ThreeD.Files", "Tachyon.Charts.ThreeD.Scene"],
        OwnsAxes = true,
        Helpers = CubeCode,
        SceneApply = (builder, series, data) =>
        {
            var (scene, model, values) = ((Chart3DBuilder)builder, (ModelFileData)data, series.Parameters);
            scene.XAxis(Cube(model, 0).From, Cube(model, 0).To, "x")
                .YAxis(Cube(model, 1).From, Cube(model, 1).To, "y")
                .ZAxis(Cube(model, 2).From, Cube(model, 2).To, "z")
                .AddMesh(model.ToMesh(SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? color : null, new Material3D { IsLit = values.GetFlag("lit", true), Opacity = values.GetNumber("opacity", 1) }));
        },
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

    private static string ModelColorCode(SpecValues values) => SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? CodeText.Color(color) : "null";

    private static Rgba8 LimitColor => new(255, 70, 70, 255);

    // An object initializer ends with " }"; a further assignment goes in before.
    private static string WithExtra(string expression, string? assignment) =>
        assignment is null ? expression : expression[..^2].TrimEnd() + (expression.EndsWith("{  }", StringComparison.Ordinal) ? " " : ", ") + assignment + " }";

    // A pie in 3D; with a hole it is a donut.
    private static ChartTypeInfo Pie3DType(string id, string name, double hole) => Type(id, name, DataShape.Slices, "mix", typeof(Pie3D), nameof(Chart3DBuilder.AddPie), null,
        [new("labels", "Labels", ParameterKind.Choice, nameof(PieLabels.NameAndPercent)) { Choices = Enum.GetNames<PieLabels>() }, new("innerRadius", "Hole", ParameterKind.Number, hole) { Minimum = 0, Maximum = 0.9, Description = "The radius of the hole as a share of the radius; above 0 for a donut." }, new("height", "Height", ParameterKind.Number, 0.3) { Minimum = 0.02, Maximum = 1 }, new("explodeDistance", "Pulled out by", ParameterKind.Number, 0.15) { Minimum = 0, Maximum = 0.5 }],
        [("labels", nameof(Pie3D.Labels)), ("innerRadius", nameof(Pie3D.InnerRadius)), ("height", nameof(Pie3D.Height)), ("explodeDistance", nameof(Pie3D.ExplodeDistance))],
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
        if (Normal(spec.Scene) is { } normal)
        {
            scene.AddPlane(new ClipPlane3D(normal, spec.Scene.GetNumber("cutAt", 5)));
        }
    });

    private static (string Start, string End) Code(ChartSpec spec)
    {
        var start = $".Scene3D(s => s.Projection(CameraProjection.{spec.Scene.GetText("projection", nameof(CameraProjection.Perspective))})";
        if (spec.Group is { Length: > 0 } group)
        {
            start += $".Group({CodeText.String(group)})";
        }

        var end = Normal(spec.Scene) is { } normal
            ? $".AddPlane(new ClipPlane3D(new Point3D({CodeText.Number(normal.X)}, {CodeText.Number(normal.Y)}, {CodeText.Number(normal.Z)}), {CodeText.Number(spec.Scene.GetNumber("cutAt", 5))})))"
            : ")";
        return (start, end);
    }

    private static Point3D? Normal(SpecValues values) => values.GetText("cut", "None") switch
    {
        "X" => new Point3D(1, 0, 0),
        "Y" => new Point3D(0, 1, 0),
        "Z" => new Point3D(0, 0, 1),
        _ => null,
    };

    private static ParameterInfo Colormap(string standard) => new("colormap", "Colormap", ParameterKind.Choice, standard) { Choices = _colormaps };

    private static ParameterInfo Lit() => new("lit", "Light", ParameterKind.Flag, true);

    private static ParameterInfo Opacity() => new("opacity", "Opacity", ParameterKind.Number, 1.0) { Minimum = 0.05, Maximum = 1 };

    private static ParameterInfo Color() => new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the type." };

    // A type that goes into the scene: the axes of the box (unless the type sets its own), then the object with its properties.
    private static Contours3D HeightLines(SpecValues values) =>
        Contours3D.Even(new DoubleRange(values.GetNumber("zFrom", -1), values.GetNumber("zTo", 1)), (int)values.GetNumber("heightLines", 0));

    private static ChartTypeInfo Type(string id, string name, DataShape shape, string sample, Type type, string add, (double From, double To)? height, IReadOnlyList<ParameterInfo> parameters, (string, string)[] properties, Func<object, object> create, Func<string, string> constructor, SceneExtra? extra = null)
    {
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
            (series, data, _) => (series.Data.Kind != DataKind.Sample && shape == DataShape.Points3D ? $".XAxis({data}.X.Min(), {data}.X.Max(), \"x\").YAxis({data}.Y.Min(), {data}.Y.Max(), \"y\").ZAxis({data}.Z.Min(), {data}.Z.Max(), \"z\")" : Axes(series.Parameters, height)) + $".{add}({WithExtra(recipe.Expression(constructor(data), series), extra?.InitCode(series.Parameters))})" + (extra?.AfterCode(series.Parameters) ?? string.Empty))
        {
            Usings = recipe.Usings,
            OwnsAxes = true,
            SceneApply = (builder, series, data) =>
            {
                var scene = (Chart3DBuilder)builder;
                if (series.Data.Kind != DataKind.Sample && data is ValueTuple<double[], double[], double[]> points)
                {
                    scene.XAxis(points.Item1.Min(), points.Item1.Max(), "x").YAxis(points.Item2.Min(), points.Item2.Max(), "y").ZAxis(points.Item3.Min(), points.Item3.Max(), "z");
                }
                else if (height is not null)
                {
                    var values = series.Parameters;
                    scene.XAxis(0, 10, "x").YAxis(0, 10, "y").ZAxis(values.GetNumber("zFrom", height.Value.From), values.GetNumber("zTo", height.Value.To), "z");
                }

                var created = create(data);
                recipe.Set(created, series);
                extra?.Init(created, series.Parameters);
                method.Invoke(scene, [created]);
                extra?.After(scene, series.Parameters);
            },
        };
    }

    private static string Axes(SpecValues values, (double From, double To)? height) => height is { } z
        ? string.Create(CultureInfo.InvariantCulture, $".XAxis(0, 10, \"x\").YAxis(0, 10, \"y\").ZAxis({CodeText.Number(values.GetNumber("zFrom", z.From))}, {CodeText.Number(values.GetNumber("zTo", z.To))}, \"z\")")
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
