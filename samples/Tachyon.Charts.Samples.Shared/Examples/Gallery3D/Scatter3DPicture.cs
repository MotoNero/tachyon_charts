using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Clouds3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Scatter3D“ of the snapshot tests.</summary>
public static class Scatter3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Scatter3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Cloud(c, 3000);
}
