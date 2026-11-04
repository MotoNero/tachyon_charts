using Tachyon.Charts.Core.Axes;

namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// A scale with breaks: each break keeps only a narrow band of <see cref="KeptShare"/> of its length, the rest stays
/// linear. A line that passes through a break crosses the band steeply instead of running along the break.
/// </summary>
internal sealed class BreakScaling : AxisScaling
{
    /// <summary>The share of its length that a break keeps on the screen.</summary>
    public const double KeptShare = 0.01;

    private readonly double[] _starts;
    private readonly double[] _ends;
    private readonly double[] _hiddenBefore;

    public BreakScaling(IEnumerable<DoubleRange> breaks)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var range in breaks.Where(range => !range.IsEmpty && range.Length > 0).OrderBy(range => range.Min))
        {
            if (merged.Count > 0 && range.Min <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.Max));
            }
            else
            {
                merged.Add((range.Min, range.Max));
            }
        }

        _starts = [.. merged.Select(item => item.Start)];
        _ends = [.. merged.Select(item => item.End)];
        _hiddenBefore = new double[merged.Count + 1];
        for (var i = 0; i < merged.Count; i++)
        {
            _hiddenBefore[i + 1] = _hiddenBefore[i] + ((_ends[i] - _starts[i]) * (1 - KeptShare));
        }
    }

    /// <summary>Gets the breaks after merging.</summary>
    public IReadOnlyList<DoubleRange> Breaks => [.. _starts.Select((start, i) => new DoubleRange(start, _ends[i]))];

    /// <inheritdoc />
    public override double Forward(double value)
    {
        var index = Last(_starts, value);
        if (index < 0 || double.IsNaN(value))
        {
            return value;
        }

        return value < _ends[index]
            ? _starts[index] - _hiddenBefore[index] + ((value - _starts[index]) * KeptShare)
            : value - _hiddenBefore[index + 1];
    }

    /// <inheritdoc />
    public override double Inverse(double value)
    {
        // The last break whose place lies at or before the position.
        var (low, high) = (0, _starts.Length);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_starts[middle] - _hiddenBefore[middle] <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low == 0)
        {
            return value;
        }

        // Inside the band of the break, or after it.
        var index = low - 1;
        var band = value - (_starts[index] - _hiddenBefore[index]);
        var kept = (_ends[index] - _starts[index]) * KeptShare;
        return band < kept ? _starts[index] + (band / KeptShare) : value + _hiddenBefore[low];
    }

    private static int Last(double[] starts, double value)
    {
        var (low, high) = (0, starts.Length);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (starts[middle] <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low - 1;
    }
}

/// <summary>The gaps of a time axis as a scale of its values, which count from the origin of the axis.</summary>
/// <param name="gaps">The gaps in absolute time.</param>
/// <param name="origin">The origin of the axis.</param>
internal sealed class TimeGapScaling(TimeGapMap gaps, long origin) : AxisScaling
{
    public TimeGapMap Gaps => gaps;

    public long Origin => origin;

    /// <inheritdoc />
    public override double Forward(double value) =>
        double.IsFinite(value) ? gaps.Compress(origin + (long)Math.Round(value)) - origin : value;

    /// <inheritdoc />
    public override double Inverse(double value) =>
        double.IsFinite(value) ? gaps.Expand(origin + (long)Math.Round(value)) - origin : value;
}
