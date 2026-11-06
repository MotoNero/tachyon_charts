using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The medical image page of the showroom.</summary>
public static class MedicalImagePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Medical image";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Left drag: window and level | Keys 1, 2, 3: brain, bone, lung | Middle drag: pan | Wheel: zoom | Pointer: value and position";

    /// <summary>Builds the chart.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(ChartBuilder chart) => MedicalPhantom.Axial().Configure(chart);
}
