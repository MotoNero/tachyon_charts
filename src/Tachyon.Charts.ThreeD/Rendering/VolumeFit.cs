using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>Chooses how a volume goes to the graphics card: the precision and how many values it keeps.</summary>
internal static class VolumeFit
{
    /// <summary>The share of the graphics memory one volume may take; the rest is for the window and other charts.</summary>
    public const double MemoryShare = 0.6;

    /// <summary>The budget where the memory is unknown.</summary>
    public const long UnknownBudget = 2L << 30;

    /// <summary>Gets the bytes a volume may take.</summary>
    /// <param name="limit">The limit the volume asks for, if any.</param>
    /// <param name="graphicsMemory">The memory of the graphics card, or 0.</param>
    /// <returns>The bytes.</returns>
    public static long Budget(long? limit, long graphicsMemory) =>
        limit ?? (graphicsMemory > 0 ? (long)(graphicsMemory * MemoryShare) : UnknownBudget);

    /// <summary>Chooses the fit.</summary>
    /// <param name="width">The values along the first axis.</param>
    /// <param name="height">The values along the second axis.</param>
    /// <param name="depth">The values along the third axis.</param>
    /// <param name="isBytes">Whether the values are bytes.</param>
    /// <param name="canBeExact">Whether the card filters 32-bit floats.</param>
    /// <param name="maxSide">The largest side of a 3D texture.</param>
    /// <param name="budget">The bytes the volume may take.</param>
    /// <param name="graphicsMemory">The memory of the card, or 0, for the report.</param>
    /// <returns>The fit.</returns>
    public static VolumeGpuFit Choose(int width, int height, int depth, bool isBytes, bool canBeExact, long maxSide, long budget, long graphicsMemory)
    {
        var full = Bytes(width, height, depth, 1, isBytes ? 1 : 4);
        for (var reduction = 1; ; reduction++)
        {
            var (w, h, d) = (Reduce(width, reduction), Reduce(height, reduction), Reduce(depth, reduction));
            if (Math.Max(w, Math.Max(h, d)) > maxSide)
            {
                continue;
            }

            if (isBytes || canBeExact)
            {
                var exact = Bytes(width, height, depth, reduction, isBytes ? 1 : 4);
                if (exact <= budget || (w == 1 && h == 1 && d == 1))
                {
                    return new VolumeGpuFit(reduction, true, exact, full, budget, graphicsMemory);
                }
            }

            if (!isBytes)
            {
                var halves = Bytes(width, height, depth, reduction, 2);
                if (halves <= budget || (w == 1 && h == 1 && d == 1))
                {
                    return new VolumeGpuFit(reduction, false, halves, full, budget, graphicsMemory);
                }
            }
        }
    }

    /// <summary>Gets the number of values left along an axis.</summary>
    /// <param name="count">The values along the axis.</param>
    /// <param name="reduction">The reduction.</param>
    /// <returns>The values left.</returns>
    public static int Reduce(int count, int reduction) => (count + reduction - 1) / reduction;

    private static long Bytes(int width, int height, int depth, int reduction, int bytesPerValue) =>
        (long)Reduce(width, reduction) * Reduce(height, reduction) * Reduce(depth, reduction) * bytesPerValue;
}
