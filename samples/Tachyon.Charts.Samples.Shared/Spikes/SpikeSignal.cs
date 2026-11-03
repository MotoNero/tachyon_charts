using System.Collections.Concurrent;

namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>
/// A deterministic test signal in the range -1 to 1 with isolated peaks that decimation must preserve.
/// </summary>
public static class SpikeSignal
{
    /// <summary>
    /// The distance between two peaks.
    /// </summary>
    public const int PeakInterval = 7_654_321;

    private const int PeakOffset = 1_234_567;

    private static readonly ConcurrentDictionary<int, Lazy<float[]>> _cache = new();

    /// <summary>
    /// Gets the signal of a length. The array is shared, callers must not change it.
    /// </summary>
    public static float[] Get(int count) =>
        _cache.GetOrAdd(count, key => new Lazy<float[]>(() => Generate(key))).Value;

    private static float[] Generate(int count)
    {
        var values = new float[count];
        Parallel.For(0, count, i =>
        {
            var phase = (double)i / count;
            var wave = (0.5 * Math.Sin(phase * Math.Tau * 3)) + (0.2 * Math.Sin(phase * Math.Tau * 47));
            values[i] = (float)(wave + (0.1 * Noise(i)));
        });

        for (long i = PeakOffset; i < count; i += PeakInterval)
        {
            values[i] = i % 2 == 0 ? 1f : -1f;
        }

        return values;
    }

    // A stateless hash, so that the signal does not depend on the thread partitioning.
    private static double Noise(int index)
    {
        var hash = (uint)index * 2654435761u;
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return (hash / (double)uint.MaxValue * 2) - 1;
    }
}
