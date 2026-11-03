using System.Numerics;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Core.HitTesting;

/// <summary>What counts as the nearest point.</summary>
public enum HitTestMode
{
    /// <summary>The nearest point on the screen.</summary>
    Nearest,

    /// <summary>The point nearest in X, whatever its Y value. Suits a vertical cursor line.</summary>
    NearestX,
}

/// <summary>A point found under a position.</summary>
/// <param name="Series">The series.</param>
/// <param name="Index">The index in the data. For a ring, the running number of the point.</param>
/// <param name="X">The X value in units of the axis.</param>
/// <param name="Y">The Y value.</param>
/// <param name="Distance">The distance from the position in device-independent units.</param>
public readonly record struct HitResult(ChartSeries Series, long Index, double X, double Y, double Distance)
{
    /// <summary>Gets the value of a cell, for series that color values.</summary>
    public double? Value { get; init; }

    /// <summary>Gets the Y value as text, with the value of a cell if there is one.</summary>
    internal string FormatY() => Value is { } value && Series.ColorAxis is { } colorAxis
        ? $"{Series.YAxis.FormatCursorValue(Y)} = {colorAxis.FormatCursorValue(value)}"
        : Series.YAxis.FormatCursorValue(Y);
}

/// <summary>
/// Finds points of a data source. Indexes grow in the background; until one is ready or after new
/// points arrived, the remaining points are scanned, with the same result.
/// </summary>
/// <remarks>Safe from any thread.</remarks>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
internal sealed class SeriesHitTester<TX, TY>(IDataSource<TX, TY> source) : IThreadPoolWorkItem
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private const double RebuildShare = 0.05;
    private const int MinimumIndexedPoints = 4096;

    private volatile IndexState? _state;
    private int _isBuilding;

    /// <summary>Finds the nearest point.</summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="origin">The value zero of the X axis in data units.</param>
    /// <param name="pixel">The position in physical pixels.</param>
    /// <param name="maximumDistance">The largest distance in physical pixels.</param>
    /// <param name="mode">What counts as nearest.</param>
    /// <param name="index">Receives the index.</param>
    /// <param name="x">Receives the X value in units of the axis.</param>
    /// <param name="y">Receives the Y value.</param>
    /// <returns>Whether a point is close enough.</returns>
    public bool TryFind(in SeriesFrame frame, TX origin, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        if (!frame.X.IsLinear || !frame.Y.IsLinear)
        {
            return TryFindInPixels(frame, origin, pixel, maximumDistance, mode, out index, out x, out y);
        }

        var query = new HitQuery(
            frame.X.ToData(pixel.X),
            frame.Y.ToData(pixel.Y),
            frame.X.ToPixel(1) - frame.X.ToPixel(0),
            mode == HitTestMode.NearestX ? 0 : frame.Y.ToPixel(1) - frame.Y.ToPixel(0),
            maximumDistance);

        return source is FifoDataSource<TX, TY> ring
            ? TryFindInRing(ring, origin, query, out index, out x, out y)
            : TryFindInSnapshot(origin, query, out index, out x, out y);
    }

    // The indexes measure in data units, which a nonlinear axis bends. This scans in pixels instead.
    private bool TryFindInPixels(in SeriesFrame frame, TX origin, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        var snapshot = source.GetSnapshot();
        var xValues = snapshot.X.Span;
        var yValues = snapshot.Y.Span;
        var firstIndex = source is FifoDataSource<TX, TY> ? snapshot.Version - xValues.Length : 0;
        var best = maximumDistance * maximumDistance;
        index = -1;
        x = 0;
        y = 0;
        for (var i = 0; i < xValues.Length; i++)
        {
            var valueX = double.CreateTruncating(xValues[i] - origin);
            var valueY = double.CreateTruncating(yValues[i]);
            var dx = frame.X.ToPixel(valueX) - pixel.X;
            var dy = mode == HitTestMode.NearestX ? 0 : frame.Y.ToPixel(valueY) - pixel.Y;
            var distance = (dx * dx) + (dy * dy);
            if (double.IsFinite(distance) && double.IsFinite(valueY) && distance < best)
            {
                best = distance;
                index = firstIndex + i;
                x = valueX;
                y = valueY;
            }
        }

        return index >= 0;
    }

    /// <summary>Collects the points inside an area.</summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="origin">The value zero of the X axis in data units.</param>
    /// <param name="area">The area in physical pixels.</param>
    /// <param name="indices">Receives the numbers of the points.</param>
    public void CollectPoints(in SeriesFrame frame, TX origin, in SelectionArea area, List<long> indices)
    {
        // A ring is sorted by contract and numbers its points by running number.
        var snapshot = source.GetSnapshot();
        var isRing = source is FifoDataSource<TX, TY>;
        var firstIndex = isRing ? snapshot.Version - snapshot.X.Length : 0;
        PointSelection.Collect(snapshot.X.Span, snapshot.Y.Span, origin, frame, area, isRing || snapshot.Metadata.IsSortedX, firstIndex, indices);
    }

    /// <summary>Reads a point by its number.</summary>
    /// <param name="origin">The value zero of the X axis in data units.</param>
    /// <param name="index">The number of the point.</param>
    /// <param name="x">Receives the X value in units of the axis.</param>
    /// <param name="y">Receives the Y value.</param>
    /// <returns>Whether the point still exists.</returns>
    public bool TryReadPoint(TX origin, long index, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (source is FifoDataSource<TX, TY> ring)
        {
            if (index < ring.Version - ring.Count || index >= ring.Version)
            {
                return false;
            }

            var slot = (int)(index % ring.Capacity);
            x = double.CreateTruncating(ring.X.Span[slot] - origin);
            y = double.CreateTruncating(ring.Y.Span[slot]);
            return true;
        }

        var snapshot = source.GetSnapshot();
        if (index < 0 || index >= snapshot.X.Length)
        {
            return false;
        }

        x = double.CreateTruncating(snapshot.X.Span[(int)index] - origin);
        y = double.CreateTruncating(snapshot.Y.Span[(int)index]);
        return true;
    }

    /// <summary>Brings the index up to date on the calling thread.</summary>
    public void BuildNow(TX origin) => Build(source.GetSnapshot(), origin);

    /// <inheritdoc />
    void IThreadPoolWorkItem.Execute()
    {
        try
        {
            Build(source.GetSnapshot(), _pendingOrigin);
        }
        finally
        {
            Volatile.Write(ref _isBuilding, 0);
        }
    }

    private TX _pendingOrigin;

    private bool TryFindInSnapshot(TX origin, in HitQuery query, out long index, out double x, out double y)
    {
        var snapshot = source.GetSnapshot();
        var state = CurrentState(snapshot, origin);
        var xValues = snapshot.X.Span;
        var yValues = snapshot.Y.Span;

        int found;
        if (!snapshot.Metadata.IsSortedX)
        {
            found = state?.Grid?.Find(xValues, yValues, origin, query) ?? new GridHitIndex<TX, TY>().Find(xValues, yValues, origin, query);
        }
        else
        {
            found = (state?.Sorted ?? new SortedHitIndex<TX, TY>()).Find(xValues, yValues, origin, query);
        }

        index = found;
        x = found >= 0 ? double.CreateTruncating(xValues[found] - origin) : 0;
        y = found >= 0 ? double.CreateTruncating(yValues[found]) : 0;
        return found >= 0;
    }

    // A ring moves under the reader, so it gets no index: X is sorted and the window is small on screen.
    private static bool TryFindInRing(FifoDataSource<TX, TY> ring, TX origin, in HitQuery query, out long index, out double x, out double y)
    {
        var changes = ring.GetChangesSince(0);
        var xValues = ring.X.Span;
        var yValues = ring.Y.Span;
        var first = changes.FirstIndex;
        var end = changes.Version;
        var low = FindFirstInRing(xValues, origin, first, end, query.X);
        var best = query.MaximumDistance * query.MaximumDistance;
        var found = -1L;
        x = 0;
        y = 0;
        ScanRing(xValues, yValues, origin, query, low - 1, -1, first, end, ref best, ref found, ref x, ref y);
        ScanRing(xValues, yValues, origin, query, low, 1, first, end, ref best, ref found, ref x, ref y);
        index = found;
        return found >= 0;
    }

    private static long FindFirstInRing(ReadOnlySpan<TX> xValues, TX origin, long first, long end, double target)
    {
        var low = first;
        var high = end;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (double.CreateTruncating(xValues[(int)(middle % xValues.Length)] - origin) < target)
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

    private static void ScanRing(ReadOnlySpan<TX> xValues, ReadOnlySpan<TY> yValues, TX origin, in HitQuery query, long start, int direction, long first, long end, ref double best, ref long found, ref double x, ref double y)
    {
        var scaleX = Math.Abs(query.ScaleX);
        for (var point = start; point >= first && point < end; point += direction)
        {
            var slot = (int)(point % xValues.Length);
            var valueX = double.CreateTruncating(xValues[slot] - origin);
            var offset = Math.Abs(valueX - query.X) * scaleX;
            if (offset * offset > best)
            {
                break;
            }

            var valueY = double.CreateTruncating(yValues[slot]);
            if (!double.IsFinite(valueY))
            {
                continue;
            }

            var distance = query.Measure(valueX, valueY);
            if (distance < best || (distance == best && (found < 0 || point < found)))
            {
                best = distance;
                found = point;
                x = valueX;
                y = valueY;
            }
        }
    }

    private IndexState? CurrentState(in DataSnapshot<TX, TY> snapshot, TX origin)
    {
        var state = _state;
        if (state is not null && (state.Epoch != snapshot.ContentEpoch || state.Origin != origin))
        {
            state = null;
        }

        var indexed = state?.IndexedCount ?? 0;
        var count = snapshot.X.Length;
        if (count >= MinimumIndexedPoints && count - indexed > count * RebuildShare && Interlocked.CompareExchange(ref _isBuilding, 1, 0) == 0)
        {
            _pendingOrigin = origin;
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        return state;
    }

    private void Build(in DataSnapshot<TX, TY> snapshot, TX origin)
    {
        var state = new IndexState(snapshot.ContentEpoch, origin);
        if (snapshot.Metadata.IsSortedX)
        {
            state.Sorted = new SortedHitIndex<TX, TY>();
            state.Sorted.Build(snapshot.Y.Span);
            state.IndexedCount = state.Sorted.IndexedCount;
        }
        else
        {
            state.Grid = new GridHitIndex<TX, TY>();
            state.Grid.Build(snapshot.X.Span, snapshot.Y.Span, origin);
            state.IndexedCount = state.Grid.IndexedCount;
        }

        _state = state;
    }

    private sealed class IndexState(long epoch, TX origin)
    {
        public long Epoch { get; } = epoch;

        public TX Origin { get; } = origin;

        public int IndexedCount { get; set; }

        public SortedHitIndex<TX, TY>? Sorted { get; set; }

        public GridHitIndex<TX, TY>? Grid { get; set; }
    }
}
