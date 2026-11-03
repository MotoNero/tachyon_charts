using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.VolumeSlices512Page;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Volume3DSlices“ of the snapshot tests.</summary>
public static class Volume3DSlicesPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Volume3DSlices";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Slices(c, 128);
}
