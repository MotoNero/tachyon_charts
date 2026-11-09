using System.Globalization;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// The 3D types of the configurator. They go into one 3D scene per chart, written as <c>.Scene3D(s => s …)</c>;
/// the apps load this module only when its assembly is there.
/// </summary>
public sealed class ThreeDModule : IChartModule
{
    /// <summary>The version of the 3D package the generated projects refer to.</summary>
    public const string PackageVersion = "1.0.0-preview.3";

    private static readonly string[] _colormaps = ["Viridis", "Inferno", "Magma", "Plasma", "Turbo", "Grayscale"];

    /// <inheritdoc />
    public IReadOnlyList<ChartTypeInfo> Types { get; } =
    [
        Type("surface3d", "Surface 3D", DataShape.Grid, "hill", typeof(Surface3D), nameof(Chart3DBuilder.AddSurface), (-1, 1),
            [Colormap("Viridis"), Lit(), Opacity()], [("colormap", nameof(Surface3D.Colormap)), ("lit", "Material.IsLit"), ("opacity", "Material.Opacity")],
            data => new Surface3D((HeatmapData)data, Box, Box), data => $"new Surface3D({data}, new DoubleRange(0, 10), new DoubleRange(0, 10))"),
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
    ];

    /// <inheritdoc />
    public IReadOnlyList<SampleInfo> Samples { get; } = Samples3D.Create();

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
    private static ChartTypeInfo Type(string id, string name, DataShape shape, string sample, Type type, string add, (double From, double To)? height, IReadOnlyList<ParameterInfo> parameters, (string, string)[] properties, Func<object, object> create, Func<string, string> constructor)
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
            (series, data, _) => Axes(series.Parameters, height) + $".{add}({recipe.Expression(constructor(data), series)})")
        {
            Usings = recipe.Usings,
            OwnsAxes = true,
            SceneApply = (builder, series, data) =>
            {
                var scene = (Chart3DBuilder)builder;
                if (height is not null)
                {
                    var values = series.Parameters;
                    scene.XAxis(0, 10, "x").YAxis(0, 10, "y").ZAxis(values.GetNumber("zFrom", height.Value.From), values.GetNumber("zTo", height.Value.To), "z");
                }

                var created = create(data);
                recipe.Set(created, series);
                method.Invoke(scene, [created]);
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
