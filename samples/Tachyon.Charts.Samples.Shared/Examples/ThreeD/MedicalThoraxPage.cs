using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>A CT or MRI volume as a linked 2 × 2 page with the measuring tools: the phantoms or real data, as chosen.</summary>
public static class MedicalThoraxPage
{
    private static readonly MedicalDataChoice _data = new(startsWithThorax: true);
    private static (int Version, MedicalMprDisplay Display)? _current;

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical CT and MRI";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Data list: phantoms or real data | Wheel or arrows: slices | Click: linked position | Drag: window/level | Keys 1, 2, 3: brain, bone, lung window | Keys D, A, C, R, F: distance, angle, circle, rectangle, freehand | Drag a point: move it | Del: delete | Ctrl+Z: undo";

    /// <summary>Gets the data to choose from.</summary>
    public static IPageData Data => _data;

    /// <summary>Gets the three linked 2D chart descriptions.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Slices { get; } =
    [
        chart => Current.Axial.Configure(chart),
        chart => Current.Coronal.Configure(chart),
        chart => Current.Sagittal.Configure(chart),
    ];

    private static MedicalMprDisplay Current
    {
        get
        {
            if (_current is not { } current || current.Version != _data.Version)
            {
                _current = current = (_data.Version, _data.CreateDisplay());
            }

            return current.Display;
        }
    }

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        Current.Configure3D(chart);
        chart.Title(_data.Name);
    }

    /// <summary>Describes the axial slice of the thorax phantom in the lung window, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureAxial(ChartBuilder chart) => Phantom().Axial.Configure(chart);

    /// <summary>Describes the coronal slice of the thorax phantom in the lung window, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureCoronal(ChartBuilder chart) => Phantom().Coronal.Configure(chart);

    private static MedicalMprDisplay Phantom()
    {
        var display = new MedicalMprDisplay(MedicalPhantoms.Thorax());
        display.ApplyPreset(CtWindowPresets.Lung);
        return display;
    }
}
