using Tachyon.Charts.Core.Colors;

namespace Tachyon.Charts.Medical;

/// <summary>How a multiplanar display shows its volume; fixed when the display is made.</summary>
public sealed record MedicalMprOptions
{
    /// <summary>Gets the colors from the lower to the upper end of the window, in the planes and the volume.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Grayscale;

    /// <summary>Gets a value indicating whether the planes blend between neighbouring values instead of showing each value as a square.</summary>
    public bool IsInterpolated { get; init; } = true;

    /// <summary>
    /// Gets the bytes the volume may take on the graphics card, or <see langword="null"/> for the usual share of its
    /// memory; a volume above it shows with less precision or fewer values.
    /// </summary>
    public long? GpuMemoryLimit { get; init; }
}
