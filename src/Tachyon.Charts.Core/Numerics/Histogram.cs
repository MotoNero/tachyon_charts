namespace Tachyon.Charts.Core.Numerics;

/// <summary>How a histogram finds its bins. The rules follow those of NumPy.</summary>
public enum BinningRule
{
    /// <summary>A fixed number of bins.</summary>
    FixedCount,

    /// <summary>A fixed width of the bins.</summary>
    FixedWidth,

    /// <summary>Sturges: log2(n) + 1 bins, for data close to a normal distribution.</summary>
    Sturges,

    /// <summary>Freedman-Diaconis: a width of 2 IQR / n^(1/3), robust against outliers.</summary>
    FreedmanDiaconis,
}

/// <summary>
/// Values sorted into bins of equal width.
/// </summary>
/// <param name="Edges">The edges of the bins, one more than bins.</param>
/// <param name="Counts">The number of values per bin. The last bin includes its upper edge.</param>
public sealed record Histogram(double[] Edges, long[] Counts)
{
    /// <summary>Counts values. NaN and infinities are left out.</summary>
    /// <param name="values">The values.</param>
    /// <param name="rule">How the bins are found.</param>
    /// <param name="parameter">The number of bins of <see cref="BinningRule.FixedCount"/> or the width of <see cref="BinningRule.FixedWidth"/>.</param>
    /// <returns>The histogram, without bins if there are no values.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A fixed rule has no positive parameter.</exception>
    public static Histogram Compute(ReadOnlySpan<double> values, BinningRule rule, double parameter = 0)
    {
        var finite = new List<double>(values.Length);
        foreach (var value in values)
        {
            if (double.IsFinite(value))
            {
                finite.Add(value);
            }
        }

        if (rule is BinningRule.FixedCount or BinningRule.FixedWidth && !(parameter > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "A fixed rule needs a positive parameter.");
        }

        if (finite.Count == 0)
        {
            return new Histogram([], []);
        }

        finite.Sort();
        var minimum = finite[0];
        var maximum = finite[^1];
        if (minimum == maximum)
        {
            // Like NumPy: one unit around a single value.
            minimum -= 0.5;
            maximum += 0.5;
        }

        var bins = CountBins(finite, minimum, maximum, rule, parameter);
        var edges = new double[bins + 1];
        for (var i = 0; i <= bins; i++)
        {
            edges[i] = minimum + ((maximum - minimum) * i / bins);
        }

        var counts = new long[bins];
        foreach (var value in finite)
        {
            counts[Math.Min((int)((value - minimum) / (maximum - minimum) * bins), bins - 1)]++;
        }

        return new Histogram(edges, counts);
    }

    /// <summary>Finds a quantile of sorted values, with the linear interpolation of NumPy.</summary>
    /// <param name="sorted">The values, rising, at least one.</param>
    /// <param name="quantile">The quantile from 0 to 1.</param>
    /// <returns>The value.</returns>
    public static double Quantile(IReadOnlyList<double> sorted, double quantile)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        var position = quantile * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    private static int CountBins(List<double> sorted, double minimum, double maximum, BinningRule rule, double parameter)
    {
        var range = maximum - minimum;
        switch (rule)
        {
            case BinningRule.FixedCount:
                return Math.Max((int)parameter, 1);
            case BinningRule.FixedWidth:
                return Math.Max((int)Math.Ceiling(range / parameter), 1);
            case BinningRule.Sturges:
                return Math.Max((int)Math.Ceiling(Math.Log2(sorted.Count) + 1), 1);
            default:
                var interquartile = Quantile(sorted, 0.75) - Quantile(sorted, 0.25);
                var width = 2 * interquartile * Math.Pow(sorted.Count, -1.0 / 3);
                return width > 0 ? Math.Max((int)Math.Ceiling(range / width), 1) : 1;
        }
    }
}
