using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Boxes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Box3DBehind“ of the snapshot tests.</summary>
public static class Box3DBehindPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Box3DBehind";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        Box(c);
        c.Scene.Camera.Orbit(180, 0);
    }
}
