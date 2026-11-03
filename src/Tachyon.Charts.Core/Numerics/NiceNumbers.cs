namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// Round numbers of the form 1, 2 or 5 times a power of ten, as used for axis ticks.
/// </summary>
public static class NiceNumbers
{
    /// <summary>
    /// Finds the nice tick step that divides a range into about the wanted number of intervals.
    /// </summary>
    /// <param name="length">The length of the range, positive and finite.</param>
    /// <param name="targetIntervals">The wanted number of intervals, positive.</param>
    /// <returns>The step.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public static double Step(double length, int targetIntervals)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetIntervals);
        if (!double.IsFinite(length) || length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The length must be positive and finite.");
        }

        return RoundToNice(length / targetIntervals);
    }

    /// <summary>
    /// Finds the range that a tick step extends a range to, aligned to multiples of the step.
    /// </summary>
    /// <param name="range">The range, not empty.</param>
    /// <param name="step">The step, positive and finite.</param>
    /// <returns>The aligned range, which contains <paramref name="range"/>.</returns>
    /// <exception cref="ArgumentException">The range is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The step is out of range.</exception>
    public static DoubleRange AlignToStep(DoubleRange range, double step)
    {
        if (range.IsEmpty)
        {
            throw new ArgumentException("An empty range cannot be aligned.", nameof(range));
        }

        if (!double.IsFinite(step) || step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive and finite.");
        }

        var min = Math.Floor(range.Min / step) * step;
        var max = Math.Ceiling(range.Max / step) * step;

        // Rounding or overflow must not cut the range.
        return new DoubleRange(
            double.IsFinite(min) ? Math.Min(min, range.Min) : range.Min,
            double.IsFinite(max) ? Math.Max(max, range.Max) : range.Max);
    }

    /// <summary>
    /// Rounds a positive value to the nearest nice number on a logarithmic scale.
    /// </summary>
    private static double RoundToNice(double value)
    {
        var exponent = Math.Floor(Math.Log10(value));
        var power = Math.Pow(10, exponent);

        // Subnormal values have no representable power of ten.
        if (power == 0 || !double.IsFinite(power))
        {
            return value;
        }

        var fraction = value / power;
        var nice = fraction switch
        {
            < 1.5 => 1,
            < 3.5 => 2,
            < 7.5 => 5,
            _ => 10,
        };

        var result = nice * power;
        return double.IsFinite(result) ? result : value;
    }
}
