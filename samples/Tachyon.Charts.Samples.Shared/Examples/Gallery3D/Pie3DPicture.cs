using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Pie3D“ of the snapshot tests: Power mix as a lit pie, Solar pulled out.</summary>
public static class Pie3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Pie3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Pie3DPage.Configure(c);
}
