using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Medical;

namespace Tachyon.Charts.Configurator.Medical;

/// <summary>
/// The medical types of the configurator: an image, a plane of a volume and the volume in 3D. The preview starts with
/// phantoms; own DICOM and NIfTI data are read for the preview only, and their paths are never saved. The apps load this
/// module only when its assembly is there.
/// </summary>
public sealed partial class MedicalModule : IChartModule, IDataLoaders
{
    /// <summary>The version of the medical package the generated projects refer to.</summary>
    public const string PackageVersion = "0.1.0-preview.1";

    private const string Note = """

            // Tachyon Charts is no medical device: these pictures are not meant for diagnosis or treatment.

        """;

    private static readonly string[] _windows = ["Automatic", "Brain", "Bone", "Lung", "Custom"];
    private static readonly string[] _usings = ["Tachyon.Charts.Core.Colors", "Tachyon.Charts.Medical"];
    private static readonly string[] _volumeUsings = ["Tachyon.Charts.Core.Colors", "Tachyon.Charts.Medical", "Tachyon.Charts.Rendering", "Tachyon.Charts.ThreeD.Scene"];
    private static readonly (string, string)[] _packages = [("Tachyon.Charts.Medical", PackageVersion)];

    /// <inheritdoc />
    public IReadOnlyList<ChartTypeInfo> Types { get; } =
    [
        new("medicalImage", "Medical image", ChartCategory.Medical, DataShape.MedicalImage, "headAxial", [.. Window(), .. Measuring()],
            (chart, _, data, _) => chart.MedicalImage((MedicalImageDisplay)data),
            (_, data, _) => $".MedicalImage({data})")
        {
            Usings = _usings,
            OwnsAxes = true,
            Packages = _packages,
            TakeOver = TakeOverDrawings,
            Prepare = (series, data) => PrepareImage(series.Parameters, (MedicalImageDisplay)data),
            PrepareCode = (series, data) => PrepareImageCode(series.Parameters, data),
            Helpers = Note + MedicalSetup.ExampleMeasurementsCode,
        },
        new("medicalSlice", "Medical plane (MPR)", ChartCategory.Medical, DataShape.MedicalVolume, "headPhantom",
            [
                new("plane", "Plane", ParameterKind.Choice, nameof(MedicalMprPlane.Axial)) { Choices = Enum.GetNames<MedicalMprPlane>() },
                new("position", "Position in %", ParameterKind.Number, 50.0) { Minimum = 0, Maximum = 100, Description = "Where the plane cuts the volume, from its first to its last slice." },
                .. Window(),
                .. Segmentation(threeD: false),
                .. Measuring(),
            ],
            (chart, series, data, _) => chart.MedicalSlice(Plane((MedicalMprDisplay)data, series.Parameters)),
            (series, data, _) => $".MedicalSlice({data}.{PlaneName(series.Parameters)})")
        {
            Usings = _volumeUsings,
            OwnsAxes = true,
            Packages = _packages,
            TakeOver = TakeOverDrawings,
            Prepare = (series, data) => PrepareVolume(series, (MedicalMprDisplay)data, slice: true),
            PrepareCode = (series, data) => PrepareVolumeCode(series, data, slice: true),
            Helpers = Note + MedicalSetup.MoveToCode + MedicalSetup.PhantomSegmentationCode + MedicalSetup.ExampleMeasurementsCode,
        },
        new("medicalVolume3d", "Medical volume 3D", ChartCategory.Medical, DataShape.MedicalVolume, "headPhantom",
            [.. Window(), Memory(), .. Segmentation(threeD: true)],
            (chart, _, data, _) => chart.MedicalVolume3D((MedicalMprDisplay)data),
            (_, data, _) => $".MedicalVolume3D({data})")
        {
            Usings = _volumeUsings,
            OwnsAxes = true,
            Packages = _packages,
            TakeOver = TakeOverDrawings,
            Prepare = (series, data) => PrepareVolume(series, (MedicalMprDisplay)data, slice: false),
            PrepareCode = (series, data) => PrepareVolumeCode(series, data, slice: false),
            Helpers = Note + MedicalSetup.PhantomSegmentationCode,
        },
        Mpr(),
        Analysis(),
        Reformat(),
    ];

    /// <inheritdoc />
    public IReadOnlyList<SampleInfo> Samples { get; } =
    [
        Phantom("headPhantom", "Head phantom", nameof(MedicalPhantoms.Head)),
        Phantom("thoraxPhantom", "Thorax phantom", nameof(MedicalPhantoms.Thorax)),
        // The planes of the head through the ventricles and the focus, in millimetres.
        Image("headAxial", "Head phantom, axial image", nameof(MedicalPhantoms.Head), MedicalMprPlane.Axial, 97),
        Image("headCoronal", "Head phantom, coronal image", nameof(MedicalPhantoms.Head), MedicalMprPlane.Coronal, 81.5),
        Image("headSagittal", "Head phantom, sagittal image", nameof(MedicalPhantoms.Head), MedicalMprPlane.Sagittal, 86.5),
        Image("thoraxAxial", "Thorax phantom, axial image", nameof(MedicalPhantoms.Thorax), MedicalMprPlane.Axial),
    ];

    /// <inheritdoc />
    IReadOnlyList<DataLoader> IDataLoaders.Loaders { get; } =
    [
        new(DataKind.MedicalFile, DataShape.MedicalImage, series => MedicalFiles.ReadImage(series.Data.Path!), MedicalFiles.ImageCode, MedicalFiles.Problem, []),
        new(DataKind.MedicalFile, DataShape.MedicalVolume, series => MedicalFiles.ReadVolume(series.Data.Path!), MedicalFiles.VolumeCode, MedicalFiles.Problem, []),
    ];

    // The phantoms take a while to make; the preview makes each once and gives every chart a display of its own.
    private static readonly Dictionary<string, Lazy<MedicalVolume>> _phantoms = new()
    {
        [nameof(MedicalPhantoms.Head)] = new(MedicalPhantoms.Head),
        [nameof(MedicalPhantoms.Thorax)] = new(MedicalPhantoms.Thorax),
    };

    private static readonly string[] _colormaps = ["Grayscale", "Viridis", "Inferno", "Magma", "Plasma", "Turbo"];

    private static ParameterInfo[] Window() =>
    [
        new("colormap", "Colors", ParameterKind.Choice, "Grayscale") { Choices = _colormaps, Description = "From the lower to the upper end of the window." },
        new("interpolated", "Blend pixels", ParameterKind.Flag, true) { Description = "Blends between neighbouring values; off shows each value as a square." },
        new("window", "Window", ParameterKind.Choice, "Automatic") { Choices = _windows, Description = "The range of values from black to white; Automatic takes the one the data bring." },
        new("center", "Window center", ParameterKind.Number, 40.0) { Description = "For a custom window." },
        new("width", "Window width", ParameterKind.Number, 400.0) { Minimum = 1, Description = "For a custom window." },
    ];

    private static ParameterInfo Memory() => new("gpuMemory", "Graphics memory of the volume in MB", ParameterKind.Integer, 0.0)
    {
        Minimum = 0,
        Maximum = 1_048_576,
        Description = "0 for the usual share of the graphics card; a volume above the limit shows with less precision or fewer values.",
    };

    private static Colormap ColormapOf(SpecValues values) => values.GetText("colormap", "Grayscale") switch
    {
        "Viridis" => Colormaps.Viridis,
        "Inferno" => Colormaps.Inferno,
        "Magma" => Colormaps.Magma,
        "Plasma" => Colormaps.Plasma,
        "Turbo" => Colormaps.Turbo,
        _ => Colormaps.Grayscale,
    };

    // The colors, the blending and, for a volume, the graphics memory; nothing is set while all keep their defaults.
    private static void Look(SpecValues values, object display)
    {
        var (colormap, blends, memory) = (ColormapOf(values), values.GetFlag("interpolated", true), (long)values.GetNumber("gpuMemory", 0));
        switch (display)
        {
            case MedicalImageDisplay image:
                (image.Colormap, image.IsInterpolated) = (colormap, blends);
                break;
            case MedicalMprDisplay volume when colormap != Colormaps.Grayscale || !blends || memory > 0:
                volume.Options = new MedicalMprOptions { Colormap = colormap, IsInterpolated = blends, GpuMemoryLimit = memory > 0 ? memory * 1024 * 1024 : null };
                break;
        }
    }

    private static IEnumerable<string> LookCode(SpecValues values, string data, bool image)
    {
        var (colormap, blends, memory) = (values.GetText("colormap", "Grayscale"), values.GetFlag("interpolated", true), (long)values.GetNumber("gpuMemory", 0));
        if (image)
        {
            if (colormap != "Grayscale")
            {
                yield return $"{data}.Colormap = Colormaps.{colormap};";
            }

            if (!blends)
            {
                yield return $"{data}.IsInterpolated = false;";
            }

            yield break;
        }

        if (colormap != "Grayscale" || !blends || memory > 0)
        {
            var parts = new List<string> { $"Colormap = Colormaps.{colormap}" };
            if (!blends)
            {
                parts.Add("IsInterpolated = false");
            }

            if (memory > 0)
            {
                parts.Add($"GpuMemoryLimit = {memory}L * 1024 * 1024");
            }

            yield return $"{data}.Options = new MedicalMprOptions {{ {string.Join(", ", parts)} }};";
        }
    }

    private static ParameterInfo[] Measuring() =>
    [
        new("measurements", "Example measurements", ParameterKind.Flag, false) { Description = "A distance, an angle and a round region, placed by the size of the image." },
        .. Tools(),
    ];

    private static ParameterInfo[] Tools() =>
    [
        new("toolBar", "Tool bar in the image", ParameterKind.Flag, true) { Description = "Buttons in the lower left corner of each image: distance, angle, circle, rectangle, freehand, delete. A click on the lit tool puts it away; the drawings stay." },
        new("tool", "Tool at the start", ParameterKind.Choice, "None")
        {
            Choices = ["None", .. Enum.GetNames<MedicalMeasurementKind>()],
            Description = "What a drag in an image draws first; None moves points and the window. The tool bar and the keys D, A, C, R, F and Escape change it while measuring.",
        },
    ];

    // The tools of every image of a display, so that a drag draws in whichever image it starts.
    private static void SetTools(SpecValues values, IEnumerable<MedicalMeasurementLayer> layers)
    {
        var tool = values.GetText("tool", "None") is var name && name != "None" ? Enum.Parse<MedicalMeasurementKind>(name) : (MedicalMeasurementKind?)null;
        foreach (var layer in layers)
        {
            layer.ShowsTools = values.GetFlag("toolBar", true);
            layer.Tool = tool;
        }
    }

    private static IEnumerable<string> ToolsCode(SpecValues values, IEnumerable<string> layers)
    {
        foreach (var layer in layers)
        {
            if (values.GetFlag("toolBar", true))
            {
                yield return $"{layer}.ShowsTools = true;";
            }

            if (values.GetText("tool", "None") is var tool && tool != "None")
            {
                yield return $"{layer}.Tool = MedicalMeasurementKind.{tool};";
            }
        }
    }

    // The measurements of every image of a display: one for an image, three for the planes of a volume.
    private static IEnumerable<MedicalMeasurementLayer> Layers(object data) => data switch
    {
        MedicalImageDisplay image => [image.Measurements],
        MedicalMprDisplay display => [display.Axial.Measurements, display.Coronal.Measurements, display.Sagittal.Measurements],
        _ => [],
    };

    // Measurements drawn in the preview stay through a change of the settings.
    private static void TakeOverDrawings(SeriesSpec before, object beforeData, SeriesSpec after, object afterData)
    {
        var keepsTool = before.Parameters.GetText("tool", "None") == after.Parameters.GetText("tool", "None");
        foreach (var (old, now) in Layers(beforeData).Zip(Layers(afterData)))
        {
            MedicalSetup.TakeOver(old, now, keepsTool);
        }
    }

    private static IEnumerable<string> PlaneLayers(string data, params MedicalMprPlane[] planes) => planes.Select(plane => $"{data}.{plane}.Measurements");

    private static ParameterInfo[] Segmentation(bool threeD) => threeD
        ?
        [
            new("segmentation", "Segmentation", ParameterKind.Flag, true) { Description = "The segments of the phantom, or of the segmentation file." },
            new("opacity", "Opacity of the segments", ParameterKind.Number, 0.5) { Minimum = 0, Maximum = 1 },
            new("mode", "Segments in 3D", ParameterKind.Choice, nameof(MedicalSegmentation3DMode.Volume)) { Choices = Enum.GetNames<MedicalSegmentation3DMode>() },
        ]
        :
        [
            new("segmentation", "Segmentation", ParameterKind.Flag, true) { Description = "The segments of the phantom, or of the segmentation file." },
            new("opacity", "Opacity of the segments", ParameterKind.Number, 0.5) { Minimum = 0, Maximum = 1 },
        ];

    private static MedicalMprSlice Plane(MedicalMprDisplay display, SpecValues values) => Enum.Parse<MedicalMprPlane>(PlaneName(values)) switch
    {
        MedicalMprPlane.Axial => display.Axial,
        MedicalMprPlane.Coronal => display.Coronal,
        _ => display.Sagittal,
    };

    private static string PlaneName(SpecValues values) => values.GetText("plane", nameof(MedicalMprPlane.Axial));

    private static void SetWindow(SpecValues values, Action<MedicalWindow> preset, Action<double, double> custom)
    {
        switch (values.GetText("window", "Automatic"))
        {
            case "Brain":
                preset(CtWindowPresets.Brain);
                break;
            case "Bone":
                preset(CtWindowPresets.Bone);
                break;
            case "Lung":
                preset(CtWindowPresets.Lung);
                break;
            case "Custom":
                custom(values.GetNumber("center", 40), values.GetNumber("width", 400));
                break;
        }
    }

    private static IEnumerable<string> WindowCode(SpecValues values, string data) => values.GetText("window", "Automatic") switch
    {
        "Automatic" => [],
        "Custom" => [$"{data}.SetWindow({CodeText.Number(values.GetNumber("center", 40))}, {CodeText.Number(values.GetNumber("width", 400))});"],
        var preset => [$"{data}.ApplyPreset(CtWindowPresets.{preset});"],
    };

    private static void Measure(SpecValues values, MedicalMeasurementLayer layer, double width, double height)
    {
        if (values.GetFlag("measurements", false))
        {
            MedicalSetup.ExampleMeasurements(layer, width, height);
        }

        SetTools(values, [layer]);
    }

    private static IEnumerable<string> MeasureCode(SpecValues values, string layer, string width, string height)
    {
        if (values.GetFlag("measurements", false))
        {
            yield return $"ExampleMeasurements({layer}, {width}, {height});";
        }

        foreach (var line in ToolsCode(values, [layer]))
        {
            yield return line;
        }
    }

    private static void PrepareImage(SpecValues values, MedicalImageDisplay display)
    {
        Look(values, display);
        SetWindow(values, display.ApplyPreset, display.SetWindow);
        Measure(values, display.Measurements, display.Width * display.Spacing.Column, display.Height * display.Spacing.Row);
    }

    private static IEnumerable<string> PrepareImageCode(SpecValues values, string data) =>
        LookCode(values, data, image: true).Concat(WindowCode(values, data)).Concat(MeasureCode(values, $"{data}.Measurements", $"{data}.Width * {data}.Spacing.Column", $"{data}.Height * {data}.Spacing.Row"));

    private static void PrepareVolume(SeriesSpec series, MedicalMprDisplay display, bool slice)
    {
        var values = series.Parameters;
        Look(values, display);
        SetWindow(values, display.ApplyPreset, display.SetWindow);
        if (slice)
        {
            MedicalSetup.MoveTo(display, Enum.Parse<MedicalMprPlane>(PlaneName(values)), values.GetNumber("position", 50) / 100);
        }

        var (mode, opacity) = (Mode(values), values.GetNumber("opacity", 0.5));
        if (values.GetFlag("segmentation", true))
        {
            if (series.Data.Kind == DataKind.Sample)
            {
                display.AddSegmentation(MedicalSetup.PhantomSegmentation(display.Source, mode, opacity));
            }
            else if (series.Data.Segmentation is { Length: > 0 } file)
            {
                display.AddSegmentation(MedicalSetup.WithMode(MedicalFiles.ReadSegmentation(file), mode, opacity));
            }
        }

        if (slice)
        {
            var plane = Plane(display, values);
            Measure(values, plane.Measurements, plane.Width * plane.Spacing.Column, plane.Height * plane.Spacing.Row);
        }
    }

    private static IEnumerable<string> PrepareVolumeCode(SeriesSpec series, string data, bool slice)
    {
        var values = series.Parameters;
        var lines = new List<string>(LookCode(values, data, image: false).Concat(WindowCode(values, data)));
        if (slice)
        {
            lines.Add($"MoveTo({data}, MedicalMprPlane.{PlaneName(values)}, {CodeText.Number(values.GetNumber("position", 50) / 100)});");
        }

        var (mode, opacity) = ($"MedicalSegmentation3DMode.{Mode(values)}", CodeText.Number(values.GetNumber("opacity", 0.5)));
        if (values.GetFlag("segmentation", true))
        {
            if (series.Data.Kind == DataKind.Sample)
            {
                lines.Add($"{data}.AddSegmentation(PhantomSegmentation({data}.Source, {mode}, {opacity}));");
            }
            else if (series.Data.Segmentation is { Length: > 0 })
            {
                lines.Add($"{data}.AddSegmentation(WithMode(ReadSegmentation(ChartSetup.SegmentationData), {mode}, {opacity}));");
            }
        }

        if (slice)
        {
            var plane = $"{data}.{PlaneName(values)}";
            lines.AddRange(MeasureCode(values, $"{plane}.Measurements", $"{plane}.Width * {plane}.Spacing.Column", $"{plane}.Height * {plane}.Spacing.Row"));
        }

        return lines;
    }

    private static MedicalSegmentation3DMode Mode(SpecValues values) => Enum.Parse<MedicalSegmentation3DMode>(values.GetText("mode", nameof(MedicalSegmentation3DMode.Volume)));

    private static SampleInfo Phantom(string id, string name, string phantom) => new(
        id,
        name,
        DataShape.MedicalVolume,
        1,
        _ => new MedicalMprDisplay(_phantoms[phantom].Value),
        nameof(MedicalMprDisplay),
        method => SampleCatalog.Header("the " + name.ToLowerInvariant() + ", a synthetic CT volume", method, nameof(MedicalMprDisplay), "Not used; the phantom has a size of its own.") + $$"""
                {
                    return new MedicalMprDisplay(MedicalPhantoms.{{phantom}}());
                }

            """)
    { MinimumCount = 1, HasCount = false };

    private static SampleInfo Image(string id, string name, string phantom, MedicalMprPlane plane, double? at = null) => new(
        id,
        name,
        DataShape.MedicalImage,
        1,
        _ => Cut(_phantoms[phantom].Value, plane, at),
        nameof(MedicalImageDisplay),
        method => SampleCatalog.Header("the " + name.ToLowerInvariant() + ", one plane of a synthetic CT volume as a single image", method, nameof(MedicalImageDisplay), "Not used; the phantom has a size of its own.") + $$"""
                {
                    var volume = MedicalPhantoms.{{phantom}}();
                    var display = new MedicalMprDisplay(volume);{{PositionCode(plane, at)}}
                    var slice = display.{{plane}};
                    var values = new float[slice.Width * slice.Height];
                    slice.CopyValues(values);
                    var image = new MedicalImageDisplay(values, slice.Width, slice.Height, slice.Spacing, slice.Orientation, display.Unit);
                    image.SetWindow(display.Window.Center, display.Window.Width);
                    return image;
                }

            """)
    { MinimumCount = 1, HasCount = false };

    // One plane of a volume as a single image, the way a scanner would store it; at the middle or where given.
    private static MedicalImageDisplay Cut(MedicalVolume volume, MedicalMprPlane plane, double? at)
    {
        var display = new MedicalMprDisplay(volume);
        if (at is { } place)
        {
            display.SetPosition(Position(plane, place));
        }

        var slice = plane switch
        {
            MedicalMprPlane.Axial => display.Axial,
            MedicalMprPlane.Coronal => display.Coronal,
            _ => display.Sagittal,
        };
        var values = new float[slice.Width * slice.Height];
        slice.CopyValues(values);
        var image = new MedicalImageDisplay(values, slice.Width, slice.Height, slice.Spacing, slice.Orientation, display.Unit);
        image.SetWindow(display.Window.Center, display.Window.Width);
        return image;
    }

    private static Tachyon.Charts.ThreeD.Scene.Point3D Position(MedicalMprPlane plane, double at) => plane switch
    {
        MedicalMprPlane.Axial => new(0, 0, at),
        MedicalMprPlane.Coronal => new(0, at, 0),
        _ => new(at, 0, 0),
    };

    private static string PositionCode(MedicalMprPlane plane, double? at)
    {
        if (at is not { } place)
        {
            return string.Empty;
        }

        var point = Position(plane, place);
        return $"\n        display.SetPosition(new Tachyon.Charts.ThreeD.Scene.Point3D({CodeText.Number(point.X)}, {CodeText.Number(point.Y)}, {CodeText.Number(point.Z)}));";
    }
}
