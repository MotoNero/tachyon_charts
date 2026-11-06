using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Resampling;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A streaming line in a persistent GPU ring, which only receives new data. The GPU maps the vertices
/// to pixels, so scrolling costs no upload.
/// </summary>
/// <remarks>
/// <para>
/// With few visible points a slot of the ring is one point, or two vertices on a step line. Otherwise a
/// slot is the extent of a bucket of fixed X width, two vertices, a power of two near one pixel: buckets
/// stay valid while the window scrolls, and only zooming by a factor of two rebuilds them. New points
/// cost O(new points).
/// </para>
/// <para>
/// Vertices are floats relative to an anchor, never absolute values. A copy of the first
/// slot behind the last one closes the line across the wrap without shader tricks.
/// </para>
/// </remarks>
internal sealed class FifoLinePath<TX, TY>(FifoDataSource<TX, TY> source) : LinePath
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
{
    private const int ExtraSlots = 64;
    private const double MaximumAnchorDistance = 4096;

    private Vector2[] _vertices = [];
    private long[] _firstPoints = [];
    private int _slots;
    private int _verticesPerSlot = 1;
    private long _slotCounter;
    private long _firstLiveSlot;
    private long _dirtyFrom;

    private long _readVersion;
    private double _bucketWidth;
    private long _bucket;
    private double _bucketMin;
    private double _bucketMax;
    private long _minAt;
    private long _maxAt;
    private bool _isBucketOpen;
    private bool _hasPreviousY;
    private double _previousY;
    private bool _isStep;

    private TX _anchorX;
    private double _anchorY;
    private long _builtOptionsVersion = -1;
    private bool _needsRebuild = true;

    public override int DrawnPointCount => (int)(_slotCounter - _firstLiveSlot);

    public override bool TryGetDataRange(AxisBase xAxis, out DoubleRange x, out DoubleRange y)
    {
        var total = source.Version;
        x = DoubleRange.Empty;
        y = DoubleRange.Empty;
        if (total == 0)
        {
            return false;
        }

        var ring = source.X.Span;
        var origin = GetOrigin<TX>(xAxis);
        var oldest = Math.Max(total - source.Capacity, 0);
        x = DoubleRange.FromUnordered(ToAxis(ring, oldest, origin), ToAxis(ring, total - 1, origin));

        for (var slot = _firstLiveSlot; slot < _slotCounter; slot++)
        {
            var index = (int)(slot % _slots) * _verticesPerSlot;
            for (var i = 0; i < _verticesPerSlot; i++)
            {
                var vertex = _vertices[index + i];
                if (vertex.X < PolylineStyle.Gap)
                {
                    y = y.Include(_anchorY + vertex.Y);
                }
            }
        }

        return true;
    }

    protected override void OnBufferReleased() => _needsRebuild = true;

    public override void Update(IRenderDevice device, in SeriesFrame frame, AxisBase xAxis, AxisBase yAxis, in LineOptions options)
    {
        var total = source.Version;
        if (total == 0)
        {
            return;
        }

        var ring = source.X.Span;
        var capacity = source.Capacity;
        var oldest = Math.Max(total - capacity, 0);
        var origin = GetOrigin<TX>(xAxis);
        var visibleX = xAxis.VisibleRange;
        var firstVisible = LowerBound(ring, oldest, total, visibleX.Min, origin);
        var lastVisible = UpperBound(ring, firstVisible, total, visibleX.Max, origin);
        var visibleCount = lastVisible - firstVisible;

        var columns = Math.Max((int)MathF.Ceiling(frame.Bounds.Width), 1);
        var usesBuckets = options.Resampling != ResamplingMode.None && (options.Resampling != ResamplingMode.Auto || visibleCount > columns * 2L);
        var bucketWidth = usesBuckets ? GetBucketWidth(visibleX.Length, columns) : 0;
        var slots = checked((int)Math.Max((columns * 4L) + ExtraSlots, usesBuckets ? 0 : (visibleCount * 2) + 2));
        var verticesPerSlot = usesBuckets || options.IsStep ? 2 : 1;

        // Labels change the width of the plot while scrolling, which alone must not rebuild anything.
        var start = Math.Max(firstVisible - 1, oldest);
        if (_needsRebuild
            || options.Version != _builtOptionsVersion
            || slots > _slots
            || slots * 4L < _slots
            || bucketWidth != _bucketWidth
            || verticesPerSlot != _verticesPerSlot
            || _readVersion < oldest
            || IsAnchorFar(ring, firstVisible, total, origin, visibleX, yAxis.VisibleRange)
            || (_slotCounter > _firstLiveSlot && start < GetFirstStoredPoint()))
        {
            Reset(device, slots + (slots / 2), verticesPerSlot, bucketWidth, options, ring, start, yAxis.VisibleRange);
        }

        // A view far behind the newest data must not lose its slots to points it does not show.
        var limit = Math.Min(total, lastVisible + 1 + (_slots / 4));
        if (limit > _readVersion)
        {
            Ingest(limit);
            Upload();
        }

        DropForgottenSlots(oldest);
    }

    public override void Draw(ICommandList commands, in SeriesFrame frame, AxisBase xAxis, in PolylineStyle style)
    {
        if (_slotCounter == _firstLiveSlot || Buffer is null)
        {
            return;
        }

        var scaleX = frame.X.ToPixel(1) - frame.X.ToPixel(0);
        var scaleY = frame.Y.ToPixel(1) - frame.Y.ToPixel(0);
        var anchorX = double.CreateTruncating(_anchorX - GetOrigin<TX>(xAxis));
        var mapped = style with
        {
            Scale = new Vector2((float)scaleX, (float)scaleY),
            Offset = new Vector2((float)frame.X.ToPixel(anchorX), (float)frame.Y.ToPixel(_anchorY)),
        };

        var first = (int)(_firstLiveSlot % _slots);
        var stored = (int)(_slotCounter - _firstLiveSlot);
        if (first + stored <= _slots)
        {
            DrawRange(commands, first * _verticesPerSlot, stored * _verticesPerSlot, mapped);
            return;
        }

        // The first range ends with the copy of slot 0, where the second one starts.
        DrawRange(commands, first * _verticesPerSlot, (_slots + 1 - first) * _verticesPerSlot, mapped);
        DrawRange(commands, 0, (first + stored - _slots) * _verticesPerSlot, mapped);
    }

    private static double GetBucketWidth(double visibleLength, int columns)
    {
        var width = visibleLength / columns;
        return width > 0 && double.IsFinite(width) ? Math.Pow(2, Math.Floor(Math.Log2(width))) : 1;
    }

    private static double ToAxis(ReadOnlySpan<TX> ring, long point, TX origin) =>
        double.CreateTruncating(ring[(int)(point % ring.Length)] - origin);

    private static long LowerBound(ReadOnlySpan<TX> ring, long low, long high, double value, TX origin)
    {
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (ToAxis(ring, middle, origin) < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static long UpperBound(ReadOnlySpan<TX> ring, long low, long high, double value, TX origin)
    {
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (ToAxis(ring, middle, origin) <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void DrawRange(ICommandList commands, int first, int count, in PolylineStyle style)
    {
        if (count >= 2)
        {
            commands.DrawPolyline(Buffer!, first, count, style);
        }
    }

    private long GetFirstStoredPoint() => _firstPoints[(int)(_firstLiveSlot % _slots)];

    // The vertex ring can be longer than the data ring. What the data ring forgot is gone from the line too.
    private void DropForgottenSlots(long oldestPoint)
    {
        _firstLiveSlot = Math.Max(_firstLiveSlot, _slotCounter - _slots);
        while (_firstLiveSlot < _slotCounter - 1
            && _firstPoints[(int)(_firstLiveSlot % _slots)] < oldestPoint
            && _firstPoints[(int)((_firstLiveSlot + 1) % _slots)] <= oldestPoint)
        {
            _firstLiveSlot++;
        }
    }

    // Floats lose precision far from the anchor, so a new one follows the view.
    private bool IsAnchorFar(ReadOnlySpan<TX> ring, long firstVisible, long total, TX origin, DoubleRange x, DoubleRange y)
    {
        var anchor = double.CreateTruncating(_anchorX - origin);
        return Math.Abs(anchor - x.Center) > x.Length * MaximumAnchorDistance
            || Math.Abs(_anchorY - y.Center) > y.Length * MaximumAnchorDistance
            || (firstVisible < total && Math.Abs(ToAxis(ring, firstVisible, origin) - anchor) > x.Length * MaximumAnchorDistance);
    }

    private void Reset(IRenderDevice device, int slots, int verticesPerSlot, double bucketWidth, in LineOptions options, ReadOnlySpan<TX> ring, long start, DoubleRange visibleY)
    {
        if (slots > _slots || slots * 4L < _slots || verticesPerSlot != _verticesPerSlot)
        {
            _slots = slots;
            _verticesPerSlot = verticesPerSlot;
            _vertices = new Vector2[(slots + 1) * verticesPerSlot];
            _firstPoints = new long[slots];
        }

        EnsureBuffer(device, _vertices.Length * 8L, keepSize: true);
        _bucketWidth = bucketWidth;
        _isStep = options.IsStep && bucketWidth == 0;
        _builtOptionsVersion = options.Version;
        _slotCounter = 0;
        _firstLiveSlot = 0;
        _dirtyFrom = 0;
        _readVersion = start;
        _isBucketOpen = false;
        _hasPreviousY = false;
        _anchorX = ring[(int)(start % ring.Length)];
        _anchorY = double.IsFinite(visibleY.Center) ? visibleY.Center : 0;
        _needsRebuild = false;
    }

    private void Ingest(long limit)
    {
        var changes = source.GetChangesSince(_readVersion);
        var count = (int)Math.Min(changes.Count, limit - _readVersion);
        var x = source.X.Span;
        var y = source.Y.Span;
        var ringIndex = changes.First.Start;
        var point = changes.FirstIndex;

        if (_isBucketOpen)
        {
            _dirtyFrom = Math.Min(_dirtyFrom, _slotCounter - 1);
        }

        for (var i = 0; i < count; i++)
        {
            var relativeX = double.CreateTruncating(x[ringIndex] - _anchorX);
            var value = double.CreateTruncating(y[ringIndex]);
            if (_bucketWidth > 0)
            {
                AddToBucket(relativeX, value, point);
            }
            else
            {
                AddPoint(relativeX, value, point);
            }

            point++;
            if (++ringIndex == x.Length)
            {
                ringIndex = 0;
            }
        }

        _readVersion = point;

        // A producer that lapped this read left mixed points, so the slots are rebuilt from the ring.
        _needsRebuild = !source.IsIntact(changes);
    }

    private void AddPoint(double relativeX, double value, long point)
    {
        if (!double.IsFinite(value))
        {
            if (_hasPreviousY)
            {
                WriteSlot(new Vector2(PolylineStyle.Gap, 0), new Vector2(PolylineStyle.Gap, 0), point);
            }

            _hasPreviousY = false;
            return;
        }

        var x = (float)relativeX;
        var vertex = new Vector2(x, (float)(value - _anchorY));
        var entry = _isStep && _hasPreviousY ? new Vector2(x, (float)(_previousY - _anchorY)) : vertex;

        // The first point of a run comes twice: alone between gaps, it still shows as a dot.
        if (!_hasPreviousY && !_isStep)
        {
            WriteSlot(vertex, vertex, point);
        }

        WriteSlot(entry, vertex, point);
        _previousY = value;
        _hasPreviousY = true;
    }

    private void AddToBucket(double relativeX, double value, long point)
    {
        var bucket = (long)Math.Floor(relativeX / _bucketWidth);
        if (!_isBucketOpen || bucket != _bucket)
        {
            _bucket = bucket;
            _bucketMin = double.PositiveInfinity;
            _bucketMax = double.NegativeInfinity;
            _isBucketOpen = true;
            _slotCounter++;
            _firstPoints[(int)((_slotCounter - 1) % _slots)] = point;
        }

        if (double.IsFinite(value))
        {
            if (value < _bucketMin)
            {
                (_bucketMin, _minAt) = (value, point);
            }

            if (value > _bucketMax)
            {
                (_bucketMax, _maxAt) = (value, point);
            }
        }

        // The extremes in the order they came, spread over the bucket: a steady edge stays one slanted line instead of
        // a saw tooth or a staircase.
        var isEmpty = _bucketMin > _bucketMax;
        var (first, second) = _minAt <= _maxAt ? (_bucketMin, _bucketMax) : (_bucketMax, _bucketMin);
        var left = isEmpty ? PolylineStyle.Gap : (float)(bucket * _bucketWidth);
        var right = isEmpty ? PolylineStyle.Gap : (float)((bucket + 1) * _bucketWidth);
        var slot = (int)((_slotCounter - 1) % _slots) * _verticesPerSlot;
        _vertices[slot] = new Vector2(left, isEmpty ? 0 : (float)(first - _anchorY));
        _vertices[slot + 1] = new Vector2(right, isEmpty ? 0 : (float)(second - _anchorY));
    }

    private void WriteSlot(Vector2 first, Vector2 second, long point)
    {
        var slot = (int)(_slotCounter % _slots);
        if (_verticesPerSlot == 1)
        {
            _vertices[slot] = second;
        }
        else
        {
            _vertices[slot * 2] = first;
            _vertices[(slot * 2) + 1] = second;
        }

        _firstPoints[slot] = point;
        _slotCounter++;
    }

    private void Upload()
    {
        var from = Math.Max(_dirtyFrom, _slotCounter - _slots);
        var count = (int)(_slotCounter - from);
        if (count <= 0)
        {
            return;
        }

        var firstSlot = (int)(from % _slots);
        var firstCount = Math.Min(count, _slots - firstSlot);
        WriteVertices(firstSlot, firstCount);
        if (count > firstCount)
        {
            WriteVertices(0, count - firstCount);
        }

        if (firstSlot == 0 || count > firstCount)
        {
            _vertices.AsSpan(0, _verticesPerSlot).CopyTo(_vertices.AsSpan(_slots * _verticesPerSlot));
            WriteVertices(_slots, 1);
        }

        _dirtyFrom = _slotCounter;
    }

    private void WriteVertices(int slot, int count) =>
        Buffer!.Write<Vector2>(slot * _verticesPerSlot * 8L, _vertices.AsSpan(slot * _verticesPerSlot, count * _verticesPerSlot));
}
