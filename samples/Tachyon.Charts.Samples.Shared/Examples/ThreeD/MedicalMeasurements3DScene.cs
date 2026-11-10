using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The linked planes of the head phantom with measurements, which the 3D chart shows on their slices.</summary>
public sealed class MedicalMeasurements3DScene : IScene
{
    private readonly MedicalMprScene _scene = new(Name, null, measured: true);

    /// <summary>The name of the scene.</summary>
    public const string Name = "MedicalMeasurements3D";

    string IScene.Name => Name;

    /// <inheritdoc />
    public PixelSize Size => _scene.Size;

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target) => _scene.Render(device, target);
}
