using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// A surface with a plane across it and, in a 2D chart beside it, the curve where the plane cuts the surface. Both
/// charts share one <see cref="CutCurveFeed"/>: dragging the outline of the plane moves the curve, and markers set in
/// either chart show in both.
/// </summary>
internal static class CutTypes3D
{
    /// <summary>The group of the two charts, so that the cursor and the markers go across.</summary>
    public const string Group = "cut-3d";

    /// <summary>The arrangement with the 2D chart above the 3D chart.</summary>
    public const string CutAbove = "Cut above the surface";

    /// <summary>Creates the type.</summary>
    /// <returns>The type.</returns>
    public static ChartTypeInfo Create() => new("surfaceCut3d", "Surface with its cut", ChartCategory.ThreeD, DataShape.Grid, "hillAcross",
        [
            new("layout", "Arrangement", ParameterKind.Choice, "Cut below the surface") { Choices = ["Cut below the surface", CutAbove, "Side by side"] },
            new("at", "Plane at", ParameterKind.Number, 0.25) { Minimum = -1, Maximum = 1, Description = "Where the plane crosses Y, from -1 to 1 across the box. Dragging its outline moves it." },
            new("colormap", "Colormap", ParameterKind.Choice, "Viridis") { Choices = ["Viridis", "Inferno", "Magma", "Plasma", "Turbo", "Grayscale"] },
        ],
        (chart, _, data, _) => Surface(chart, (CutCurveFeed)data),
        (_, _, _) => string.Empty)
    {
        Usings = ["Tachyon.Charts.Core.Colors", "Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Numerics", "Tachyon.Charts.ThreeD", "Tachyon.Charts.ThreeD.Scene", "Tachyon.Charts.Rendering"],
        OwnsAxes = true,
        Packages = [("Tachyon.Charts.ThreeD", ThreeDModule.PackageVersion)],
        Make = (series, data) => Feed((HeatmapData)data, series.Parameters),
        Views = series =>
        {
            var surface = new ViewInfo("Surface", (chart, _, data) => Surface(chart, (CutCurveFeed)data), (_, _) => "chart.Scene3D(SurfaceWithCut);");
            var cut = new ViewInfo("Cut", (chart, _, data) => Cut(chart, (CutCurveFeed)data), (_, _) => "CutCurve(chart);");
            return series.Parameters.GetText("layout", "Cut below the surface") == CutAbove ? [cut, surface] : [surface, cut];
        },
        Columns = series => series.Parameters.GetText("layout", "Cut below the surface") == "Side by side" ? 2 : 1,
        PrepareCode = (series, data) => [$"PrepareCut({data}, {CodeText.Number(series.Parameters.GetNumber("at", 0.25))}, Colormaps.{series.Parameters.GetText("colormap", "Viridis")});"],
        Helpers = Code,
    };

    // The surface over -1 to 1 in both directions, the plane across Y that shows where it cuts without hiding anything,
    // and the curve of the cut.
    private static CutCurveFeed Feed(HeatmapData data, SpecValues values) =>
        new(new ClipPlane3D(new Point3D(0, 1, 0), values.GetNumber("at", 0.25)) { Hides = false }, new Surface3D(data, new DoubleRange(-1, 1), new DoubleRange(-1, 1)) { Colormap = ThreeDModule.ColormapOf(values.GetText("colormap", "Viridis")) });

    private static void Surface(ChartBuilder chart, CutCurveFeed cut) => chart.Scene3D(scene => scene
        .Title("Cut through a surface").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
        .AddSurface(cut.Surface)
        .AddPlane(cut.Plane)
        .AddCut(cut)
        .Group(Group));

    private static void Cut(ChartBuilder chart, CutCurveFeed cut) => chart
        .Title("Height at the plane")
        .XAxis(a => a.Numeric("x").Range(-1, 1).Title("x"))
        .YAxis(a => a.Numeric("z").Range(-1, 1).Title("z"))
        .AddLine(cut.Data, s => s.Thickness(2))
        .AddOverlay(surface => CutMarkerOverlay.For(cut, surface))
        .Group(Group)
        .Modifiers(m => m.Custom(() => new CutMarkerModifier(cut)).ZoomPan().Crosshair());

    private const string Code = """

            // The surface, the plane across it and the curve of the cut, shared by both charts.
            private static CutCurveFeed? _cut;

            // The surface over -1 to 1 in both directions and a plane across Y that shows where it cuts without hiding anything.
            private static void PrepareCut(HeatmapData data, double at, Colormap colormap) =>
                _cut = new CutCurveFeed(new ClipPlane3D(new Point3D(0, 1, 0), at) { Hides = false }, new Surface3D(data, new DoubleRange(-1, 1), new DoubleRange(-1, 1)) { Colormap = colormap });

            // The 3D chart: dragging the outline of the plane moves the curve in the other chart.
            private static void SurfaceWithCut(Chart3DBuilder scene) => scene
                .Title("Cut through a surface").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
                .AddSurface(_cut!.Surface)
                .AddPlane(_cut.Plane)
                .AddCut(_cut)
                .Group("cut-3d");

            // The 2D chart: the height along the plane; a Ctrl+click sets a marker that shows in both charts.
            private static void CutCurve(ChartBuilder chart) => chart
                .Title("Height at the plane")
                .XAxis(a => a.Numeric("x").Range(-1, 1).Title("x"))
                .YAxis(a => a.Numeric("z").Range(-1, 1).Title("z"))
                .AddLine(_cut!.Data, s => s.Thickness(2))
                .AddOverlay(surface => CutMarkerOverlay.For(_cut, surface))
                .Group("cut-3d")
                .Modifiers(m => m.Custom(() => new CutMarkerModifier(_cut)).ZoomPan().Crosshair());

        """;
}
