using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>
/// The medical analysis page of the showroom: the volume in 3D, the axial slice with a line and a region, the profile
/// along the line and the histogram of the region, all linked.
/// </summary>
public static class MedicalAnalysisPage
{
    private static readonly MedicalDataChoice _data = new(startsWithThorax: false);
    private static Analysis? _analysis;

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical analysis";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Data list: phantoms or real data | Drag a point of the line or the circle: profile and histogram follow | Keys D, A, C, R, F: distance, angle, circle, rectangle, freehand | Drag a point: move it | Del: delete | Ctrl+Z: undo | Drag elsewhere: window, the band in the histogram follows | Wheel: slices";

    /// <summary>Gets the data to choose from.</summary>
    public static IPageData Data => _data;

    /// <summary>Gets the slice, the profile and the histogram.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Charts { get; } =
    [
        chart => Current.Display.Axial.Configure(chart),
        chart => Current.Profile.Configure(chart),
        chart => Current.Histogram.Configure(chart),
    ];

    private static Analysis Current
    {
        get
        {
            if (_analysis is not { } analysis || analysis.Version != _data.Version)
            {
                _analysis = analysis = new Analysis(_data.CreateDisplay(), _data.Version);
            }

            return analysis;
        }
    }

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        Current.Display.Configure3D(chart);
        chart.Title(_data.Name);
    }

    /// <summary>Describes the profile chart alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureProfile(ChartBuilder chart) => Phantom().Profile.Configure(chart);

    /// <summary>Describes the histogram chart alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureHistogram(ChartBuilder chart) => Phantom().Histogram.Configure(chart);

    /// <summary>Describes the slice with the line and the region alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureSlice(ChartBuilder chart) => Phantom().Display.Axial.Configure(chart);

    private static Analysis Phantom()
    {
        var display = new MedicalMprDisplay(MedicalPhantoms.Head());
        display.ApplyPreset(CtWindowPresets.Brain);
        return new Analysis(display, -1);
    }

    // A line across the middle of the axial slice and a circle on the left of the line, where the phantom has its focus;
    // places in shares of the slice, so they fit any volume.
    private sealed class Analysis
    {
        public Analysis(MedicalMprDisplay display, int version)
        {
            (Display, Version) = (display, version);
            var axial = Display.Axial;
            var (width, height) = (axial.Width * axial.Spacing.Column, axial.Height * axial.Spacing.Row);
            var layer = axial.Measurements;
            layer.Add(MedicalMeasurementKind.Distance, [new(width * 0.12, height * 0.5), new(width * 0.88, height * 0.5)], name: "Profile line");
            layer.Add(MedicalMeasurementKind.Circle, [new(width * 0.323, height * 0.43), new(width * 0.358, height * 0.43)], name: "Focus");
            Profile = new MedicalProfile(Display.Axial);
            Histogram = new MedicalHistogram(Display.Axial) { Region = layer.Measurements[1] };
        }

        public int Version { get; }

        public MedicalMprDisplay Display { get; }

        public MedicalProfile Profile { get; }

        public MedicalHistogram Histogram { get; }
    }
}
