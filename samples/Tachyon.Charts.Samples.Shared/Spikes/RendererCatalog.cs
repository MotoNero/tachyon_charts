using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Rendering.WebGPU;

namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>
/// The fallback chain of the spikes: hardware, software adapter, Skia.
/// </summary>
public static class RendererCatalog
{
    /// <summary>
    /// The environment variable that selects the renderer.
    /// </summary>
    public const string EnvironmentVariable = "TACHYON_RENDERER";

    /// <summary>
    /// Gets the candidates, best first.
    /// </summary>
    public static IReadOnlyList<RendererCandidate> Candidates { get; } =
    [
        new(RendererPreference.Hardware, () => WebGpuRenderDevice.Create(WebGpuAdapterPreference.Hardware)),
        new(RendererPreference.Software, () => WebGpuRenderDevice.Create(WebGpuAdapterPreference.Software)),
        new(RendererPreference.Skia, () => new SkiaRenderDevice()),
    ];

    /// <summary>
    /// Selects a renderer by a name, or by the environment variable if there is none.
    /// </summary>
    public static RendererSelection Select(string? name = null) =>
        RendererSelector.Select(
            RendererSelector.ParsePreference(name ?? Environment.GetEnvironmentVariable(EnvironmentVariable)),
            Candidates);
}
