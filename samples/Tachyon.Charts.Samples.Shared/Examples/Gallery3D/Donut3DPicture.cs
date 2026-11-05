using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Donut3D“ of the snapshot tests: Power mix as a lit ring.</summary>
public static class Donut3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Donut3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Donut3DPage.Configure(c);
}
