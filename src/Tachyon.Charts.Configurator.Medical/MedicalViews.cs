using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Configurator.Medical;

/// <summary>
/// The medical types that fill several linked charts from one display: the planes with the volume, an analysis with
/// profile and histogram, and oblique and curved sections. A click, a window or a measurement in one chart shows in all.
/// </summary>
public sealed partial class MedicalModule
{
    private static readonly string[] _layouts = ["2 × 2", "Row", "Column"];
    private static readonly string[] _viewUsings = ["Tachyon.Charts.Medical", "Tachyon.Charts.Rendering", "Tachyon.Charts.ThreeD", "Tachyon.Charts.ThreeD.Scene"];

    /// <inheritdoc />
    public IReadOnlyList<TemplateInfo> Templates { get; } =
    [
        new("mprMeasuring", "MPR with measuring tools", catalog =>
        {
            var spec = catalog.CreateSpec("medicalMpr");
            spec.Series[0].Parameters.Set("measurements", true);
            spec.Series[0].Parameters.Set("tool", nameof(MedicalMeasurementKind.Distance));
            return spec;
        }),
        new("analysis", "Analysis with profile and histogram", catalog => catalog.CreateSpec("medicalAnalysis")),
    ];

    private static ParameterInfo Layout(string standard) => new("layout", "Arrangement", ParameterKind.Choice, standard) { Choices = _layouts, Description = "How the linked charts stand in the window." };

    private static int Columns(SeriesSpec series, int views, string standard) => series.Parameters.GetText("layout", standard) switch
    {
        "Row" => views,
        "Column" => 1,
        _ => Math.Min(2, views),
    };

    private static ViewInfo Slice(MedicalMprPlane plane) => new(
        plane.ToString(),
        (chart, _, data) => chart.MedicalSlice(PlaneOf((MedicalMprDisplay)data, plane)),
        (_, data) => $"chart.MedicalSlice({data}.{plane});");

    private static MedicalMprSlice PlaneOf(MedicalMprDisplay display, MedicalMprPlane plane) => plane switch
    {
        MedicalMprPlane.Axial => display.Axial,
        MedicalMprPlane.Coronal => display.Coronal,
        _ => display.Sagittal,
    };

    // The planes and the volume of one display; the position, the window and the segments are shared.
    private static ChartTypeInfo Mpr() => new("medicalMpr", "Medical MPR (linked)", ChartCategory.Medical, DataShape.MedicalVolume, "headPhantom",
        [
            Layout("2 × 2"),
            new("axial", "Axial", ParameterKind.Flag, true),
            new("coronal", "Coronal", ParameterKind.Flag, true),
            new("sagittal", "Sagittal", ParameterKind.Flag, true),
            new("volume", "Volume in 3D", ParameterKind.Flag, true),
            new("x", "Sagittal position in %", ParameterKind.Number, 50.0) { Minimum = 0, Maximum = 100 },
            new("y", "Coronal position in %", ParameterKind.Number, 50.0) { Minimum = 0, Maximum = 100 },
            new("z", "Axial position in %", ParameterKind.Number, 50.0) { Minimum = 0, Maximum = 100 },
            .. Window(),
            .. Segmentation(threeD: true),
            .. Measuring(),
            new("cine", "Cine", ParameterKind.Choice, "Off") { Choices = ["Off", .. Enum.GetNames<MedicalMprPlane>()], Description = "Plays the slices of a plane in a loop; Space starts and stops it in a plane." },
            new("speed", "Cine frames per second", ParameterKind.Number, 10.0) { Minimum = 1, Maximum = 120 },
        ],
        (chart, series, data, _) => MprViews(series)[0].Apply(chart, series, data),
        (_, _, _) => string.Empty)
    {
        Usings = _viewUsings,
        OwnsAxes = true,
        Packages = _packages,
        Views = MprViews,
        Columns = series => Columns(series, MprViews(series).Count, "2 × 2"),
        Prepare = (series, data) => PrepareMpr(series, (MedicalMprDisplay)data),
        PrepareCode = PrepareMprCode,
        Helpers = Note + MedicalSetup.MoveToCode + MedicalSetup.PhantomSegmentationCode + MedicalSetup.ExampleMeasurementsCode,
    };

    private static IReadOnlyList<ViewInfo> MprViews(SeriesSpec series)
    {
        var values = series.Parameters;
        var views = new List<ViewInfo>();
        foreach (var plane in Enum.GetValues<MedicalMprPlane>())
        {
            if (values.GetFlag(plane.ToString().ToLowerInvariant(), true))
            {
                views.Add(Slice(plane));
            }
        }

        if (values.GetFlag("volume", true))
        {
            views.Add(new ViewInfo("Volume", (chart, _, data) => chart.MedicalVolume3D((MedicalMprDisplay)data), (_, data) => $"chart.MedicalVolume3D({data});"));
        }

        // Without any chart chosen the axial plane stays.
        return views.Count > 0 ? views : [Slice(MedicalMprPlane.Axial)];
    }

    private static void PrepareMpr(SeriesSpec series, MedicalMprDisplay display)
    {
        var values = series.Parameters;
        SetWindow(values, display.ApplyPreset, display.SetWindow);
        MedicalSetup.MoveTo(display, MedicalMprPlane.Sagittal, values.GetNumber("x", 50) / 100);
        MedicalSetup.MoveTo(display, MedicalMprPlane.Coronal, values.GetNumber("y", 50) / 100);
        MedicalSetup.MoveTo(display, MedicalMprPlane.Axial, values.GetNumber("z", 50) / 100);
        AddSegmentation(series, display);
        var axial = display.Axial;
        Measure(values, axial.Measurements, axial.Width * axial.Spacing.Column, axial.Height * axial.Spacing.Row);
        display.Cine.FramesPerSecond = values.GetNumber("speed", 10);
        if (values.GetText("cine", "Off") is var cine && cine != "Off")
        {
            display.Cine.Play(Enum.Parse<MedicalMprPlane>(cine));
        }
    }

    private static IEnumerable<string> PrepareMprCode(SeriesSpec series, string data)
    {
        var values = series.Parameters;
        var lines = new List<string>(WindowCode(values, data))
        {
            $"MoveTo({data}, MedicalMprPlane.Sagittal, {CodeText.Number(values.GetNumber("x", 50) / 100)});",
            $"MoveTo({data}, MedicalMprPlane.Coronal, {CodeText.Number(values.GetNumber("y", 50) / 100)});",
            $"MoveTo({data}, MedicalMprPlane.Axial, {CodeText.Number(values.GetNumber("z", 50) / 100)});",
        };
        lines.AddRange(SegmentationCode(series, data));
        lines.AddRange(MeasureCode(values, $"{data}.Axial.Measurements", $"{data}.Axial.Width * {data}.Axial.Spacing.Column", $"{data}.Axial.Height * {data}.Axial.Spacing.Row"));
        lines.Add($"{data}.Cine.FramesPerSecond = {CodeText.Number(values.GetNumber("speed", 10))};");
        if (values.GetText("cine", "Off") is var cine && cine != "Off")
        {
            lines.Add($"{data}.Cine.Play(MedicalMprPlane.{cine});");
        }

        return lines;
    }

    // A plane with a line across and a round region; the profile follows the line, the histogram the region.
    private static ChartTypeInfo Analysis() => new("medicalAnalysis", "Medical analysis (profile, histogram)", ChartCategory.Medical, DataShape.MedicalVolume, "headPhantom",
        [
            Layout("2 × 2"),
            .. Window(),
            .. Segmentation(threeD: false),
            new("bins", "Histogram bins", ParameterKind.Integer, 512.0) { Minimum = 8, Maximum = 4096 },
        ],
        (chart, series, data, _) => AnalysisViews(series)[0].Apply(chart, series, data),
        (_, _, _) => string.Empty)
    {
        Usings = _viewUsings,
        OwnsAxes = true,
        Packages = _packages,
        Views = AnalysisViews,
        Columns = series => Columns(series, 3, "2 × 2"),
        Prepare = (series, data) =>
        {
            var display = (MedicalMprDisplay)data;
            SetWindow(series.Parameters, display.ApplyPreset, display.SetWindow);
            AddSegmentation(series, display);
            MedicalSetup.AnalysisMeasurements(display.Axial);
        },
        PrepareCode = (series, data) => [.. WindowCode(series.Parameters, data), .. SegmentationCode(series, data), $"AnalysisMeasurements({data}.Axial);"],
        Helpers = Note + MedicalSetup.PhantomSegmentationCode + MedicalSetup.AnalysisMeasurementsCode,
    };

    private static IReadOnlyList<ViewInfo> AnalysisViews(SeriesSpec series) =>
    [
        Slice(MedicalMprPlane.Axial),
        new("Profile", (chart, _, data) => new MedicalProfile(((MedicalMprDisplay)data).Axial).Configure(chart), (_, data) => $"new MedicalProfile({data}.Axial).Configure(chart);"),
        new(
            "Histogram",
            (chart, _, data) => new MedicalHistogram(((MedicalMprDisplay)data).Axial, Bins(series)) { Region = MedicalSetup.Named(((MedicalMprDisplay)data).Axial, "Focus") }.Configure(chart),
            (_, data) => $"new MedicalHistogram({data}.Axial, {Bins(series)}) {{ Region = Named({data}.Axial, \"Focus\") }}.Configure(chart);"),
    ];

    private static int Bins(SeriesSpec series) => (int)series.Parameters.GetNumber("bins", 512);

    // A line for the oblique plane and an arc for the curved section, on the axial plane; in 3D the oblique plane shows too.
    private static ChartTypeInfo Reformat() => new("medicalReformat", "Medical oblique and curved", ChartCategory.Medical, DataShape.MedicalVolume, "thoraxPhantom",
        [
            Layout("2 × 2"),
            new("volume", "Volume in 3D", ParameterKind.Flag, true) { Description = "With the oblique plane in it; the key V on the oblique plane turns it to face the 3D view." },
            .. Window(),
        ],
        (chart, series, data, _) => ReformatViews(series)[0].Apply(chart, series, data),
        (_, _, _) => string.Empty)
    {
        Usings = _viewUsings,
        OwnsAxes = true,
        Packages = _packages,
        Views = ReformatViews,
        Columns = series => Columns(series, ReformatViews(series).Count, "2 × 2"),
        Prepare = (series, data) =>
        {
            var display = (MedicalMprDisplay)data;
            SetWindow(series.Parameters, display.ApplyPreset, display.SetWindow);
            MedicalSetup.Oblique(display);
            MedicalSetup.ReformatMeasurements(display.Axial);
        },
        PrepareCode = (series, data) => [.. WindowCode(series.Parameters, data), $"Oblique({data});", $"ReformatMeasurements({data}.Axial);"],
        Helpers = Note + MedicalSetup.ReformatCode,
    };

    private static IReadOnlyList<ViewInfo> ReformatViews(SeriesSpec series)
    {
        var views = new List<ViewInfo>
        {
            Slice(MedicalMprPlane.Axial),
            new("Oblique", (chart, _, data) => MedicalSetup.Oblique((MedicalMprDisplay)data).Configure(chart), (_, data) => $"Oblique({data}).Configure(chart);"),
            new(
                "Curved",
                (chart, _, data) => new MedicalCurvedSlice(((MedicalMprDisplay)data).Axial) { Path = MedicalSetup.Named(((MedicalMprDisplay)data).Axial, "Arc") }.Configure(chart),
                (_, data) => $"new MedicalCurvedSlice({data}.Axial) {{ Path = Named({data}.Axial, \"Arc\") }}.Configure(chart);"),
        };
        if (series.Parameters.GetFlag("volume", true))
        {
            views.Add(new ViewInfo(
                "Volume",
                (chart, _, data) => chart.Scene3D(scene => MedicalSetup.WithOblique((MedicalMprDisplay)data, scene)),
                (_, data) => $"chart.Scene3D(scene => WithOblique({data}, scene));"));
        }

        return views;
    }

    private static void AddSegmentation(SeriesSpec series, MedicalMprDisplay display)
    {
        var values = series.Parameters;
        if (!values.GetFlag("segmentation", true))
        {
            return;
        }

        var (mode, opacity) = (Mode(values), values.GetNumber("opacity", 0.5));
        if (series.Data.Kind == DataKind.Sample)
        {
            display.AddSegmentation(MedicalSetup.PhantomSegmentation(display.Source, mode, opacity));
        }
        else if (series.Data.Segmentation is { Length: > 0 } file)
        {
            display.AddSegmentation(MedicalSetup.WithMode(MedicalFiles.ReadSegmentation(file), mode, opacity));
        }
    }

    private static IEnumerable<string> SegmentationCode(SeriesSpec series, string data)
    {
        var values = series.Parameters;
        if (!values.GetFlag("segmentation", true))
        {
            yield break;
        }

        var (mode, opacity) = ($"MedicalSegmentation3DMode.{Mode(values)}", CodeText.Number(values.GetNumber("opacity", 0.5)));
        if (series.Data.Kind == DataKind.Sample)
        {
            yield return $"{data}.AddSegmentation(PhantomSegmentation({data}.Source, {mode}, {opacity}));";
        }
        else if (series.Data.Segmentation is { Length: > 0 })
        {
            yield return $"{data}.AddSegmentation(WithMode(ReadSegmentation(ChartSetup.SegmentationData), {mode}, {opacity}));";
        }
    }
}
