using System.Numerics;

namespace Tachyon.Charts.Core.HitTesting;

/// <summary>
/// How points are measured against a position, in pixels per data unit.
/// </summary>
/// <param name="X">The position on the X axis, relative to the origin of the search.</param>
/// <param name="Y">The position on the Y axis.</param>
/// <param name="ScaleX">The pixels per X unit. Its sign does not matter.</param>
/// <param name="ScaleY">The pixels per Y unit. Zero compares by X alone.</param>
/// <param name="MaximumDistance">The largest distance in pixels.</param>
internal readonly record struct HitQuery(double X, double Y, double ScaleX, double ScaleY, double MaximumDistance)
{
    public double Measure(double x, double y)
    {
        var dx = (x - X) * ScaleX;
        var dy = (y - Y) * ScaleY;
        return (dx * dx) + (dy * dy);
    }
}

/// <summary>
/// Finds the nearest point in data sorted by X. Blocks keep their Y extent on several levels, so that
/// the search skips everything that cannot beat the best point so far. The result is exact: it equals a
/// scan of all points, with the lowest index among equal distances.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
internal sealed class SortedHitIndex<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private const int BaseBlockSize = 256;
    private const int Factor = 16;

    private readonly List<(double[] Min, double[] Max, int Size)> _levels = [];
    private int _indexedCount;

    /// <summary>Gets the number of points in full blocks.</summary>
    public int IndexedCount => _indexedCount;

    /// <summary>Indexes the full blocks of a data set.</summary>
    /// <param name="y">The Y values.</param>
    public void Build(ReadOnlySpan<TY> y)
    {
        var blocks = y.Length / BaseBlockSize;
        var min = new double[blocks];
        var max = new double[blocks];
        for (var block = 0; block < blocks; block++)
        {
            var low = double.PositiveInfinity;
            var high = double.NegativeInfinity;
            foreach (var value in y.Slice(block * BaseBlockSize, BaseBlockSize))
            {
                var number = double.CreateTruncating(value);
                if (double.IsFinite(number))
                {
                    low = Math.Min(low, number);
                    high = Math.Max(high, number);
                }
            }

            min[block] = low;
            max[block] = high;
        }

        _levels.Clear();
        _levels.Add((min, max, BaseBlockSize));
        while (_levels[^1].Min.Length >= Factor)
        {
            var (lowerMin, lowerMax, size) = _levels[^1];
            var count = lowerMin.Length / Factor;
            var upperMin = new double[count];
            var upperMax = new double[count];
            for (var i = 0; i < count; i++)
            {
                var low = double.PositiveInfinity;
                var high = double.NegativeInfinity;
                for (var j = i * Factor; j < (i + 1) * Factor; j++)
                {
                    low = Math.Min(low, lowerMin[j]);
                    high = Math.Max(high, lowerMax[j]);
                }

                upperMin[i] = low;
                upperMax[i] = high;
            }

            _levels.Add((upperMin, upperMax, size * Factor));
        }

        _indexedCount = blocks * BaseBlockSize;
    }

    /// <summary>Finds the nearest point.</summary>
    /// <param name="x">The X values, not decreasing.</param>
    /// <param name="y">The Y values.</param>
    /// <param name="origin">Subtracted from X values.</param>
    /// <param name="query">The search.</param>
    /// <returns>The index, or -1 if no point is close enough.</returns>
    public int Find(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query)
    {
        var scaleX = Math.Abs(query.ScaleX);
        var reach = scaleX > 0 ? query.MaximumDistance / scaleX : double.PositiveInfinity;
        var first = LowerBound(x, origin, query.X - reach);
        var last = UpperBound(x, origin, query.X + reach);
        var state = new SearchState(query.MaximumDistance * query.MaximumDistance);

        // Everything behind the blocks is still unindexed.
        var indexedEnd = Math.Min(last, _indexedCount);
        if (first < indexedEnd && _levels.Count > 0)
        {
            var top = _levels.Count - 1;
            var (min, _, size) = _levels[top];
            var center = Math.Clamp(LowerBound(x, origin, query.X), first, indexedEnd - 1);
            var middle = center / size;
            var blocks = _indexedCount / size;
            SearchOutwards(x, y, origin, query, top, 0, Math.Max(min.Length, blocks), middle, first, indexedEnd, ref state);
            if (min.Length * size < _indexedCount)
            {
                ScanLowerLevels(x, y, origin, query, top, min.Length, first, indexedEnd, ref state);
            }
        }

        Scan(x, y, origin, query, Math.Max(first, _indexedCount), last, ref state);
        return state.Index;
    }

    private static int LowerBound(ReadOnlySpan<TX> x, TX origin, double value)
    {
        var low = 0;
        var high = x.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (double.CreateTruncating(x[middle] - origin) < value)
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

    private static int UpperBound(ReadOnlySpan<TX> x, TX origin, double value)
    {
        var low = 0;
        var high = x.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (double.CreateTruncating(x[middle] - origin) <= value)
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

    private static void Scan(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query, int start, int end, ref SearchState state)
    {
        for (var i = start; i < end; i++)
        {
            var valueY = double.CreateTruncating(y[i]);
            if (!double.IsFinite(valueY))
            {
                continue;
            }

            var valueX = double.CreateTruncating(x[i] - origin);
            if (!double.IsFinite(valueX))
            {
                continue;
            }

            state.Offer(i, query.Measure(valueX, valueY));
        }
    }

    // A block counts as far away as the nearest place it could hold a point.
    private static double LowerBoundOf(ReadOnlySpan<TX> x, TX origin, in HitQuery query, int start, int end, double min, double max)
    {
        var low = double.CreateTruncating(x[start] - origin);
        var high = double.CreateTruncating(x[end - 1] - origin);
        var dx = Math.Max(Math.Max(low - query.X, query.X - high), 0);
        var dy = Math.Max(Math.Max(min - query.Y, query.Y - max), 0);
        dx *= query.ScaleX;
        dy *= query.ScaleY;
        return (dx * dx) + (dy * dy);
    }

    private void SearchOutwards(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query, int level, int firstBlock, int endBlock, int middle, int first, int end, ref SearchState state)
    {
        middle = Math.Clamp(middle, firstBlock, endBlock - 1);
        for (var step = 0; step < endBlock - firstBlock; step++)
        {
            var left = middle - step;
            var right = middle + step;
            if (left < firstBlock && right >= endBlock)
            {
                return;
            }

            if (left >= firstBlock)
            {
                Visit(x, y, origin, query, level, left, first, end, ref state);
            }

            if (step > 0 && right < endBlock)
            {
                Visit(x, y, origin, query, level, right, first, end, ref state);
            }
        }
    }

    private void Visit(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query, int level, int block, int first, int end, ref SearchState state)
    {
        var (min, max, size) = _levels[level];
        if (block >= min.Length)
        {
            return;
        }

        var start = Math.Max(block * size, first);
        var stop = Math.Min((block + 1) * size, end);
        if (start >= stop || min[block] > max[block])
        {
            return;
        }

        // Equal distances must stay reachable: the lower index wins them.
        if (LowerBoundOf(x, origin, query, start, stop, min[block], max[block]) > state.BestDistance)
        {
            return;
        }

        if (level == 0)
        {
            Scan(x, y, origin, query, start, stop, ref state);
            return;
        }

        var firstChild = block * Factor;
        var middle = firstChild + Math.Clamp((Math.Max(first, start) - start) / (size / Factor), 0, Factor - 1);
        SearchOutwards(x, y, origin, query, level - 1, firstChild, firstChild + Factor, middle, first, end, ref state);
    }

    // Blocks that do not fill a block of the level above.
    private void ScanLowerLevels(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query, int top, int coveredBlocks, int first, int end, ref SearchState state)
    {
        var covered = coveredBlocks;
        for (var level = top - 1; level >= 0; level--)
        {
            covered *= Factor;
            var count = _levels[level].Min.Length;
            for (var block = covered; block < count; block++)
            {
                Visit(x, y, origin, query, level, block, first, end, ref state);
            }

            covered = count;
        }
    }

    private struct SearchState(double limit)
    {
        public double BestDistance { get; private set; } = limit;

        public int Index { get; private set; } = -1;

        public void Offer(int index, double distance)
        {
            if (distance < BestDistance || (distance == BestDistance && (Index < 0 || index < Index)))
            {
                BestDistance = distance;
                Index = index;
            }
        }
    }
}

/// <summary>
/// Finds the nearest point in unsorted data with a uniform grid. Points appended after building are
/// scanned until the next build. The result equals a scan of all points.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
internal sealed class GridHitIndex<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private const int PointsPerCell = 8;
    private const int MaximumCellsPerSide = 4096;

    private int[] _starts = [];
    private int[] _indices = [];
    private double _minX;
    private double _minY;
    private double _cellWidth = 1;
    private double _cellHeight = 1;
    private int _side;

    /// <summary>Gets the number of points in the grid.</summary>
    public int IndexedCount { get; private set; }

    /// <summary>Sorts a data set into the grid.</summary>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values.</param>
    /// <param name="origin">Subtracted from X values.</param>
    public void Build(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin)
    {
        var valid = MeasureBounds(x, y, origin, out var minX, out var maxX, out var minY, out var maxY);
        _side = Math.Clamp((int)Math.Sqrt(valid / (double)PointsPerCell), 1, MaximumCellsPerSide);
        _minX = valid > 0 ? minX : 0;
        _minY = valid > 0 ? minY : 0;
        _cellWidth = valid > 0 && maxX > minX ? (maxX - minX) / _side : 1;
        _cellHeight = valid > 0 && maxY > minY ? (maxY - minY) / _side : 1;

        var cells = _side * _side;
        var starts = new int[cells + 1];
        for (var i = 0; i < x.Length; i++)
        {
            if (TryGetCell(x[i], y[i], origin, out var cell))
            {
                starts[cell + 1]++;
            }
        }

        for (var i = 0; i < cells; i++)
        {
            starts[i + 1] += starts[i];
        }

        var indices = new int[valid];
        var next = new int[cells];
        Array.Copy(starts, next, cells);
        for (var i = 0; i < x.Length; i++)
        {
            if (TryGetCell(x[i], y[i], origin, out var cell))
            {
                indices[next[cell]++] = i;
            }
        }

        _starts = starts;
        _indices = indices;
        IndexedCount = x.Length;
    }

    private static int MeasureBounds(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, out double minX, out double maxX, out double minY, out double maxY)
    {
        minX = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        minY = double.PositiveInfinity;
        maxY = double.NegativeInfinity;
        var valid = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var valueX = double.CreateTruncating(x[i] - origin);
            var valueY = double.CreateTruncating(y[i]);
            if (double.IsFinite(valueX) && double.IsFinite(valueY))
            {
                minX = Math.Min(minX, valueX);
                maxX = Math.Max(maxX, valueX);
                minY = Math.Min(minY, valueY);
                maxY = Math.Max(maxY, valueY);
                valid++;
            }
        }

        return valid;
    }

    /// <summary>Finds the nearest point.</summary>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values.</param>
    /// <param name="origin">Subtracted from X values.</param>
    /// <param name="query">The search.</param>
    /// <returns>The index, or -1 if no point is close enough.</returns>
    public int Find(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query)
    {
        var best = query.MaximumDistance * query.MaximumDistance;
        var index = -1;
        if (_side > 0)
        {
            var scaleX = Math.Abs(query.ScaleX);
            var scaleY = Math.Abs(query.ScaleY);
            var reachX = scaleX > 0 ? query.MaximumDistance / scaleX : double.PositiveInfinity;
            var reachY = scaleY > 0 ? query.MaximumDistance / scaleY : double.PositiveInfinity;
            var firstColumn = ToCell(query.X - reachX, _minX, _cellWidth);
            var lastColumn = ToCell(query.X + reachX, _minX, _cellWidth);
            var firstRow = ToCell(query.Y - reachY, _minY, _cellHeight);
            var lastRow = ToCell(query.Y + reachY, _minY, _cellHeight);
            for (var row = firstRow; row <= lastRow; row++)
            {
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var cell = (row * _side) + column;
                    for (var i = _starts[cell]; i < _starts[cell + 1]; i++)
                    {
                        Offer(x, y, origin, query, _indices[i], ref best, ref index);
                    }
                }
            }
        }

        for (var i = IndexedCount; i < x.Length; i++)
        {
            Offer(x, y, origin, query, i, ref best, ref index);
        }

        return index;
    }

    private static void Offer(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in HitQuery query, int i, ref double best, ref int index)
    {
        var valueX = double.CreateTruncating(x[i] - origin);
        var valueY = double.CreateTruncating(y[i]);
        if (!double.IsFinite(valueX) || !double.IsFinite(valueY))
        {
            return;
        }

        var distance = query.Measure(valueX, valueY);
        if (distance < best || (distance == best && (index < 0 || i < index)))
        {
            best = distance;
            index = i;
        }
    }

    private int ToCell(double value, double minimum, double size)
    {
        if (double.IsNegativeInfinity(value))
        {
            return 0;
        }

        return double.IsPositiveInfinity(value) ? _side - 1 : (int)Math.Clamp(Math.Floor((value - minimum) / size), 0, _side - 1);
    }

    private bool TryGetCell(TX x, TY y, TX origin, out int cell)
    {
        var valueX = double.CreateTruncating(x - origin);
        var valueY = double.CreateTruncating(y);
        cell = 0;
        if (!double.IsFinite(valueX) || !double.IsFinite(valueY))
        {
            return false;
        }

        cell = (ToCell(valueY, _minY, _cellHeight) * _side) + ToCell(valueX, _minX, _cellWidth);
        return true;
    }
}
