using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// A linear axis with ticks on round numbers.
/// </summary>
/// <param name="id">The identifier that series refer to.</param>
/// <param name="position">The side of the plot area.</param>
public sealed class NumericAxis(string id, AxisPosition position) : AxisBase(id, position)
{
    private const double SnapTolerance = 1e-9;
    private const double MinimumTickSpacing = 48;
    private const double LabelGap = 8;
    private const double MinimumMinorSpacing = 4;
    private const int MaximumCoarsening = 64;

    /// <summary>
    /// Gets or sets how labels are written.
    /// </summary>
    public NumericLabelFormat LabelFormat { get; set; } = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The length is negative or not finite.</exception>
    public override TickSet GenerateTicks(double length, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        if (!double.IsFinite(length) || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The length must not be negative.");
        }

        var range = VisibleRange;
        if (length == 0 || !double.IsFinite(range.Length))
        {
            return TickSet.Empty;
        }

        if (range.Length == 0)
        {
            return SingleTick(range.Min);
        }

        var intervals = Math.Max((int)(length / Math.Max(MinimumTickSpacing, measurer.LineHeight + LabelGap)), 1);
        var step = NiceNumbers.Step(range.Length, intervals);

        for (var attempt = 0; attempt < MaximumCoarsening; attempt++)
        {
            var values = CreateTicks(range, step);
            if (values.Length == 0)
            {
                return SingleTick(range.Center);
            }

            var labels = new string[values.Length];
            LabelFormat.FormatTicks(values, step, labels);
            if (Fits(values, labels, step, length / range.Length, measurer))
            {
                return new TickSet(ToTicks(values, labels), CreateMinorTicks(range, step, length), step);
            }

            step = NextStep(step);
        }

        return SingleTick(range.Center);
    }

    private static double NextStep(double step)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(step) + SnapTolerance));
        var mantissa = Math.Round(step / power);
        return mantissa switch
        {
            1 => 2 * power,
            2 => 5 * power,
            _ => 10 * power,
        };
    }

    private static double[] CreateTicks(DoubleRange range, double step)
    {
        var first = Math.Ceiling((range.Min / step) - SnapTolerance);
        var last = Math.Floor((range.Max / step) + SnapTolerance);
        var count = last - first + 1;
        if (!(count >= 1) || count > 100_000 || first * step == (first + 1) * step)
        {
            return [];
        }

        var values = new double[(int)count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Snap((first + i) * step, step);
        }

        return values;
    }

    private static double[] CreateMinorTicks(DoubleRange range, double step, double length)
    {
        var divisions = Math.Round(step / Math.Pow(10, Math.Floor(Math.Log10(step) + SnapTolerance))) == 2 ? 4 : 5;
        var minorStep = step / divisions;
        if (minorStep * (length / range.Length) < MinimumMinorSpacing)
        {
            return [];
        }

        var first = Math.Ceiling((range.Min / minorStep) - SnapTolerance);
        var last = Math.Floor((range.Max / minorStep) + SnapTolerance);
        var minor = new List<double>();
        for (var index = first; index <= last; index++)
        {
            // Every n-th minor tick falls on a major tick.
            if (Math.Abs(Math.IEEERemainder(index, divisions)) > 0.5)
            {
                minor.Add(Snap(index * minorStep, minorStep));
            }
        }

        return [.. minor];
    }

    // Multiples such as 3 * 0.1 miss the round value by a rounding error, and zero must be exact.
    private static double Snap(double value, double step) =>
        Math.Abs(value) < step * SnapTolerance ? 0 : value;

    private static AxisTick[] ToTicks(double[] values, string[] labels)
    {
        var ticks = new AxisTick[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            ticks[i] = new AxisTick(values[i], labels[i]);
        }

        return ticks;
    }

    private bool Fits(double[] values, string[] labels, double step, double unitsPerData, ITextMeasurer measurer)
    {
        var spacing = step * unitsPerData;
        if (values.Length < 2)
        {
            return true;
        }

        if (!IsHorizontal)
        {
            return spacing >= measurer.LineHeight + LabelGap;
        }

        // Centered labels: half of each neighbor reaches into the space between two ticks.
        for (var i = 1; i < labels.Length; i++)
        {
            var needed = ((measurer.MeasureWidth(labels[i - 1]) + measurer.MeasureWidth(labels[i])) / 2) + LabelGap;
            if (spacing < needed)
            {
                return false;
            }
        }

        return true;
    }

    private TickSet SingleTick(double value)
    {
        var labels = new string[1];
        var magnitude = value != 0 ? Math.Abs(value) : 1;
        LabelFormat.FormatTicks([value], magnitude / 1000, labels);
        return new TickSet([new AxisTick(value, labels[0])], [], 0);
    }
}
