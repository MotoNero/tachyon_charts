namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// Hidden time intervals, such as weekends. It maps time to a continuous scale without them.
/// </summary>
/// <remarks>
/// Intervals include their start and exclude their end. Times inside a gap collapse to its start on the
/// scale, so the mapping stays continuous and never decreases. Times outside gaps map back exactly.
/// </remarks>
public sealed class TimeGapMap
{
    private readonly long[] _starts;
    private readonly long[] _ends;
    private readonly long[] _hiddenBefore;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeGapMap"/> class.
    /// </summary>
    /// <param name="gaps">The intervals in axis units, in any order. Overlaps merge.</param>
    /// <exception cref="ArgumentException">An interval ends before it starts.</exception>
    public TimeGapMap(IEnumerable<(long Start, long End)> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        var merged = new List<(long Start, long End)>();
        foreach (var gap in gaps.OrderBy(gap => gap.Start))
        {
            if (gap.End < gap.Start)
            {
                throw new ArgumentException($"The gap from {gap.Start} ends at {gap.End}.", nameof(gaps));
            }

            if (gap.End == gap.Start)
            {
                continue;
            }

            if (merged.Count > 0 && gap.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, gap.End));
            }
            else
            {
                merged.Add(gap);
            }
        }

        _starts = new long[merged.Count];
        _ends = new long[merged.Count];
        _hiddenBefore = new long[merged.Count + 1];
        for (var i = 0; i < merged.Count; i++)
        {
            _starts[i] = merged[i].Start;
            _ends[i] = merged[i].End;
            _hiddenBefore[i + 1] = _hiddenBefore[i] + (merged[i].End - merged[i].Start);
        }
    }

    /// <summary>
    /// Gets the number of separate gaps.
    /// </summary>
    public int Count => _starts.Length;

    /// <summary>Gets the start of a gap, for marking it on the axis.</summary>
    internal long StartOf(int index) => _starts[index];

    /// <summary>
    /// Creates the gaps from Saturday 00:00 to Monday 00:00 local time for a period.
    /// </summary>
    /// <param name="fromUtc">The start of the period, in UTC.</param>
    /// <param name="toUtc">The end of the period, in UTC.</param>
    /// <param name="zone">The time zone of the weekends.</param>
    /// <returns>The map, in <see cref="DateTime.Ticks"/>.</returns>
    public static TimeGapMap Weekends(DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var gaps = new List<(long Start, long End)>();
        var day = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc), zone).Date.AddDays(-7);
        var last = toUtc.AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Saturday)
        {
            day = day.AddDays(1);
        }

        for (; day < last; day = day.AddDays(7))
        {
            gaps.Add((WallClock.ToUtcTicks(day, zone), WallClock.ToUtcTicks(day.AddDays(2), zone)));
        }

        return new TimeGapMap(gaps);
    }

    /// <summary>
    /// Tests whether a time lies in a gap.
    /// </summary>
    public bool IsHidden(long time)
    {
        var index = FindGap(time);
        return index >= 0 && time < _ends[index];
    }

    /// <summary>
    /// Maps a time to the scale without gaps.
    /// </summary>
    public long Compress(long time)
    {
        var index = FindGap(time);
        if (index < 0)
        {
            return time;
        }

        return time < _ends[index]
            ? _starts[index] - _hiddenBefore[index]
            : time - _hiddenBefore[index + 1];
    }

    /// <summary>
    /// Maps a position on the scale back to time. The position of a gap maps to its end.
    /// </summary>
    public long Expand(long position)
    {
        var low = 0;
        var high = _starts.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_starts[middle] - _hiddenBefore[middle] <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return position + _hiddenBefore[low];
    }

    private int FindGap(long time)
    {
        var index = Array.BinarySearch(_starts, time);
        return index >= 0 ? index : ~index - 1;
    }
}

/// <summary>
/// Conversions between wall clocks and UTC that survive daylight saving changes.
/// </summary>
internal static class WallClock
{
    private const int InvalidTimeStepMinutes = 15;

    /// <summary>
    /// Converts a local time. A time skipped by a clock change moves to the first valid one, a time
    /// that occurs twice means its first occurrence.
    /// </summary>
    public static long ToUtcTicks(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(wall))
        {
            wall = wall.AddMinutes(InvalidTimeStepMinutes);
        }

        if (zone.IsAmbiguousTime(wall))
        {
            return (wall - zone.GetAmbiguousTimeOffsets(wall).Max()).Ticks;
        }

        return (wall - zone.GetUtcOffset(wall)).Ticks;
    }
}
