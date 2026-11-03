using System.Numerics;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// Reduces a line with sorted X to the extremes per pixel column, which keeps every peak visible.
/// </summary>
public static class MinMaxDecimator
{
    /// <summary>
    /// Finds the extremes per column.
    /// </summary>
    /// <typeparam name="TX">The X type.</typeparam>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="x">The X values, sorted.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <param name="pyramid">The pyramid of <paramref name="y"/>, or <see langword="null"/> to scan the values.</param>
    /// <param name="visible">The visible X range. It divides evenly into the columns, the last one includes the upper bound.</param>
    /// <param name="minima">Receives the minimum per column. A column without finite values gets a minimum above its maximum.</param>
    /// <param name="maxima">Receives the maximum per column, as long as <paramref name="minima"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ, the range is empty, or there are no columns.</exception>
    public static void Decimate<TX, TY>(
        ReadOnlySpan<TX> x,
        ReadOnlySpan<TY> y,
        LodPyramid<TY>? pyramid,
        DoubleRange visible,
        Span<TY> minima,
        Span<TY> maxima)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
    {
        if (x.Length != y.Length || minima.Length != maxima.Length)
        {
            throw new ArgumentException("X and Y, and the column spans, must have the same length.", nameof(y));
        }

        if (minima.IsEmpty || visible.IsEmpty)
        {
            throw new ArgumentException("There must be columns and a visible range.", nameof(visible));
        }

        var columns = minima.Length;
        var start = LowerBound(x, visible.Min, 0, 0);
        var stride = Math.Max((UpperBound(x, visible.Max) - start) / columns, 1);
        for (var column = 0; column < columns; column++)
        {
            int end;
            if (column == columns - 1)
            {
                end = UpperBound(x, visible.Max);
            }
            else
            {
                var boundary = visible.Min + (visible.Length * (column + 1) / columns);
                end = LowerBound(x, boundary, start, stride);
            }

            var extremes = pyramid is null ? Scan(y, start, end) : pyramid.Query(y, start, end);
            minima[column] = extremes.Min;
            maxima[column] = extremes.Max;
            start = end;
        }
    }

    /// <summary>
    /// Finds the first index with an X value of at least <paramref name="value"/>.
    /// </summary>
    internal static int LowerBound<TX>(ReadOnlySpan<TX> x, double value, int first, int stride)
        where TX : unmanaged, INumber<TX>
    {
        // Boundaries are nearly evenly spaced, so the search starts at the expected index and
        // gallops, which needs far fewer cache misses than a search over the whole array.
        var low = first;
        var high = x.Length;
        if (stride > 0)
        {
            var step = stride;
            var probe = Math.Min((long)first + step, x.Length);
            while (probe < x.Length && double.CreateTruncating(x[(int)probe]) < value)
            {
                low = (int)probe + 1;
                step *= 2;
                probe = Math.Min((long)low + step, x.Length);
            }

            high = (int)probe;
        }

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (double.CreateTruncating(x[middle]) < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Finds the first index with an X value above <paramref name="value"/>.
    /// </summary>
    internal static int UpperBound<TX>(ReadOnlySpan<TX> x, double value)
        where TX : unmanaged, INumber<TX>
    {
        var low = 0;
        var high = x.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (double.CreateTruncating(x[middle]) <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static MinMax<TY> Scan<TY>(ReadOnlySpan<TY> y, int start, int end)
        where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
    {
        var extremes = MinMax<TY>.Empty;
        var min = extremes.Min;
        var max = extremes.Max;
        SimdMinMax.Accumulate(y[start..end], ref min, ref max);
        return new MinMax<TY>(min, max);
    }
}
