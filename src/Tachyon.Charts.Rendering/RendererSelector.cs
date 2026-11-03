namespace Tachyon.Charts.Rendering;

/// <summary>
/// Which renderer to use.
/// </summary>
public enum RendererPreference
{
    /// <summary>
    /// The best renderer that works: hardware, then software adapter, then Skia.
    /// </summary>
    Auto,

    /// <summary>
    /// Only a real GPU.
    /// </summary>
    Hardware,

    /// <summary>
    /// Only a GPU implementation on the CPU, such as WARP or lavapipe.
    /// </summary>
    Software,

    /// <summary>
    /// Only the Skia CPU renderer.
    /// </summary>
    Skia,
}

/// <summary>
/// A renderer the selector can try.
/// </summary>
/// <param name="Kind">The kind of renderer, not <see cref="RendererPreference.Auto"/>.</param>
/// <param name="Create">Creates the device, throwing if that is not possible.</param>
public sealed record RendererCandidate(RendererPreference Kind, Func<IRenderDevice> Create);

/// <summary>
/// One tried renderer.
/// </summary>
/// <param name="Kind">The kind of renderer.</param>
/// <param name="Failure">Why it failed, or <see langword="null"/> if it works.</param>
public sealed record RendererAttempt(RendererPreference Kind, string? Failure);

/// <summary>
/// The result of a selection.
/// </summary>
/// <param name="Device">The working device, owned by the caller.</param>
/// <param name="Attempts">All tried renderers in order, the last one is <paramref name="Device"/>.</param>
public sealed record RendererSelection(IRenderDevice Device, IReadOnlyList<RendererAttempt> Attempts);

/// <summary>
/// Selects a working renderer. It knows backends only as candidates, so that this assembly stays free
/// of backend dependencies.
/// </summary>
public static class RendererSelector
{
    /// <summary>The smallest texture limit of a GPU renderer: the largest raster and the text atlas need it.</summary>
    public const int MinimumTextureDimension = 4096;

    /// <summary>The smallest buffer limit of a GPU renderer.</summary>
    public const long MinimumBufferSize = 256L * 1024 * 1024;

    private static readonly PixelSize _probeSize = new(1, 1);

    /// <summary>
    /// Parses a blacklist such as the value of the <c>TACHYON_RENDERER_BLACKLIST</c> environment variable.
    /// </summary>
    /// <param name="value">Parts of device names, separated by semicolons.</param>
    /// <returns>The parts.</returns>
    public static IReadOnlyList<string> ParseBlacklist(string? value) =>
        value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>
    /// Parses a preference such as the value of the <c>TACHYON_RENDERER</c> environment variable.
    /// </summary>
    /// <param name="value">The case-insensitive name, or nothing for <see cref="RendererPreference.Auto"/>.</param>
    /// <returns>The preference.</returns>
    /// <exception cref="ArgumentException">The value is unknown.</exception>
    public static RendererPreference ParsePreference(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" or "auto" => RendererPreference.Auto,
        "hardware" => RendererPreference.Hardware,
        "software" => RendererPreference.Software,
        "skia" => RendererPreference.Skia,
        _ => throw new ArgumentException($"Unknown renderer '{value}', expected auto, hardware, software or skia.", nameof(value)),
    };

    /// <summary>
    /// Tries the candidates in order and returns the first device that renders a probe frame.
    /// </summary>
    /// <param name="preference">The renderer to use.</param>
    /// <param name="candidates">The candidates, best first.</param>
    /// <param name="blacklist">Parts of device names that are not used, whatever their case.</param>
    /// <returns>The selection.</returns>
    /// <exception cref="InvalidOperationException">No allowed candidate works.</exception>
    /// <remarks>
    /// Only managed failures are caught. A native abort of a backend ends the process.
    /// </remarks>
    public static RendererSelection Select(RendererPreference preference, IReadOnlyList<RendererCandidate> candidates, IReadOnlyList<string>? blacklist = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var attempts = new List<RendererAttempt>();
        foreach (var candidate in candidates)
        {
            if (preference != RendererPreference.Auto && candidate.Kind != preference)
            {
                continue;
            }

            IRenderDevice? device = null;
            try
            {
                device = candidate.Create();
                Check(candidate.Kind, device.Capabilities, blacklist ?? []);
                Probe(device);
                attempts.Add(new RendererAttempt(candidate.Kind, null));
                return new RendererSelection(device, attempts);
            }
            catch (Exception exception)
            {
                device?.Dispose();
                attempts.Add(new RendererAttempt(candidate.Kind, exception.Message));
            }
        }

        var details = string.Join("; ", attempts.Select(attempt => $"{attempt.Kind}: {attempt.Failure}"));
        throw new InvalidOperationException($"No renderer for {preference} works. {details}");
    }

    private static void Check(RendererPreference kind, DeviceCapabilities capabilities, IReadOnlyList<string> blacklist)
    {
        foreach (var entry in blacklist)
        {
            if (capabilities.BackendName.Contains(entry, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"{capabilities.BackendName} is on the blacklist ({entry}).");
            }
        }

        // Skia is the last resort and runs on the CPU, so these limits do not concern it. Compute is no
        // condition: what needs it falls back to the CPU.
        if (kind != RendererPreference.Skia
            && (capabilities.MaxBufferSize < MinimumBufferSize || capabilities.MaxTextureDimension < MinimumTextureDimension))
        {
            throw new NotSupportedException($"{capabilities.BackendName} lacks buffer size or texture size.");
        }
    }

    private static void Probe(IRenderDevice device)
    {
        using var target = device.CreateOffscreenTarget(_probeSize, 1);
        using (var frame = device.BeginFrame(target))
        {
            frame.Clear(default);
            frame.Submit();
        }

        target.ReadPixels(new Rgba8[1]);
    }
}
