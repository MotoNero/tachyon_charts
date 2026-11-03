using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// A ring of rows of equal length: every new row replaces the oldest. Appending is safe from any thread.
/// </summary>
public sealed class SpectrogramData
{
    private readonly object _gate = new();
    private readonly float[] _values;
    private readonly float[] _minimums;
    private readonly float[] _maximums;
    private long _totalRows;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrogramData"/> class.
    /// </summary>
    /// <param name="columns">The length of a row, positive.</param>
    /// <param name="rows">The number of rows kept, positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public SpectrogramData(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        Columns = columns;
        Rows = rows;
        _values = new float[checked(columns * rows)];
        Array.Fill(_values, float.NaN);
        _minimums = new float[rows];
        _maximums = new float[rows];
        Array.Fill(_minimums, float.PositiveInfinity);
        Array.Fill(_maximums, float.NegativeInfinity);
    }

    /// <summary>Gets the length of a row.</summary>
    public int Columns { get; }

    /// <summary>Gets the number of rows kept.</summary>
    public int Rows { get; }

    /// <summary>Gets the number of rows appended so far.</summary>
    public long TotalRows => Interlocked.Read(ref _totalRows);

    /// <summary>Gets the range of the finite values kept, empty if there is none.</summary>
    public DoubleRange ValueRange
    {
        get
        {
            lock (_gate)
            {
                var minimum = float.PositiveInfinity;
                var maximum = float.NegativeInfinity;
                for (var i = 0; i < Rows; i++)
                {
                    minimum = Math.Min(minimum, _minimums[i]);
                    maximum = Math.Max(maximum, _maximums[i]);
                }

                return minimum <= maximum ? new DoubleRange(minimum, maximum) : DoubleRange.Empty;
            }
        }
    }

    /// <summary>
    /// Appends a row.
    /// </summary>
    /// <param name="row">The values. NaN means missing.</param>
    /// <exception cref="ArgumentException">The length does not match.</exception>
    public void AppendRow(ReadOnlySpan<float> row)
    {
        if (row.Length != Columns)
        {
            throw new ArgumentException("The length does not match a row.", nameof(row));
        }

        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var value in row)
        {
            if (float.IsFinite(value))
            {
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }

        lock (_gate)
        {
            var slot = (int)(_totalRows % Rows);
            row.CopyTo(_values.AsSpan(slot * Columns, Columns));
            _minimums[slot] = minimum;
            _maximums[slot] = maximum;
            Interlocked.Increment(ref _totalRows);
        }
    }

    /// <summary>
    /// Reads a value.
    /// </summary>
    /// <param name="row">The number of the row since the start.</param>
    /// <param name="column">The column.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns>Whether the ring still holds the row.</returns>
    public bool TryRead(long row, int column, out float value)
    {
        lock (_gate)
        {
            if (row < Math.Max(_totalRows - Rows, 0) || row >= _totalRows || column < 0 || column >= Columns)
            {
                value = float.NaN;
                return false;
            }

            value = _values[((row % Rows) * Columns) + column];
            return true;
        }
    }

    // Under the lock, so that a row is never half written. Every row is its own write.
    internal long Upload(IGpuTexture texture, long uploaded, ref long uploadedRows)
    {
        lock (_gate)
        {
            var missing = _totalRows - uploaded;
            if (uploaded < 0 || missing >= Rows)
            {
                texture.Write(MemoryMarshal.AsBytes(_values.AsSpan()));
                uploadedRows += Rows;
                return _totalRows;
            }

            for (var row = uploaded; row < _totalRows; row++)
            {
                var slot = (int)(row % Rows);
                texture.WriteRow(slot, MemoryMarshal.AsBytes(_values.AsSpan(slot * Columns, Columns)));
                uploadedRows++;
            }

            return _totalRows;
        }
    }
}
