using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The linked 2 × 2 medical MPR page of the showroom.</summary>
public static class MedicalMprPage
{
    private static readonly MedicalMprDisplay _display = CreateDisplay();

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical MPR";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Wheel or arrows: slices | Space: cine | +/-: speed | Home/End: first/last | Click: linked position | Drag: window/level";

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

    private static MedicalMprDisplay CreateDisplay()
    {
        var volume = MedicalPhantom.Volume();
        var display = new MedicalMprDisplay(volume);
        display.AddSegmentation(MedicalPhantom.Segmentation(volume));
        return display;
    }
}
