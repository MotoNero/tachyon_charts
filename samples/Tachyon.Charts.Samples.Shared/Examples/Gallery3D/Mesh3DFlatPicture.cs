using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Mesh3DFlat“ of the snapshot tests.</summary>
public static class Mesh3DFlatPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Mesh3DFlat";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Hill(c, new Material3D { IsLit = false });
}
