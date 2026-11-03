namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// The kind of adapter a <see cref="WebGpuRenderDevice"/> runs on.
/// </summary>
public enum WebGpuAdapterPreference
{
    /// <summary>
    /// A real GPU, discrete before integrated.
    /// </summary>
    Hardware,

    /// <summary>
    /// A CPU implementation such as lavapipe or WARP, used for headless testing.
    /// </summary>
    Software,
}
