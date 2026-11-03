using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>
/// Brings rendered frames to the screen. All members except Dispose run on the render thread.
/// </summary>
public interface ILineOutput : IDisposable
{
    /// <summary>
    /// Gets or sets the callback for a frame that reached WPF or the swapchain, with the input stamp
    /// passed to <see cref="Complete"/>.
    /// </summary>
    Action<long>? FrameShown { get; set; }

    /// <summary>
    /// Gets the target for the next frame.
    /// </summary>
    IRenderTarget Prepare(IRenderDevice device, PixelSize size, double scale, bool vSync);

    /// <summary>
    /// Finishes a submitted frame.
    /// </summary>
    /// <param name="target">The target from <see cref="Prepare"/>.</param>
    /// <param name="vSync">Whether to wait for the display.</param>
    /// <param name="inputStamp">The timestamp of the input this frame shows first, or 0.</param>
    void Complete(IRenderTarget target, bool vSync, long inputStamp);
}
