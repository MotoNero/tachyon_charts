using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.MaximumProjection512Page;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Volume3DMaximum“ of the snapshot tests.</summary>
public static class Volume3DMaximumPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Volume3DMaximum";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Maximum(c, 128);
}
