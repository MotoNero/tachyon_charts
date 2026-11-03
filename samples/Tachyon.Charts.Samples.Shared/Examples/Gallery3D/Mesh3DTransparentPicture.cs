using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Mesh3DTransparent“ of the snapshot tests.</summary>
public static class Mesh3DTransparentPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Mesh3DTransparent";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Crossing(c);

    private static void Crossing(Chart3DBuilder chart) => chart
        .Title("Transparent surfaces")
        .XAxis(-1, 1, "x")
        .YAxis(-1, 1, "y")
        .ZAxis(-1, 1, "z")
        .AddMesh(CreateSurface((_, y) => y * 0.8, _ => new Rgba8(240, 90, 90, 255), new Material3D { Opacity = 0.5 }))
        .AddMesh(CreateSurface((_, y) => -y * 0.8, _ => new Rgba8(80, 170, 240, 255), new Material3D { Opacity = 0.5 }))
        .AddMesh(CreateSurface((x, y) => (0.25 * Math.Cos(x * 4) * Math.Cos(y * 4)) - 0.2, _ => new Rgba8(255, 200, 90, 255), new Material3D()));
}
