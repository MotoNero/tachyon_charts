using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Planes and limits“ of the showroom: a surface against a transparent limit, a test zone and an area on the floor.</summary>
public static class PlanesAndLimitsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Planes and limits";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>The limit on the third axis.</summary>
    public const double Limit = 0.9;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        var red = new Rgba8(255, 70, 70, 255);
        var range = new DoubleRange(0, 6);
        var surface = new Surface3D(ContourData.CreateField(128, false), range, range)
        {
            // The line where the surface crosses the limit.
            Contours = new Contours3D([Limit]) { Color = red, Width = 2.5, OnFloor = false },
        };

        // A slanted area with a hole above the left part, and a vertical zone where a test runs.
        var area = new Polygon(
            new PolygonRing([0.5, 3.0, 4.2, 2.6, 0.8], [0.6, 0.4, 2.2, 3.6, 2.8]),
            [new PolygonRing([1.6, 2.6, 2.2], [1.4, 1.5, 2.4])]);
        var floor = Shapes3D.Polygon(area, new Point3D(0, 0, 1.45), new Point3D(1, 0, 0), new Point3D(0, 1, 0.12), new Rgba8(6, 214, 160, 255), new Material3D { Opacity = 0.6, Gloss = 0.2 });
        var zone = Shapes3D.Rectangle(new Point3D(4.5, 0, -1.5), new Point3D(0, 6, 0), new Point3D(0, 0, 3.5), new Rgba8(58, 134, 255, 255), new Material3D { Opacity = 0.25, Gloss = 0 });

        c.Title("Surface against a limit of 0.9")
            .XAxis(0, 6, "x").YAxis(0, 6, "y").ZAxis(-1.5, 2, "z")
            .AddSurface(surface)
            .AddMesh(Shapes3D.Limit(range, range, Limit, red))
            .AddMesh(zone)
            .AddMesh(floor);
    }
}
