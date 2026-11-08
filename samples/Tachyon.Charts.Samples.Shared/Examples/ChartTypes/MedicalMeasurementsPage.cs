using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The medical measurement page of the showroom: distances, an angle and regions with statistics on the phantom.</summary>
public static class MedicalMeasurementsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Medical measurements";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Keys D, A, C, R, F: distance, angle, circle, rectangle, freehand, then drag | Drag a point: change | Delete: remove | Ctrl+Z: undo | Left drag elsewhere: window";

    /// <summary>Creates the phantom with a few measurements in place.</summary>
    /// <returns>The image.</returns>
    public static MedicalImageDisplay Create()
    {
        var display = MedicalPhantom.Axial();
        var layer = display.Measurements;

        // Across the skull low down, the angle of two ventricle tips, the round region on the left and the right ventricle.
        layer.Add(MedicalMeasurementKind.Distance, [new(55.5, 140), new(168.5, 140)], name: "Width at 140 mm");
        layer.Add(MedicalMeasurementKind.Angle, [new(98.3, 69), new(112, 120), new(125.7, 69)]);
        layer.Add(MedicalMeasurementKind.Circle, [new(75.5, 78.3), new(84.5, 78.3)], name: "Focus");
        layer.Add(MedicalMeasurementKind.Freehand, [new(125.7, 66), new(133, 75), new(134, 95), new(127, 112), new(119, 104), new(118, 80)], name: "Ventricle");
        return display;
    }

    /// <summary>Builds the chart.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(ChartBuilder chart) => Create().Configure(chart);
}
