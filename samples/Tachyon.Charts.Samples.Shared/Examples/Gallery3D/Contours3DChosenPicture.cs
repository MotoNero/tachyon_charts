using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Contours3DChosen“ of the snapshot tests.</summary>
public static class Contours3DChosenPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Contours3DChosen";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Field(c, withGap: false, 256, new Contours3D([0, 0.5]) { Color = new Rgba8(255, 80, 80, 255), Width = 2, OnFloor = false });
}
