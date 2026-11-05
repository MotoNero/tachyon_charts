using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Bubbles3D“ of the snapshot tests: wells of three fields, volume by yield.</summary>
public static class Bubbles3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Bubbles3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => BubblesPage.Configure(c);
}
