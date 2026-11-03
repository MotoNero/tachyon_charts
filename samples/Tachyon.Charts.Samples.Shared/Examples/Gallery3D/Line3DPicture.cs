using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.PathOf200000PointsPage;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Line3D“ of the snapshot tests.</summary>
public static class Line3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Line3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Trajectory(c);
}
