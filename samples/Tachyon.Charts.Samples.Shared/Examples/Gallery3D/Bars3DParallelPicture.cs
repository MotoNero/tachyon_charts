using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Columns3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Bars3DParallel“ of the snapshot tests.</summary>
public static class Bars3DParallelPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Bars3DParallel";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Columns(c.Projection(CameraProjection.Orthographic), 16);
}
