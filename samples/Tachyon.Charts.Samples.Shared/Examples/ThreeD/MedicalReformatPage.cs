using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>
/// The oblique and curved sections of the showroom: the synthetic thorax in 3D with the outline of the oblique plane, the
/// axial slice with a line and an arc along the ribs, the plane through the line and the arc stretched flat.
/// </summary>
public static class MedicalReformatPage
{
    private static readonly MedicalDataChoice _data = new(startsWithThorax: true);
    private static Sections? _sections;

    /// <summary>The name of the page.</summary>
    public const string Name = "Medical oblique and curved";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Data list: phantoms or real data | Drag an end of the line: the oblique plane follows | Drag a point of the arc: the curved section follows | Wheel on the oblique plane: move it | Key V on the oblique plane: it faces the 3D view | Keys D, A, C, R, F: distance, angle, circle, rectangle, freehand | Drag a point: move it | Del: delete | Ctrl+Z: undo | Drag elsewhere: window";

    /// <summary>Gets the data to choose from.</summary>
    public static IPageData Data => _data;

    /// <summary>Gets the axial slice, the oblique plane and the curved section.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Charts { get; } =
    [
        chart => Current.Display.Axial.Configure(chart),
        chart => Current.Oblique.Configure(chart),
        chart => Current.Curved.Configure(chart),
    ];

    private static Sections Current
    {
        get
        {
            if (_sections is not { } sections || sections.Version != _data.Version)
            {
                _sections = sections = new Sections(_data.CreateDisplay(), _data.Version);
            }

            return sections;
        }
    }

    /// <summary>Describes the volume in 3D with the outline of the oblique plane.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var sections = Current;
        sections.Display.Configure3D(chart);
        sections.Oblique.Configure3D(chart);
        sections.Oblique.ViewCamera = chart.Scene.Camera;
        chart.Title(_data.Name);
    }

    /// <summary>Describes the axial slice with the line and the arc alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureSlice(ChartBuilder chart) => Phantom().Display.Axial.Configure(chart);

    /// <summary>Describes the oblique plane alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureOblique(ChartBuilder chart) => Phantom().Oblique.Configure(chart);

    /// <summary>Describes the curved section alone, for pictures.</summary>
    /// <param name="chart">The chart.</param>
    public static void ConfigureCurved(ChartBuilder chart) => Phantom().Curved.Configure(chart);

    private static Sections Phantom() => new(new MedicalMprDisplay(MedicalPhantoms.Thorax()), -1);

    // A line across the slice from lower left to upper right, and an arc along the front of the body; places in shares
    // of the slice, so they fit any volume.
    private sealed class Sections
    {
        public Sections(MedicalMprDisplay display, int version)
        {
            (Display, Version) = (display, version);
            var axial = Display.Axial;
            var (width, height) = (axial.Width * axial.Spacing.Column, axial.Height * axial.Spacing.Row);
            var layer = axial.Measurements;
            Oblique = new MedicalObliqueSlice(Display);
            Oblique.FollowLine(axial);
            Curved = new MedicalCurvedSlice(axial);
            layer.Add(MedicalMeasurementKind.Distance, [new(width * 0.156, height * 0.73), new(width * 0.86, height * 0.35)], name: "Oblique plane");
            var arc = new MedicalPoint[13];
            for (var i = 0; i < arc.Length; i++)
            {
                var angle = Math.PI + 0.45 + ((Math.PI - 0.9) * i / (arc.Length - 1));
                arc[i] = new(width * (0.5 + (0.396 * Math.Cos(angle))), height * (0.5 + (0.285 * Math.Sin(angle))));
            }

            Curved.Path = layer.Add(MedicalMeasurementKind.Freehand, arc, name: "Arc");
        }

        public int Version { get; }

        public MedicalMprDisplay Display { get; }

        public MedicalObliqueSlice Oblique { get; }

        public MedicalCurvedSlice Curved { get; }
    }
}
