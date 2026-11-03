namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// Marks the assembly.
/// </summary>
public static class WebGpuAssemblyMarker
{
    /// <summary>
    /// Gets the name of this assembly.
    /// </summary>
    public static string AssemblyName => typeof(WebGpuAssemblyMarker).Assembly.GetName().Name!;
}
