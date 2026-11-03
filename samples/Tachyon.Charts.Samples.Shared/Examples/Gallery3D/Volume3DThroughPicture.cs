using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Volumes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Volume3DThrough“ of the snapshot tests.</summary>
public static class Volume3DThroughPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Volume3DThrough";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => LookThrough(c, 128);
}
