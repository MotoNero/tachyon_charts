using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Line3DBends“ of the snapshot tests.</summary>
public static class Line3DBendsPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Line3DBends";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Bends(c);

    /// <summary>Describes sharp bends, a gap and two lines that cross, with wide lines that show the corners.</summary>
    /// <param name="chart">The chart.</param>
    public static void Bends(Chart3DBuilder chart)
    {
        double[] x = [-0.9, -0.6, -0.5, -0.2, -0.15, 0.1, double.NaN, 0.3, 0.6, 0.9];
        double[] y = [-0.5, 0.5, -0.5, 0.5, -0.5, 0.5, double.NaN, 0.5, -0.5, 0.5];
        double[] z = [-0.5, -0.5, -0.5, -0.5, -0.5, -0.5, double.NaN, -0.5, -0.5, -0.5];
        chart.Title("Bends, gap and crossing")
            .XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
            .AddLine(new Line3D(Points3D.From(x, y, z)) { Width = 9 })
            .AddLine(new Line3D(Points3D.From([-1, 1], [0, 0], [-0.3, -0.7])) { Width = 9, Color = new Tachyon.Charts.Rendering.Rgba8(240, 150, 60, 255) });
    }
}
