using System.Numerics;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// Largest-Triangle-Three-Buckets: selects points that keep the shape of a curve for smooth lines.
/// Unlike <see cref="MinMaxDecimator"/>, it does not guarantee to keep peaks.
/// </summary>
public static class Lttb
{
    /// <summary>
    /// Selects points in the range of the data, the first and last finite point always among them.
    /// </summary>
    /// <typeparam name="TX">The X type.</typeparam>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="x">The X values, sorted.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <param name="indices">Receives the selected indices in ascending order. Its length is the target count, at least 2.</param>
    /// <returns>The number of selected points, smaller than the target count if there are fewer finite points.</returns>
    /// <exception cref="ArgumentException">The lengths differ, or the target count is too small.</exception>
    public static int Select<TX, TY>(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, Span<int> indices)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("X and Y must have the same length.", nameof(y));
        }

        if (indices.Length < 2)
        {
            throw new ArgumentException("The target count must be at least 2.", nameof(indices));
        }

        var first = NextFinite(y, 0);
        if (first == y.Length)
        {
            return 0;
        }

        var last = y.Length - 1;
        while (!TY.IsFinite(y[last]))
        {
            last--;
        }

        if (last - first + 1 <= indices.Length)
        {
            return CopyFinite(y, first, last, indices);
        }

        var count = 0;
        indices[count++] = first;

        var buckets = indices.Length - 2;
        var bucketSize = (last - first - 1) / (double)buckets;
        var selected = first;
        for (var bucket = 0; bucket < buckets; bucket++)
        {
            var bucketStart = first + 1 + (int)(bucket * bucketSize);
            var bucketEnd = first + 1 + (int)((bucket + 1) * bucketSize);
            var nextEnd = bucket == buckets - 1 ? last + 1 : first + 1 + (int)((bucket + 2) * bucketSize);

            var (targetX, targetY) = Average(x, y, bucketEnd, nextEnd);
            var best = FindLargestTriangle(x, y, selected, bucketStart, bucketEnd, targetX, targetY);
            if (best >= 0)
            {
                indices[count++] = best;
                selected = best;
            }
        }

        indices[count++] = last;
        return count;
    }

    private static int NextFinite<TY>(ReadOnlySpan<TY> y, int index)
        where TY : unmanaged, INumber<TY>
    {
        while (index < y.Length && !TY.IsFinite(y[index]))
        {
            index++;
        }

        return index;
    }

    private static int CopyFinite<TY>(ReadOnlySpan<TY> y, int first, int last, Span<int> indices)
        where TY : unmanaged, INumber<TY>
    {
        var count = 0;
        for (var i = first; i <= last; i++)
        {
            if (TY.IsFinite(y[i]))
            {
                indices[count++] = i;
            }
        }

        return count;
    }

    private static (double X, double Y) Average<TX, TY>(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, int start, int end)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>
    {
        var sumX = 0.0;
        var sumY = 0.0;
        var count = 0;
        for (var i = start; i < end; i++)
        {
            if (TY.IsFinite(y[i]))
            {
                sumX += double.CreateTruncating(x[i]);
                sumY += double.CreateTruncating(y[i]);
                count++;
            }
        }

        return count == 0
            ? (double.CreateTruncating(x[Math.Min(start, x.Length - 1)]), 0)
            : (sumX / count, sumY / count);
    }

    private static int FindLargestTriangle<TX, TY>(
        ReadOnlySpan<TX> x,
        ReadOnlySpan<TY> y,
        int anchor,
        int start,
        int end,
        double targetX,
        double targetY)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>
    {
        var anchorX = double.CreateTruncating(x[anchor]);
        var anchorY = double.CreateTruncating(y[anchor]);
        var best = -1;
        var bestArea = -1.0;
        for (var i = start; i < end; i++)
        {
            if (!TY.IsFinite(y[i]))
            {
                continue;
            }

            var pointX = double.CreateTruncating(x[i]);
            var pointY = double.CreateTruncating(y[i]);
            var area = Math.Abs(((anchorX - targetX) * (pointY - anchorY)) - ((anchorX - pointX) * (targetY - anchorY)));
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }

        return best;
    }
}
