using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A surface with a plane and the curve of its cut, shared by two 3D examples.</summary>
public static class Cuts3D
{
    /// <summary>Gets the curve of the last <see cref="SurfaceWithCut"/>, for the 2D chart below it.</summary>
    public static CutCurveFeed? LastCut { get; private set; }

    /// <summary>Describes a surface with a plane whose cut feeds a 2D chart.</summary>
    /// <param name="chart">The chart.</param>
    public static void SurfaceWithCut(Chart3DBuilder chart)
    {
        var surface = CreateHillSurface();
        var plane = new ClipPlane3D(new Point3D(0, 1, 0), 0.25) { Hides = false };
        LastCut = new CutCurveFeed(plane, surface);
        chart.Title("Cut through a surface").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
            .AddSurface(surface)
            .AddPlane(plane)
            .AddCut(LastCut)
            .Group("cut-3d");
    }
}
