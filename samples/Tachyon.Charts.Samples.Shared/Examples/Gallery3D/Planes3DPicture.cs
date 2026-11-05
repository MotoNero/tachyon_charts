using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Planes3D“ of the snapshot tests: a surface against a transparent limit, a test zone and an area with a hole.</summary>
public static class Planes3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Planes3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => PlanesAndLimitsPage.Configure(c);
}
