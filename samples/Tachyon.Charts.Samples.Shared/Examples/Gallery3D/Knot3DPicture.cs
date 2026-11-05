using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Knot3D“ of the snapshot tests: a model of a million triangles, colored by height.</summary>
public static class Knot3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Knot3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => ModelOf1MillionTrianglesPage.Configure(c);
}
