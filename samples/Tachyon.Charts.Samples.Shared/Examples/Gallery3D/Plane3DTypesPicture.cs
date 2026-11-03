using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.OnePlaneAllKindsPage;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Plane3DTypes“ of the snapshot tests.</summary>
public static class Plane3DTypesPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Plane3DTypes";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => CutTypes(c);
}
