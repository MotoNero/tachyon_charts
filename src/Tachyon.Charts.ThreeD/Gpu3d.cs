using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Rendering.WebGPU;

namespace Tachyon.Charts.ThreeD;

/// <summary>Whether this computer can show 3D charts.</summary>
/// <param name="IsAvailable">Whether a real graphics card is available.</param>
/// <param name="Adapter">The name of the graphics card, empty without one.</param>
public sealed record Gpu3dStatus(bool IsAvailable, string Adapter);

/// <summary>
/// Finds out whether 3D charts can run. They need a real graphics card: software adapters such as WARP or
/// lavapipe count as none, and there is no CPU renderer.
/// </summary>
public static class Gpu3d
{
    private const string SoftwareOnly = "software";

    private static readonly Lazy<Gpu3dStatus> _status = new(() =>
        Probe(Environment.GetEnvironmentVariable(WebGpuRenderDevice.AdapterVariable), CreateHardwareDevice));

    /// <summary>Gets the result of the check. The first call takes a moment, so call it away from the UI thread.</summary>
    public static Gpu3dStatus Status => _status.Value;

    /// <summary>Gets the text for charts that cannot show 3D, in the language of <see cref="ChartText"/>.</summary>
    public static string UnavailableText => ChartText.Language == ChartLanguage.German
        ? "3D-Charts brauchen eine Grafikkarte. Hier steht keine zur Verfügung, zum Beispiel über Remote Desktop."
        : "3D charts need a graphics card. None is available here, for example over a remote desktop.";

    /// <summary>Decides from a request for the adapter and an attempt to create the device.</summary>
    /// <param name="adapterVariable">The value of the adapter variable.</param>
    /// <param name="createDevice">Creates the device on a real graphics card and returns its name.</param>
    /// <returns>The status.</returns>
    internal static Gpu3dStatus Probe(string? adapterVariable, Func<string> createDevice)
    {
        if (string.Equals(adapterVariable, SoftwareOnly, StringComparison.OrdinalIgnoreCase))
        {
            return new Gpu3dStatus(false, string.Empty);
        }

        try
        {
            return new Gpu3dStatus(true, createDevice());
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            return new Gpu3dStatus(false, string.Empty);
        }
    }

    private static string CreateHardwareDevice()
    {
        using var device = WebGpuRenderDevice.Create(WebGpuAdapterPreference.Hardware);
        return device.Capabilities.BackendName;
    }
}
