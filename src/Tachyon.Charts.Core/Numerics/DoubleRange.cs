namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// A closed interval of finite numbers, or the empty range that collects values through <see cref="Include"/>.
/// </summary>
public readonly record struct DoubleRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DoubleRange"/> struct.
    /// </summary>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound, not below <paramref name="min"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not finite, or the bounds are inverted.</exception>
    public DoubleRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), $"The range [{min}, {max}] is invalid.");
        }

        Min = min;
        Max = max;
    }

    private DoubleRange(bool empty)
    {
        Min = empty ? double.PositiveInfinity : 0;
        Max = empty ? double.NegativeInfinity : 0;
    }

    /// <summary>
    /// Gets the range without values, the neutral element of <see cref="Include"/> and <see cref="Union"/>.
    /// </summary>
    public static DoubleRange Empty { get; } = new(true);

    /// <summary>
    /// Gets the lower bound.
    /// </summary>
    public double Min { get; }

    /// <summary>
    /// Gets the upper bound.
    /// </summary>
    public double Max { get; }

    /// <summary>
    /// Gets a value indicating whether the range contains no values.
    /// </summary>
    public bool IsEmpty => Min > Max;

    /// <summary>
    /// Gets the distance between the bounds. It is 0 for an empty range, and infinite if it exceeds <see cref="double.MaxValue"/>.
    /// </summary>
    public double Length => IsEmpty ? 0 : Max - Min;

    /// <summary>
    /// Gets the middle of the range, which does not overflow for large bounds.
    /// </summary>
    /// <exception cref="InvalidOperationException">The range is empty.</exception>
    public double Center => IsEmpty
        ? throw new InvalidOperationException("An empty range has no center.")
        : (Min / 2) + (Max / 2);

    /// <summary>
    /// Creates a range from two bounds in any order.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A bound is not finite.</exception>
    public static DoubleRange FromUnordered(double first, double second) =>
        first <= second ? new DoubleRange(first, second) : new DoubleRange(second, first);

    /// <summary>
    /// Tests whether a value lies in the range.
    /// </summary>
    public bool Contains(double value) => value >= Min && value <= Max;

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "[empty]" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[{Min}, {Max}]");

    /// <summary>
    /// Returns the smallest range that also contains a value. NaN and infinite values are ignored,
    /// as they occur as gaps in data.
    /// </summary>
    public DoubleRange Include(double value)
    {
        if (!double.IsFinite(value))
        {
            return this;
        }

        return IsEmpty ? new DoubleRange(value, value) : new DoubleRange(Math.Min(Min, value), Math.Max(Max, value));
    }

    /// <summary>
    /// Returns the smallest range that contains both ranges.
    /// </summary>
    public DoubleRange Union(DoubleRange other)
    {
        if (IsEmpty)
        {
            return other;
        }

        return other.IsEmpty ? this : new DoubleRange(Math.Min(Min, other.Min), Math.Max(Max, other.Max));
    }
}
