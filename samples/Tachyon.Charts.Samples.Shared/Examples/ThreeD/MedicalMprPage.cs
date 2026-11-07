using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The linked 2 × 2 medical MPR page of the showroom.</summary>
public static class MedicalMprPage
{
    private static readonly MedicalMprDisplay _display = new(MedicalPhantom.Volume());

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical MPR";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Click a slice or the 3D volume: all crosshairs and planes follow | Drag in 2D: window/level";

    /// <summary>Gets the three linked 2D chart descriptions.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Slices { get; } =
    [
        _display.Axial.Configure,
        _display.Coronal.Configure,
        _display.Sagittal.Configure,
    ];

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart) => _display.Configure3D(chart);
}
