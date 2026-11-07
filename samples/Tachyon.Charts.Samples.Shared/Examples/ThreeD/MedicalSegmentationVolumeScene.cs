using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>A medical segmentation rendered as a categorical volume.</summary>
public sealed class MedicalSegmentationVolumeScene : IScene
{
    private readonly MedicalMprScene _scene = new(Name, MedicalSegmentation3DMode.Volume);

    /// <summary>The snapshot name.</summary>
    public const string Name = "MedicalSegmentationVolume";

    string IScene.Name => Name;

    /// <inheritdoc />
    public PixelSize Size => _scene.Size;

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target) => _scene.Render(device, target);
}
