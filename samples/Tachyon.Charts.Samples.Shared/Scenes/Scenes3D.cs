using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Gallery3D;
using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>A 3D scene. It needs a GPU, so its reference pictures exist for WebGPU only.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="configure">Describes the chart.</param>
public sealed class Scene3DPicture(string name, Action<Chart3DBuilder> configure) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var scene = new Chart3DBuilder();
        configure(scene);
        var builder = new ChartBuilder();
        scene.Apply(builder);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}

/// <summary>The scenes of the 3D package.</summary>
public static class Scenes3D
{
    /// <summary>Gets the scenes.</summary>
    public static IReadOnlyList<IScene> All { get; } =
    [
        new MedicalMprScene(),
        new Scene3DPicture(Box3DPicture.Name, Box3DPicture.Configure),
        new Scene3DPicture(Box3DBehindPicture.Name, Box3DBehindPicture.Configure),
        new Scene3DPicture(Box3DTopPicture.Name, Box3DTopPicture.Configure),
        new Scene3DPicture(Box3DBelowPicture.Name, Box3DBelowPicture.Configure),
        new Scene3DPicture(Box3DOrthographicPicture.Name, Box3DOrthographicPicture.Configure),
        new Scene3DPicture(Surface3DPicture.Name, Surface3DPicture.Configure),
        new Scene3DPicture(Surface3DGapPicture.Name, Surface3DGapPicture.Configure),
        new Scene3DPicture(Contours3DPicture.Name, Contours3DPicture.Configure),
        new Scene3DPicture(Contours3DWallsPicture.Name, Contours3DWallsPicture.Configure),
        new Scene3DPicture(Contours3DChosenPicture.Name, Contours3DChosenPicture.Configure),
        new Scene3DPicture(Map3DPicture.Name, Map3DPicture.Configure),
        new Scene3DPicture(Line3DPicture.Name, Line3DPicture.Configure),
        new Scene3DPicture(Scatter3DPicture.Name, Scatter3DPicture.Configure),
        new Scene3DPicture(Plane3DPicture.Name, Plane3DPicture.Configure),
        new Scene3DPicture(Model3DPicture.Name, Model3DPicture.Configure),
        new Scene3DPicture(Knot3DPicture.Name, Knot3DPicture.Configure),
        new Scene3DPicture(Pie3DPicture.Name, Pie3DPicture.Configure),
        new Scene3DPicture(Donut3DPicture.Name, Donut3DPicture.Configure),
        new Scene3DPicture(Planes3DPicture.Name, Planes3DPicture.Configure),
        new Scene3DPicture(Bubbles3DPicture.Name, Bubbles3DPicture.Configure),
        new Scene3DPicture(Bubbles1M3DPicture.Name, Bubbles1M3DPicture.Configure),
        new Scene3DPicture(Plane3DTypesPicture.Name, Plane3DTypesPicture.Configure),
        new Scene3DPicture(Cloud3DPicture.Name, Cloud3DPicture.Configure),
        new Scene3DPicture(Cloud3DLoadingPicture.Name, Cloud3DLoadingPicture.Configure),
        new Scene3DPicture(Volume3DSlicesPicture.Name, Volume3DSlicesPicture.Configure),
        new Scene3DPicture(Volume3DThroughPicture.Name, Volume3DThroughPicture.Configure),
        new Scene3DPicture(Volume3DMixedPicture.Name, Volume3DMixedPicture.Configure),
        new Scene3DPicture(Volume3DMaximumPicture.Name, Volume3DMaximumPicture.Configure),
        new Scene3DPicture(Volume3DSurfacePicture.Name, Volume3DSurfacePicture.Configure),
        new Scene3DPicture(Coupled3DPicture.Name, Coupled3DPicture.Configure),
        new Scene3DPicture(Bars3DPicture.Name, Bars3DPicture.Configure),
        new Scene3DPicture(Bars3DParallelPicture.Name, Bars3DParallelPicture.Configure),
        new Scene3DPicture(Line3DBendsPicture.Name, Line3DBendsPicture.Configure),
        new Scene3DPicture(Antenna3DPicture.Name, Antenna3DPicture.Configure),
        new Scene3DPicture(Antenna3DPolePicture.Name, Antenna3DPolePicture.Configure),
        new Scene3DPicture(Waterfall3DPicture.Name, Waterfall3DPicture.Configure),
        new Scene3DPicture(Waterfall3DLinesPicture.Name, Waterfall3DLinesPicture.Configure),
        new Scene3DPicture(Waterfall3DMaskPicture.Name, Waterfall3DMaskPicture.Configure),
        new Scene3DPicture(Mesh3DLitPicture.Name, Mesh3DLitPicture.Configure),
        new Scene3DPicture(Mesh3DFlatPicture.Name, Mesh3DFlatPicture.Configure),
        new Scene3DPicture(Mesh3DTransparentPicture.Name, Mesh3DTransparentPicture.Configure),
        new Scene3DPicture(Box3DLogAxisPicture.Name, Box3DLogAxisPicture.Configure),
    ];

}
