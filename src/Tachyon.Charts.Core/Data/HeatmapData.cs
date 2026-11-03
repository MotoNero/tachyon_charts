using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// A grid of values. Writing is safe from any thread: a frame sees either the old or the new grid.
/// </summary>
public sealed class HeatmapData
{
    private Grid _grid;
    private long _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeatmapData"/> class, with all values missing.
    /// </summary>
    /// <param name="width">The number of columns, positive.</param>
    /// <param name="height">The number of rows, positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public HeatmapData(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        var values = new float[checked(width * height)];
        Array.Fill(values, float.NaN);
        _grid = new Grid(values, DoubleRange.Empty);
    }

    /// <summary>Gets the number of columns.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }

    /// <summary>Gets a version that grows with every change.</summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>Gets the range of the finite values, empty if there is none.</summary>
    public DoubleRange ValueRange => Volatile.Read(ref _grid).Range;

    internal float[] Values => Volatile.Read(ref _grid).Values;

    /// <summary>Gets a value.</summary>
    /// <param name="column">The column.</param>
    /// <param name="row">The row, zero at the lowest Y value.</param>
    /// <returns>The value, NaN if it is missing.</returns>
    public float this[int column, int row]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)column, (uint)Width, nameof(column));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)row, (uint)Height, nameof(row));
            return Values[((long)row * Width) + column];
        }
    }

    /// <summary>
    /// Replaces all values.
    /// </summary>
    /// <param name="values">The values row by row, starting at the lowest Y value. NaN means missing.</param>
    /// <exception cref="ArgumentException">The length does not match the grid.</exception>
    public void SetValues(ReadOnlySpan<float> values)
    {
        if (values.Length != (long)Width * Height)
        {
            throw new ArgumentException("The length does not match the grid.", nameof(values));
        }

        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var value in values)
        {
            if (float.IsFinite(value))
            {
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        var range = minimum <= maximum ? new DoubleRange(minimum, maximum) : DoubleRange.Empty;
        Volatile.Write(ref _grid, new Grid(values.ToArray(), range));
        Interlocked.Increment(ref _version);
    }

    private sealed record Grid(float[] Values, DoubleRange Range);
}
