using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The synthetic thorax of the medical package as a linked 2 × 2 page.</summary>
public static class MedicalThoraxPage
{
    private static readonly MedicalMprDisplay _display = CreateDisplay();

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical thorax phantom";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Keys 1, 2, 3: brain, bone and lung window | Wheel or arrows: slices | Click: linked position | Drag: window/level";

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

    /// <summary>Describes the axial slice alone in the lung window, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureAxial(ChartBuilder chart) => CreateDisplay().Axial.Configure(chart);

    /// <summary>Describes the coronal slice alone in the lung window, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureCoronal(ChartBuilder chart) => CreateDisplay().Coronal.Configure(chart);

    private static MedicalMprDisplay CreateDisplay()
    {
        var display = new MedicalMprDisplay(MedicalPhantoms.Thorax());
        display.ApplyPreset(CtWindowPresets.Lung);
        return display;
    }
}
