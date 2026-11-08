using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>
/// The medical analysis page of the showroom: the volume in 3D, the axial slice with a line and a region, the profile
/// along the line and the histogram of the region, all linked.
/// </summary>
public static class MedicalAnalysisPage
{
    private static readonly Analysis _analysis = new();

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical analysis";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Drag a point of the line or the circle: profile and histogram follow | Keys D, C, R, F: new line or region | Drag elsewhere: window, the band in the histogram follows | Wheel: slices";

    /// <summary>Gets the slice, the profile and the histogram.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Charts { get; } =
    [
        _analysis.Display.Axial.Configure,
        _analysis.Profile.Configure,
        _analysis.Histogram.Configure,
    ];

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart) => _analysis.Display.Configure3D(chart);

    /// <summary>Describes the profile chart alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureProfile(ChartBuilder chart) => new Analysis().Profile.Configure(chart);

    /// <summary>Describes the histogram chart alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureHistogram(ChartBuilder chart) => new Analysis().Histogram.Configure(chart);

    /// <summary>Describes the slice with the line and the region alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureSlice(ChartBuilder chart) => new Analysis().Display.Axial.Configure(chart);

    // A line through the head from left to right and a circle on the bright focus of the middle slice.
    private sealed class Analysis
    {
        public Analysis()
        {
            Display = new MedicalMprDisplay(MedicalPhantom.Volume());
            Display.ApplyPreset(CtWindowPresets.Brain);
            var layer = Display.Axial.Measurements;
            layer.Add(MedicalMeasurementKind.Distance, [new(4, 44.8), new(98, 44.8)], name: "Profile line");
            layer.Add(MedicalMeasurementKind.Circle, [new(51.2, 44.8), new(66, 44.8)], name: "Middle");
            Profile = new MedicalProfile(Display.Axial);
            Histogram = new MedicalHistogram(Display.Axial) { Region = layer.Measurements[1] };
        }

        public MedicalMprDisplay Display { get; }

        public MedicalProfile Profile { get; }

        public MedicalHistogram Histogram { get; }
    }
}
