namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// The reference point near the visible range. The GPU only receives floats relative to it.
/// </summary>
/// <remarks>
/// Timestamps need the <see cref="long"/> overloads. Around 6.4e17 ticks, adjacent doubles are 128
/// ticks apart, so a double cannot even hold them, while the integer difference to the origin is exact.
/// </remarks>
public readonly record struct RenderOrigin
{
    private readonly long _integer;
    private readonly double _fraction;

    private RenderOrigin(long integer, double fraction, double value)
    {
        _integer = integer;
        _fraction = fraction;
        Value = value;
    }

    /// <summary>
    /// Gets the origin as a double.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Creates an origin from a data value.
    /// </summary>
    /// <param name="value">The origin, within the range of <see cref="long"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or too large.</exception>
    public static RenderOrigin FromValue(double value)
    {
        // The upper bound is exclusive: long.MaxValue rounds up to 2^63 as a double.
        if (!double.IsFinite(value) || value < long.MinValue || value >= long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The origin must be finite and fit into a long.");
        }

        return new RenderOrigin((long)value, value - Math.Truncate(value), value);
    }

    /// <summary>
    /// Creates an origin from an integer such as a tick count, without loss of precision.
    /// </summary>
    public static RenderOrigin FromTicks(long ticks) => new(ticks, 0, ticks);

    /// <summary>
    /// Creates an origin in the middle of a range.
    /// </summary>
    /// <exception cref="InvalidOperationException">The range is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The middle does not fit into a long.</exception>
    public static RenderOrigin ForRange(DoubleRange range) => FromValue(range.Center);

    /// <summary>
    /// Splits a double into a float and the float remainder, for double-float math in shaders. Their
    /// sum holds about twice the precision of a float. Non-finite values keep a remainder of 0.
    /// </summary>
    /// <returns>The rounded value and the remainder.</returns>
    public static (float High, float Low) Split(double value)
    {
        var high = (float)value;
        return (high, double.IsFinite(high) ? (float)(value - high) : 0f);
    }

    /// <summary>
    /// Converts a value to a float relative to the origin.
    /// </summary>
    public float ToRelative(double value) => (float)RelativeDouble(value); // NOSONAR: the overloads are grouped by input type

    /// <summary>
    /// Converts an integer to a float relative to the origin. The difference is exact over the full
    /// range of <see cref="long"/>.
    /// </summary>
    public float ToRelative(long ticks) => (float)RelativeDouble(ticks);

    /// <summary>
    /// Converts a value to a relative double-float pair, see <see cref="Split"/>.
    /// </summary>
    public (float High, float Low) ToRelativeSplit(double value) => Split(RelativeDouble(value));

    /// <summary>
    /// Converts an integer to a relative double-float pair, see <see cref="Split"/>.
    /// </summary>
    public (float High, float Low) ToRelativeSplit(long ticks) => Split(RelativeDouble(ticks));

    /// <summary>
    /// Converts many values to floats relative to the origin.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <param name="relative">Receives the results, as long as <paramref name="values"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public void ToRelative(ReadOnlySpan<double> values, Span<float> relative)
    {
        ThrowIfLengthsDiffer(values.Length, relative.Length);
        for (var i = 0; i < values.Length; i++)
        {
            relative[i] = (float)(values[i] - Value);
        }
    }

    /// <summary>
    /// Converts many integers to floats relative to the origin.
    /// </summary>
    /// <param name="ticks">The integers.</param>
    /// <param name="relative">Receives the results, as long as <paramref name="ticks"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public void ToRelative(ReadOnlySpan<long> ticks, Span<float> relative)
    {
        ThrowIfLengthsDiffer(ticks.Length, relative.Length);
        for (var i = 0; i < ticks.Length; i++)
        {
            relative[i] = (float)RelativeDouble(ticks[i]);
        }
    }

    private static void ThrowIfLengthsDiffer(int sourceLength, int destinationLength)
    {
        if (sourceLength != destinationLength)
        {
            throw new ArgumentException("The spans must have the same length.", nameof(destinationLength));
        }
    }

    private double RelativeDouble(double value) => value - Value;

    // Int128 keeps the difference of two extreme longs from overflowing.
    private double RelativeDouble(long ticks) => (double)((Int128)ticks - _integer) - _fraction;
}
