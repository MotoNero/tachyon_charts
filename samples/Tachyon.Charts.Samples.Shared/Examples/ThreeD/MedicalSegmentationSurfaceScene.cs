using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>A medical segmentation rendered as boundary surfaces and linked slice overlays.</summary>
public sealed class MedicalSegmentationSurfaceScene : IScene
{
    private readonly MedicalMprScene _scene = new(Name, MedicalSegmentation3DMode.Surface);

    /// <summary>The snapshot name.</summary>
    public const string Name = "MedicalSegmentationSurface";

    string IScene.Name => Name;

    /// <inheritdoc />
    public PixelSize Size => _scene.Size;

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target) => _scene.Render(device, target);
}
