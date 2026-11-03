using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Contours3DWalls“ of the snapshot tests.</summary>
public static class Contours3DWallsPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Contours3DWalls";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Field(c, withGap: true, 256, Contours3D.Even(new DoubleRange(-1.5, 2), 13) with { OnWalls = true });
}
