using System.Numerics;

namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>
/// Reduces a visible range to one minimum and maximum per pixel column. Spike code: the real
/// decimation with LOD pyramid belongs to Core.
/// </summary>
public sealed class MinMaxDecimator
{
    private readonly float[] _minima;
    private readonly float[] _maxima;

    /// <summary>
    /// Initializes a new instance of the <see cref="MinMaxDecimator"/> class.
    /// </summary>
    /// <param name="columns">The number of pixel columns.</param>
    public MinMaxDecimator(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        _minima = new float[columns];
        _maxima = new float[columns];
    }

    /// <summary>
    /// Gets the largest number of points <see cref="Decimate"/> can write.
    /// </summary>
    public int MaxPointCount => _minima.Length * 2;

    /// <summary>
    /// Writes the polyline for the range <paramref name="start"/> to <paramref name="start"/> + <paramref name="length"/>.
    /// </summary>
    /// <param name="values">The samples, evenly spaced.</param>
    /// <param name="start">The first visible sample.</param>
    /// <param name="length">The number of visible samples.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="map">Maps a sample value to a vertical pixel position.</param>
    /// <param name="points">Receives the points.</param>
    /// <returns>The number of points written.</returns>
    public int Decimate(float[] values, long start, long length, float width, YMapping map, Span<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, values.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, values.Length - start);

        if (length <= MaxPointCount)
        {
            return WriteRaw(values, (int)start, (int)length, width, map, points);
        }

        var columns = _minima.Length;
        Parallel.For(0, columns, column =>
        {
            var first = start + (length * column / columns);
            var next = start + (length * (column + 1) / columns);
            (_minima[column], _maxima[column]) = FindRange(values.AsSpan((int)first, (int)(next - first)));
        });

        for (var column = 0; column < columns; column++)
        {
            var x = (column + 0.5f) * width / columns;
            points[column * 2] = new Vector2(x, map.ToPixel(_minima[column]));
            points[(column * 2) + 1] = new Vector2(x, map.ToPixel(_maxima[column]));
        }

        return columns * 2;
    }

    private static int WriteRaw(float[] values, int start, int length, float width, YMapping map, Span<Vector2> points)
    {
        var step = length > 1 ? (double)width / (length - 1) : 0;
        for (var i = 0; i < length; i++)
        {
            points[i] = new Vector2((float)(i * step), map.ToPixel(values[start + i]));
        }

        return length;
    }

    private static (float Min, float Max) FindRange(ReadOnlySpan<float> values)
    {
        var index = 0;
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;

        if (values.Length >= Vector<float>.Count)
        {
            var minima = new Vector<float>(values);
            var maxima = minima;
            for (index = Vector<float>.Count; index <= values.Length - Vector<float>.Count; index += Vector<float>.Count)
            {
                var block = new Vector<float>(values[index..]);
                minima = Vector.Min(minima, block);
                maxima = Vector.Max(maxima, block);
            }

            for (var lane = 0; lane < Vector<float>.Count; lane++)
            {
                min = Math.Min(min, minima[lane]);
                max = Math.Max(max, maxima[lane]);
            }
        }

        for (; index < values.Length; index++)
        {
            min = Math.Min(min, values[index]);
            max = Math.Max(max, values[index]);
        }

        return (min, max);
    }
}

/// <summary>
/// Maps sample values to vertical pixel positions.
/// </summary>
/// <param name="Bottom">The value at the bottom edge.</param>
/// <param name="Top">The value at the top edge.</param>
/// <param name="Height">The target height in pixels.</param>
public readonly record struct YMapping(double Bottom, double Top, float Height)
{
    /// <summary>
    /// Converts a value to a pixel position, with the origin at the top.
    /// </summary>
    public float ToPixel(double value) => (float)((Top - value) / (Top - Bottom) * Height);
}
