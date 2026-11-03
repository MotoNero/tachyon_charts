using System.Numerics;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Collects metadata incrementally, so that an append only looks at the new points.
/// </summary>
internal struct MetadataScanner<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    // Tolerates the rounding of steps such as 0.1, which are not exact in binary.
    private const double UniformTolerance = 1e-9;

    private TX _lastX;
    private bool _hasPoints;
    private bool _isUnsorted;
    private bool _isNotUniform;
    private double _step;
    private double _minX;
    private double _maxX;
    private double _minY;
    private double _maxY;

    public MetadataScanner()
    {
        _minX = _minY = double.PositiveInfinity;
        _maxX = _maxY = double.NegativeInfinity;
    }

    public void Add(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        for (var i = 0; i < x.Length; i++)
        {
            AddX(x[i]);

            var value = double.CreateTruncating(y[i]);
            if (double.IsFinite(value))
            {
                _minY = Math.Min(_minY, value);
                _maxY = Math.Max(_maxY, value);
            }
        }
    }

    public readonly DataMetadata ToMetadata(int count) => new(
        count,
        !_isUnsorted,
        count >= 2 && !_isNotUniform ? _step : null,
        ToRange(_minX, _maxX),
        ToRange(_minY, _maxY));

    private static DoubleRange ToRange(double min, double max) =>
        min <= max ? new DoubleRange(min, max) : DoubleRange.Empty;

    private void AddX(TX x)
    {
        var position = double.CreateTruncating(x);
        if (double.IsFinite(position))
        {
            _minX = Math.Min(_minX, position);
            _maxX = Math.Max(_maxX, position);
        }

        if (!_hasPoints)
        {
            _hasPoints = true;
            _isUnsorted = TX.IsNaN(x);
            _isNotUniform = _isUnsorted;
        }
        else if (!(x >= _lastX)) // NOSONAR: also true for NaN, on purpose
        {
            // The negated form also catches NaN.
            _isUnsorted = true;
            _isNotUniform = true;
        }
        else if (!_isNotUniform)
        {
            UpdateStep(double.CreateTruncating(x - _lastX));
        }

        _lastX = x;
    }

    private void UpdateStep(double distance)
    {
        if (_step == 0)
        {
            _step = distance;
            _isNotUniform = !(distance > 0 && double.IsFinite(distance));
        }
        else if (Math.Abs(distance - _step) > _step * UniformTolerance)
        {
            _isNotUniform = true;
        }
    }
}
