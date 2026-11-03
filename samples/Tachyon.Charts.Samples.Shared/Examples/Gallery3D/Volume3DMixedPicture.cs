using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.ObjectsInsideAVolumePage;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Volume3DMixed“ of the snapshot tests.</summary>
public static class Volume3DMixedPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Volume3DMixed";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => VolumeWithObjects(c);
}
