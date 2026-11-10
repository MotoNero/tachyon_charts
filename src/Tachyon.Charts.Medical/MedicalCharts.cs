using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Medical;

/// <summary>Adds medical displays to a chart description in the chain of the builder.</summary>
public static class MedicalCharts
{
    /// <summary>Describes the chart with a medical image.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="display">The image.</param>
    /// <returns>The chart, for chaining.</returns>
    public static ChartBuilder MedicalImage(this ChartBuilder chart, MedicalImageDisplay display)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(display);
        display.Configure(chart);
        return chart;
    }

    /// <summary>Describes the chart with one plane of a multiplanar display.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="slice">The plane.</param>
    /// <returns>The chart, for chaining.</returns>
    public static ChartBuilder MedicalSlice(this ChartBuilder chart, MedicalMprSlice slice)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(slice);
        slice.Configure(chart);
        return chart;
    }

    /// <summary>Describes the chart with the volume of a multiplanar display in 3D, with its segmentations.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="display">The display.</param>
    /// <returns>The chart, for chaining.</returns>
    public static ChartBuilder MedicalVolume3D(this ChartBuilder chart, MedicalMprDisplay display)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(display);
        return chart.Scene3D(display.Configure3D);
    }
}
