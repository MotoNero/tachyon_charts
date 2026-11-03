using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Waterfalls3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Waterfall3D“ of the snapshot tests.</summary>
public static class Waterfall3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Waterfall3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Waterfall(c, WaterfallStyle.Surface);
}
