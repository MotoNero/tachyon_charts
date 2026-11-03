using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Cut planes“ of the showroom.</summary>
public static class CutPlanesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Cut planes";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the orange outline: move the plane | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Describes a surface with two planes that open it.</summary>
    /// <param name="chart">The chart.</param>
    public static void CutHill(Chart3DBuilder chart)
    {
        Hill(chart, new Material3D());
        chart.AddPlane(new ClipPlane3D(new Point3D(-1, 0, 0), -0.35)).AddPlane(new ClipPlane3D(new Point3D(-0.5, 1, 0), -0.5));
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => CutHill(c);
}
