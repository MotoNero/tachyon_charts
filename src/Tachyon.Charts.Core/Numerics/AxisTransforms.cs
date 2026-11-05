using System.Numerics;

namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// Maps data values of an axis to pixel positions and back.
/// </summary>
/// <remarks>
/// Implementations are structs, so that hot paths use them through a generic constraint without boxing.
/// </remarks>
public interface IAxisTransform
{
    /// <summary>
    /// Converts a data value to a pixel position. NaN stays NaN.
    /// </summary>
    double ToPixel(double value);

    /// <summary>
    /// Converts a pixel position to a data value.
    /// </summary>
    double ToData(double pixel);

    /// <summary>
    /// Converts many data values to pixel positions.
    /// </summary>
    /// <param name="values">The data values.</param>
    /// <param name="pixels">Receives the positions, as long as <paramref name="values"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    void ToPixels(ReadOnlySpan<double> values, Span<float> pixels);
}

/// <summary>
/// A mapping that is linear after an optional <see cref="AxisScaling"/>. The pixel positions may be
/// inverted, as usual for vertical axes.
/// </summary>
public readonly struct LinearAxisTransform : IAxisTransform, IEquatable<LinearAxisTransform>
{
    private readonly double _dataOrigin;
    private readonly double _pixelOrigin;
    private readonly double _pixelsPerUnit;
    private readonly AxisScaling? _scaling;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearAxisTransform"/> struct for a nonlinear scale.
    /// </summary>
    /// <param name="data">The visible data range, not empty.</param>
    /// <param name="pixelAtMin">The position of <see cref="DoubleRange.Min"/>.</param>
    /// <param name="pixelAtMax">The position of <see cref="DoubleRange.Max"/>.</param>
    /// <param name="scaling">The scale, or <see langword="null"/> for a linear one.</param>
    /// <exception cref="ArgumentException">The range is empty or too long, or a position is not finite.</exception>
    public LinearAxisTransform(DoubleRange data, double pixelAtMin, double pixelAtMax, AxisScaling? scaling)
        : this(scaling is null ? data : new DoubleRange(scaling.Forward(data.Min), scaling.Forward(data.Max)), pixelAtMin, pixelAtMax)
    {
        _scaling = scaling;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearAxisTransform"/> struct. A range of one
    /// value maps to the middle of the pixel span, so that a single data point stays visible.
    /// </summary>
    /// <param name="data">The visible data range, not empty.</param>
    /// <param name="pixelAtMin">The position of <see cref="DoubleRange.Min"/>.</param>
    /// <param name="pixelAtMax">The position of <see cref="DoubleRange.Max"/>.</param>
    /// <exception cref="ArgumentException">The range is empty or too long, or a position is not finite.</exception>
    public LinearAxisTransform(DoubleRange data, double pixelAtMin, double pixelAtMax)
    {
        if (data.IsEmpty || !double.IsFinite(data.Length))
        {
            throw new ArgumentException($"The range [{data.Min}, {data.Max}] cannot be mapped.", nameof(data));
        }

        if (!double.IsFinite(pixelAtMin) || !double.IsFinite(pixelAtMax))
        {
            throw new ArgumentException("The pixel positions must be finite.", nameof(pixelAtMin));
        }

        _dataOrigin = data.Min;
        if (data.Length > 0)
        {
            _pixelOrigin = pixelAtMin;
            _pixelsPerUnit = (pixelAtMax - pixelAtMin) / data.Length;
        }
        else
        {
            _pixelOrigin = (pixelAtMin / 2) + (pixelAtMax / 2);
            _pixelsPerUnit = 0;
        }
    }

    /// <summary>
    /// Compares two transforms.
    /// </summary>
    /// <param name="left">The first transform.</param>
    /// <param name="right">The second transform.</param>
    /// <returns>Whether both map all values to the same pixels.</returns>
    public static bool operator ==(LinearAxisTransform left, LinearAxisTransform right) => left.Equals(right);

    /// <summary>
    /// Compares two transforms.
    /// </summary>
    /// <param name="left">The first transform.</param>
    /// <param name="right">The second transform.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(LinearAxisTransform left, LinearAxisTransform right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(LinearAxisTransform other) =>
        _dataOrigin.Equals(other._dataOrigin) && _pixelOrigin.Equals(other._pixelOrigin) && _pixelsPerUnit.Equals(other._pixelsPerUnit) && ReferenceEquals(_scaling, other._scaling);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LinearAxisTransform other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_dataOrigin, _pixelOrigin, _pixelsPerUnit);

    private LinearAxisTransform(double dataOrigin, double pixelOrigin, double pixelsPerUnit, AxisScaling? scaling) =>
        (_dataOrigin, _pixelOrigin, _pixelsPerUnit, _scaling) = (dataOrigin, pixelOrigin, pixelsPerUnit, scaling);

    /// <inheritdoc />
    public double ToPixel(double value) =>
        ToPixelLinear(_scaling is null ? value : _scaling.Forward(value));

    /// <summary>Gets the same mapping with each pixel p moved to sum − p, as a quarter turn needs.</summary>
    /// <param name="sum">The sum.</param>
    /// <returns>The mirrored mapping.</returns>
    internal LinearAxisTransform Mirrored(double sum) => new(_dataOrigin, sum - _pixelOrigin, -_pixelsPerUnit, _scaling);

    /// <summary>Gets a value indicating whether pixels follow values in a straight line.</summary>
    public bool IsLinear => _scaling is null;

    /// <summary>Gets the pixels per unit of the linear space, for mappings on the GPU.</summary>
    public double PixelsPerUnit => _pixelsPerUnit;

    /// <summary>Converts a position of the linear space to a pixel.</summary>
    /// <param name="value">The value after the scale.</param>
    /// <returns>The pixel.</returns>
    public double ToPixelLinear(double value) => _pixelOrigin + ((value - _dataOrigin) * _pixelsPerUnit);

    /// <inheritdoc />
    /// <remarks>Without a pixel span or data extent, every position maps to the lower data bound.</remarks>
    public double ToData(double pixel)
    {
        var value = _pixelsPerUnit == 0 ? _dataOrigin : _dataOrigin + ((pixel - _pixelOrigin) / _pixelsPerUnit);
        return _scaling is null ? value : _scaling.Inverse(value);
    }

    /// <inheritdoc />
    public void ToPixels(ReadOnlySpan<double> values, Span<float> pixels)
    {
        if (values.Length != pixels.Length)
        {
            throw new ArgumentException("The spans must have the same length.", nameof(pixels));
        }

        var index = 0;
        var blockLength = Vector<float>.Count;
        if (_scaling is null && Vector.IsHardwareAccelerated && values.Length >= blockLength)
        {
            var dataOrigin = new Vector<double>(_dataOrigin);
            var pixelOrigin = new Vector<double>(_pixelOrigin);
            var scale = new Vector<double>(_pixelsPerUnit);
            var halfLength = Vector<double>.Count;

            for (; index <= values.Length - blockLength; index += blockLength)
            {
                var low = pixelOrigin + ((new Vector<double>(values[index..]) - dataOrigin) * scale);
                var high = pixelOrigin + ((new Vector<double>(values[(index + halfLength)..]) - dataOrigin) * scale);
                Vector.Narrow(low, high).CopyTo(pixels[index..]);
            }
        }

        for (; index < values.Length; index++)
        {
            pixels[index] = (float)ToPixel(values[index]);
        }
    }
}
