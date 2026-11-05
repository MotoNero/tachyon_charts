using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Bubbles1M3D“ of the snapshot tests: a million bubbles with a size in screen points.</summary>
public static class Bubbles1M3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Bubbles1M3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => MillionBubblesPage.Configure(c);
}
