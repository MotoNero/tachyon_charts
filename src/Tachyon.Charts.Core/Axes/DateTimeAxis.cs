using System.Globalization;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// What one unit of a time value means.
/// </summary>
public enum TimeResolution
{
    /// <summary>
    /// <see cref="DateTime.Ticks"/> in UTC, 100 ns each.
    /// </summary>
    Ticks,

    /// <summary>
    /// Nanoseconds since 1970-01-01 UTC, which reach until the year 2262.
    /// </summary>
    Nanoseconds,
}

/// <summary>
/// A time axis with ticks on calendar boundaries of its time zone.
/// </summary>
/// <remarks>
/// Time stays a <see cref="long"/>: a double cannot hold today's ticks. The base class range
/// therefore counts from <see cref="OriginTicks"/>. Its unit is one <see cref="TimeResolution"/> step.
/// </remarks>
public sealed class DateTimeAxis : AxisBase
{
    private const double TargetSpacing = 72;
    private const double LabelGap = 8;
    private const int MaximumTicks = 4000;

    private static readonly TickStep[] _steps = CreateSteps();

    private readonly TimeResolution _resolution;
    private TimeGapMap? _gaps;
    private TimeGapScaling? _gapScaling;

    /// <summary>
    /// Initializes a new instance of the <see cref="DateTimeAxis"/> class.
    /// </summary>
    /// <param name="id">The identifier that series refer to.</param>
    /// <param name="position">The side of the plot area.</param>
    /// <param name="resolution">The unit of the time values.</param>
    public DateTimeAxis(string id, AxisPosition position, TimeResolution resolution = TimeResolution.Ticks)
        : base(id, position)
    {
        _resolution = resolution;
    }

    /// <summary>
    /// Gets the time the range of the base class counts from.
    /// </summary>
    public long OriginTicks { get; private set; }

    /// <summary>
    /// Gets or sets the zone of labels and calendar boundaries.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Gets or sets the culture of names and dates, or <see langword="null"/> for the language of the charts, <see cref="Instruments.ChartText.Language"/>.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets the hidden intervals, in the unit of the time values.
    /// </summary>
    public TimeGapMap? Gaps
    {
        get => _gaps;
        set
        {
            _gaps = value;
            _gapScaling = null;
            OnChanged();
        }
    }

    /// <inheritdoc />
    /// <remarks>With <see cref="Gaps"/>, the hidden intervals take no room; zooming, panning and the values under the pointer go across them.</remarks>
    public override AxisScaling? Scaling
    {
        get
        {
            if (_gaps is null)
            {
                return null;
            }

            // The scale counts from the origin like the values of the axis, so it follows a new origin.
            var scaling = _gapScaling;
            if (scaling is null || scaling.Origin != OriginTicks || scaling.Gaps != _gaps)
            {
                scaling = new TimeGapScaling(_gaps, OriginTicks);
                _gapScaling = scaling;
            }

            return scaling;
        }
    }

    internal long NanosecondsPerUnit => _resolution == TimeResolution.Ticks ? 100 : 1;

    /// <summary>
    /// Sets the visible time. Like setting the range, this ends the automatic range.
    /// </summary>
    /// <param name="start">The first visible time.</param>
    /// <param name="end">The last visible time.</param>
    /// <exception cref="ArgumentOutOfRangeException">The end lies before the start.</exception>
    public void SetVisibleTime(long start, long end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        OriginTicks = start;
        VisibleRange = new DoubleRange(0, end - start);
    }

    /// <summary>
    /// Applies the time range of the data while the automatic range is on.
    /// </summary>
    /// <param name="first">The earliest time.</param>
    /// <param name="last">The latest time.</param>
    /// <param name="padding">The share of the length to add on each side.</param>
    /// <exception cref="ArgumentOutOfRangeException">The last time lies before the first.</exception>
    public void FitToTime(long first, long last, double padding = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);

        if (IsAutoRange)
        {
            if (OriginTicks != first)
            {
                OriginTicks = first;
                OnChanged();
            }

            FitTo(new DoubleRange(0, last - first), padding);
        }
    }

    /// <inheritdoc />
    public override string FormatCursorValue(double value)
    {
        var ticks = OriginTicks + ((long)value * (NanosecondsPerUnit / 100));
        var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(ticks, DateTimeKind.Utc), TimeZone);
        return local.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates the mapping of times to pixels, which skips the gaps.
    /// </summary>
    /// <param name="start">The first pixel of the axis, the left or top end.</param>
    /// <param name="length">The length in pixels, not negative.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
    public DateTimeAxisTransform CreateTimeTransform(double start, double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var (first, last) = VisibleTime();
        var pixelStart = IsHorizontal != IsInverted ? start : start + length;
        var pixelEnd = IsHorizontal != IsInverted ? start + length : start;
        return new DateTimeAxisTransform(first, last, pixelStart, pixelEnd, Gaps, OriginTicks);
    }


    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The length is negative or not finite.</exception>
    public override TickSet GenerateTicks(double length, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ThrowIfInvalid(length);
        var (first, last) = VisibleTime();
        if (length == 0 || last <= first)
        {
            return length == 0 ? TickSet.Empty : SingleTick(first);
        }

        var transform = CreateTimeTransform(0, length);
        var visibleNanoseconds = (double)(Compressed(last) - Compressed(first)) * NanosecondsPerUnit;
        var target = visibleNanoseconds / Math.Max(length / TargetSpacing, 1);

        foreach (var step in _steps)
        {
            if (step.Nanoseconds < target || !IsUsable(step))
            {
                continue;
            }

            var times = CreateTimes(step, first, last);
            if (times is null)
            {
                continue;
            }

            if (times.Count == 0)
            {
                return TickSet.Empty;
            }

            var labels = CreateLabels(step, times);
            if (Fits(times, labels, transform, measurer))
            {
                return ToTickSet(times, labels, step);
            }
        }

        return SingleTick(first);
    }

    private static void ThrowIfInvalid(double length)
    {
        if (!double.IsFinite(length) || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The length must not be negative.");
        }
    }

    private static TickStep[] CreateSteps()
    {
        const double Microsecond = 1e3;
        const double Millisecond = 1e6;
        const double Second = 1e9;
        const double Minute = 60 * Second;
        const double Hour = 60 * Minute;
        const double Day = 24 * Hour;
        const double Month = 30.44 * Day;
        const double Year = 365.25 * Day;

        var steps = new List<TickStep>();
        foreach (var unit in new[] { 1.0, Microsecond, Millisecond })
        {
            foreach (var factor in new[] { 1, 2, 5, 10, 20, 50, 100, 200, 500 })
            {
                steps.Add(new TickStep(StepKind.Fixed, factor, factor * unit));
            }
        }

        foreach (var factor in new[] { 1, 2, 5, 10, 15, 30 })
        {
            steps.Add(new TickStep(StepKind.Fixed, factor, factor * Second));
        }

        foreach (var factor in new[] { 1, 2, 5, 10, 15, 30 })
        {
            steps.Add(new TickStep(StepKind.Fixed, factor, factor * Minute));
        }

        steps.Add(new TickStep(StepKind.Fixed, 1, Hour));
        foreach (var factor in new[] { 2, 3, 6, 12 })
        {
            steps.Add(new TickStep(StepKind.Hours, factor, factor * Hour));
        }

        steps.Add(new TickStep(StepKind.Days, 1, Day));
        steps.Add(new TickStep(StepKind.Days, 2, 2 * Day));
        steps.Add(new TickStep(StepKind.Weeks, 1, 7 * Day));
        foreach (var factor in new[] { 1, 3, 6 })
        {
            steps.Add(new TickStep(StepKind.Months, factor, factor * Month));
        }

        foreach (var factor in new[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 })
        {
            steps.Add(new TickStep(StepKind.Years, factor, factor * Year));
        }

        return [.. steps];
    }

    private static string FractionDigits(long nanosecondOfSecond, int digits)
    {
        var text = nanosecondOfSecond.ToString("D9", CultureInfo.InvariantCulture);
        return text[..digits];
    }

    private (long First, long Last) VisibleTime()
    {
        var range = VisibleRange;
        return (OriginTicks + (long)Math.Floor(range.Min), OriginTicks + (long)Math.Ceiling(range.Max));
    }

    private long Compressed(long time) => Gaps?.Compress(time) ?? time;

    private bool IsUsable(TickStep step) =>
        step.Kind != StepKind.Fixed || step.Nanoseconds >= NanosecondsPerUnit;

    private long ToDateTimeTicks(long time) =>
        _resolution == TimeResolution.Ticks ? time : DateTime.UnixEpoch.Ticks + Math.DivRem(time, 100, out _);

    private long FromDateTimeTicks(long ticks) =>
        _resolution == TimeResolution.Ticks ? ticks : (ticks - DateTime.UnixEpoch.Ticks) * 100;

    private DateTime ToLocal(long time) =>
        TimeZoneInfo.ConvertTimeFromUtc(new DateTime(ToDateTimeTicks(time), DateTimeKind.Utc), TimeZone);

    private List<long>? CreateTimes(TickStep step, long first, long last)
    {
        if (ToDateTimeTicks(first) < 0 || ToDateTimeTicks(last) > DateTime.MaxValue.Ticks)
        {
            return [];
        }

        return step.Kind == StepKind.Fixed
            ? CreateFixedTimes(step, first, last)
            : CreateCalendarTimes(step, first, last);
    }

    private List<long>? CreateFixedTimes(TickStep step, long first, long last)
    {
        var stepUnits = (long)(step.Nanoseconds / NanosecondsPerUnit);
        if ((last - first) / stepUnits > MaximumTicks)
        {
            return null;
        }

        // The offset aligns steps such as 30 minutes with the local clock in every time zone.
        var offset = TimeZone.GetUtcOffset(new DateTime(ToDateTimeTicks(first), DateTimeKind.Utc)).Ticks * (100 / NanosecondsPerUnit);
        var aligned = FloorDiv(first + offset, stepUnits) * stepUnits;
        var time = aligned - offset;
        if (time < first)
        {
            time += stepUnits;
        }

        var times = new List<long>();
        for (; time <= last; time += stepUnits)
        {
            if (Gaps?.IsHidden(time) != true)
            {
                times.Add(time);
            }
        }

        return times;
    }

    private static long FloorDiv(long value, long divisor)
    {
        var quotient = Math.DivRem(value, divisor, out var remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private List<long>? CreateCalendarTimes(TickStep step, long first, long last)
    {
        var local = ToLocal(first);
        var wall = AlignWall(step, local);
        var lastLocal = ToLocal(last).AddDays(1);
        var times = new List<long>();
        while (wall <= lastLocal)
        {
            // A wall time skipped by a clock change has no tick. Midnight still gets one at the first valid time.
            var isSkipped = step.Kind == StepKind.Hours && TimeZone.IsInvalidTime(wall);
            if (!isSkipped)
            {
                var time = FromDateTimeTicks(WallClock.ToUtcTicks(wall, TimeZone));
                if (time >= first && time <= last && Gaps?.IsHidden(time) != true)
                {
                    times.Add(time);
                }
            }

            if (times.Count > MaximumTicks)
            {
                return null;
            }

            if (DateTime.MaxValue.Year - wall.Year < 2 * step.Factor)
            {
                break;
            }

            wall = Advance(step, wall);
        }

        return times;
    }

    private static DateTime AlignWall(TickStep step, DateTime local) => step.Kind switch
    {
        StepKind.Hours => local.Date.AddHours(local.Hour / step.Factor * step.Factor),
        StepKind.Days => local.Date.AddDays(-(local.Date.Ticks / TimeSpan.TicksPerDay % step.Factor)),
        StepKind.Weeks => local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)),
        StepKind.Months => new DateTime(local.Year, ((local.Month - 1) / step.Factor * step.Factor) + 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        _ => new DateTime(Math.Max(local.Year / step.Factor * step.Factor, 1), 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
    };

    private static DateTime Advance(TickStep step, DateTime wall) => step.Kind switch
    {
        StepKind.Hours => wall.AddHours(step.Factor),
        StepKind.Days => wall.AddDays(step.Factor),
        StepKind.Weeks => wall.AddDays(7),
        StepKind.Months => wall.AddMonths(step.Factor),
        _ => wall.AddYears(step.Factor),
    };

    private string[] CreateLabels(TickStep step, List<long> times)
    {
        var culture = Culture ?? Instruments.ChartText.Culture;
        var labels = new string[times.Count];
        for (var i = 0; i < labels.Length; i++)
        {
            labels[i] = FormatLabel(step, times[i], i == 0, culture);
        }

        return labels;
    }

    private string FormatLabel(TickStep step, long time, bool isFirst, CultureInfo culture)
    {
        var local = ToLocal(time);
        switch (step.Kind)
        {
            case StepKind.Years:
                return local.ToString("yyyy", culture);

            case StepKind.Months:
                return local.Month == 1 || isFirst ? local.ToString("MMM yyyy", culture) : local.ToString("MMM", culture);

            case StepKind.Days:
            case StepKind.Weeks:
                if (isFirst || local is { Month: 1, Day: 1 })
                {
                    return local.ToString("d MMM yyyy", culture);
                }

                return local.Day == 1 ? local.ToString("MMM", culture) : local.ToString("%d", culture);

            default:
                return FormatClock(step, time, local, isFirst, culture);
        }
    }

    private string FormatClock(TickStep step, long time, DateTime local, bool isFirst, CultureInfo culture)
    {
        if (step.Nanoseconds >= 1e9)
        {
            if (local.TimeOfDay == TimeSpan.Zero || isFirst)
            {
                var date = local.ToString("d MMM", culture);
                return local.TimeOfDay == TimeSpan.Zero ? date : $"{date} {local.ToString(ClockFormat(step), culture)}";
            }

            return local.ToString(ClockFormat(step), culture);
        }

        var digits = step.Nanoseconds switch
        {
            >= 1_000_000 => 3,
            >= 1_000 => 6,
            _ => 9,
        };
        var fraction = FractionDigits(NanosecondOfSecond(time), digits);
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        return $"{local.ToString(isFirst ? "HH:mm:ss" : "ss", culture)}{separator}{fraction}";
    }

    private static string ClockFormat(TickStep step) => step.Nanoseconds >= 60e9 ? "HH:mm" : "HH:mm:ss";

    private long NanosecondOfSecond(long time)
    {
        var perSecond = 1_000_000_000 / NanosecondsPerUnit;
        return ((time % perSecond) + perSecond) % perSecond * NanosecondsPerUnit;
    }

    private bool Fits(List<long> times, string[] labels, DateTimeAxisTransform transform, ITextMeasurer measurer)
    {
        for (var i = 1; i < times.Count; i++)
        {
            var distance = Math.Abs(transform.ToPixel(times[i]) - transform.ToPixel(times[i - 1]));
            var needed = IsHorizontal
                ? ((measurer.MeasureWidth(labels[i - 1]) + measurer.MeasureWidth(labels[i])) / 2) + LabelGap
                : measurer.LineHeight + LabelGap;
            if (distance < needed)
            {
                return false;
            }
        }

        return true;
    }

    private TickSet ToTickSet(List<long> times, string[] labels, TickStep step)
    {
        var ticks = new AxisTick[times.Count];
        for (var i = 0; i < ticks.Length; i++)
        {
            ticks[i] = new AxisTick(times[i] - OriginTicks, labels[i]);
        }

        return new TickSet(ticks, [], step.Nanoseconds / NanosecondsPerUnit);
    }

    private TickSet SingleTick(long time)
    {
        var culture = Culture ?? Instruments.ChartText.Culture;
        var label = ToLocal(time).ToString("d MMM yyyy HH:mm:ss", culture);
        return new TickSet([new AxisTick(time - OriginTicks, label)], [], 0);
    }

    private enum StepKind
    {
        Fixed,
        Hours,
        Days,
        Weeks,
        Months,
        Years,
    }

    private readonly record struct TickStep(StepKind Kind, int Factor, double Nanoseconds);
}

/// <summary>
/// Maps times to pixels, skipping the gaps of the axis.
/// </summary>
public readonly struct DateTimeAxisTransform : IAxisTransform
{
    private readonly TimeGapMap? _gaps;
    private readonly long _origin;
    private readonly long _scaleStart;
    private readonly double _pixelStart;
    private readonly double _pixelsPerUnit;

    internal DateTimeAxisTransform(long first, long last, double pixelStart, double pixelEnd, TimeGapMap? gaps, long origin)
    {
        _gaps = gaps;
        _origin = origin;
        _scaleStart = gaps?.Compress(first) ?? first;
        _pixelStart = pixelStart;

        var scaleEnd = gaps?.Compress(last) ?? last;
        _pixelsPerUnit = scaleEnd > _scaleStart ? (pixelEnd - pixelStart) / (scaleEnd - _scaleStart) : 0;
    }

    /// <summary>
    /// Converts a time to a pixel position.
    /// </summary>
    public double ToPixel(long time)
    {
        var position = _gaps?.Compress(time) ?? time;
        return _pixelStart + ((position - _scaleStart) * _pixelsPerUnit);
    }

    /// <summary>
    /// Converts a pixel position to a time.
    /// </summary>
    public long ToTime(double pixel)
    {
        var position = _scaleStart + (_pixelsPerUnit == 0 ? 0 : (long)Math.Round((pixel - _pixelStart) / _pixelsPerUnit));
        return _gaps?.Expand(position) ?? position;
    }

    /// <summary>
    /// Converts many times to pixel positions.
    /// </summary>
    /// <param name="times">The times.</param>
    /// <param name="pixels">Receives the positions, as long as <paramref name="times"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public void ToPixels(ReadOnlySpan<long> times, Span<float> pixels)
    {
        if (times.Length != pixels.Length)
        {
            throw new ArgumentException("The spans must have the same length.", nameof(pixels));
        }

        for (var i = 0; i < times.Length; i++)
        {
            pixels[i] = (float)ToPixel(times[i]);
        }
    }

    /// <inheritdoc />
    /// <remarks>The value counts from <see cref="DateTimeAxis.OriginTicks"/>.</remarks>
    public double ToPixel(double value) =>
        double.IsNaN(value) ? double.NaN : ToPixel(_origin + (long)Math.Round(value));

    /// <inheritdoc />
    /// <remarks>The value counts from <see cref="DateTimeAxis.OriginTicks"/>.</remarks>
    public double ToData(double pixel) => ToTime(pixel) - _origin;

    /// <inheritdoc />
    /// <remarks>The values count from <see cref="DateTimeAxis.OriginTicks"/>.</remarks>
    public void ToPixels(ReadOnlySpan<double> values, Span<float> pixels)
    {
        if (values.Length != pixels.Length)
        {
            throw new ArgumentException("The spans must have the same length.", nameof(pixels));
        }

        for (var i = 0; i < values.Length; i++)
        {
            pixels[i] = (float)ToPixel(values[i]);
        }
    }
}
