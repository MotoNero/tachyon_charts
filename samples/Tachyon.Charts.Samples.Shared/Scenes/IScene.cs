using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// A named scene that sample apps, snapshot tool and tests render identically.
/// </summary>
public interface IScene
{
    /// <summary>
    /// Gets the scene name, also the name of its snapshot file.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the size to render at.
    /// </summary>
    PixelSize Size { get; }

    /// <summary>
    /// Renders one frame.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="target">The render target of <paramref name="device"/>.</param>
    void Render(IRenderDevice device, IRenderTarget target);
}
