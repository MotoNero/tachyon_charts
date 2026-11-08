using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>
/// The oblique and curved sections of the showroom: the synthetic thorax in 3D with the outline of the oblique plane, the
/// axial slice with a line and an arc along the ribs, the plane through the line and the arc stretched flat.
/// </summary>
public static class MedicalReformatPage
{
    private static readonly Sections _sections = new();

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical oblique and curved";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Drag an end of the line: the oblique plane follows | Drag a point of the arc: the curved section follows | Wheel on the oblique plane: move it | Key V on the oblique plane: it faces the 3D view | Drag elsewhere: window";

    /// <summary>Gets the axial slice, the oblique plane and the curved section.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Charts { get; } =
    [
        _sections.Display.Axial.Configure,
        _sections.Oblique.Configure,
        _sections.Curved.Configure,
    ];

    /// <summary>Describes the volume in 3D with the outline of the oblique plane.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart)
    {
        _sections.Display.Configure3D(chart);
        _sections.Oblique.Configure3D(chart);
        _sections.Oblique.ViewCamera = chart.Scene.Camera;
    }

    /// <summary>Describes the axial slice with the line and the arc alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureSlice(ChartBuilder chart) => new Sections().Display.Axial.Configure(chart);

    /// <summary>Describes the oblique plane alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureOblique(ChartBuilder chart) => new Sections().Oblique.Configure(chart);

    /// <summary>Describes the curved section alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureCurved(ChartBuilder chart) => new Sections().Curved.Configure(chart);

    // A line across both lungs and the heart, and an arc along the front ribs.
    private sealed class Sections
    {
        public Sections()
        {
            Display = new MedicalMprDisplay(MedicalPhantoms.Thorax());
            var layer = Display.Axial.Measurements;
            Oblique = new MedicalObliqueSlice(Display);
            Oblique.FollowLine(Display.Axial);
            Curved = new MedicalCurvedSlice(Display.Axial);
            layer.Add(MedicalMeasurementKind.Distance, [new(60, 210), new(330, 100)], name: "Oblique plane");
            var arc = new MedicalPoint[13];
            for (var i = 0; i < arc.Length; i++)
            {
                var angle = Math.PI + 0.45 + ((Math.PI - 0.9) * i / (arc.Length - 1));
                arc[i] = new(192 + (152 * Math.Cos(angle)), 144 + (82 * Math.Sin(angle)));
            }

            Curved.Path = layer.Add(MedicalMeasurementKind.Freehand, arc, name: "Ribs");
        }

        public MedicalMprDisplay Display { get; }

        public MedicalObliqueSlice Oblique { get; }

        public MedicalCurvedSlice Curved { get; }
    }
}
