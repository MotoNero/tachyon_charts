using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.SurfaceOfEqualValue512Page;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Volume3DSurface“ of the snapshot tests.</summary>
public static class Volume3DSurfacePicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Volume3DSurface";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => IsoSurface(c, 128);
}
