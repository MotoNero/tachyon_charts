using System.Numerics;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// The extremes of the finite values in a range. NaN and infinities are gaps and do not count.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Min">The smallest value.</param>
/// <param name="Max">The largest value.</param>
public readonly record struct MinMax<T>(T Min, T Max)
    where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    /// <summary>
    /// Gets the extremes of no values, the start for collecting.
    /// </summary>
    public static MinMax<T> Empty => new(T.MaxValue, T.MinValue);

    /// <summary>
    /// Gets a value indicating whether the range held no finite value.
    /// </summary>
    public bool IsEmpty => Min > Max;
}

/// <summary>
/// Finds extremes with SIMD where the value type allows it.
/// </summary>
internal static class SimdMinMax
{
    public static void Accumulate<T>(ReadOnlySpan<T> values, ref T min, ref T max)
        where T : unmanaged, INumber<T>, IMinMaxValue<T>
    {
        var index = 0;
        if (Vector.IsHardwareAccelerated && Vector<T>.IsSupported && values.Length >= Vector<T>.Count)
        {
            index = AccumulateVectors(values, ref min, ref max);
        }

        for (; index < values.Length; index++)
        {
            var value = values[index];
            if (T.IsFinite(value))
            {
                min = T.Min(min, value);
                max = T.Max(max, value);
            }
        }
    }

    private static int AccumulateVectors<T>(ReadOnlySpan<T> values, ref T min, ref T max)
        where T : unmanaged, INumber<T>, IMinMaxValue<T>
    {
        var isFloatingPoint = typeof(T) == typeof(float) || typeof(T) == typeof(double);
        var largest = new Vector<T>(T.MaxValue);
        var minima = new Vector<T>(min);
        var maxima = new Vector<T>(max);
        var index = 0;

        for (; index <= values.Length - Vector<T>.Count; index += Vector<T>.Count)
        {
            var block = new Vector<T>(values[index..]);
            if (isFloatingPoint)
            {
                // NaN and infinity fail this comparison and leave the extremes alone.
                var isFinite = Vector.LessThanOrEqual(Vector.Abs(block), largest);
                minima = Vector.Min(minima, Vector.ConditionalSelect(isFinite, block, minima));
                maxima = Vector.Max(maxima, Vector.ConditionalSelect(isFinite, block, maxima));
            }
            else
            {
                minima = Vector.Min(minima, block);
                maxima = Vector.Max(maxima, block);
            }
        }

        for (var lane = 0; lane < Vector<T>.Count; lane++)
        {
            min = T.Min(min, minima[lane]);
            max = T.Max(max, maxima[lane]);
        }

        return index;
    }
}
