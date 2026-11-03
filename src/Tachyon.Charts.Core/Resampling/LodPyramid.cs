using System.Numerics;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// Precomputed extremes in levels of growing blocks, which answer range queries without touching
/// most of the data.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks>
/// One thread calls <see cref="Update"/>, any thread may query at the same time. Only complete blocks
/// are stored, and a published block never changes, so the values must be append-only.
/// </remarks>
public sealed class LodPyramid<T>
    where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    /// <summary>
    /// The default number of blocks of one level that form a block of the next level.
    /// </summary>
    public const int DefaultFactor = 4;

    private const int BaseBlockSize = 16;
    private const int InitialBlocks = 64;

    private readonly int _factor;
    private volatile Level[] _levels = [new Level(InitialBlocks, 0)];

    /// <summary>
    /// Initializes a new instance of the <see cref="LodPyramid{T}"/> class.
    /// </summary>
    /// <param name="factor">The growth of the block size per level, at least 2.</param>
    /// <exception cref="ArgumentOutOfRangeException">The factor is out of range.</exception>
    public LodPyramid(int factor = DefaultFactor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 2);
        _factor = factor;
    }

    /// <summary>
    /// Gets the number of values covered by stored blocks.
    /// </summary>
    public long CoveredCount => (long)_levels[0].Count * BaseBlockSize;

    /// <summary>
    /// Gets the memory of the stored blocks in bytes.
    /// </summary>
    public long ByteCount
    {
        get
        {
            long blocks = 0;
            foreach (var level in _levels)
            {
                blocks += level.Count;
            }

            return blocks * 2 * System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        }
    }

    /// <summary>
    /// Adds the blocks that became complete. Only the values behind <see cref="CoveredCount"/> are read.
    /// </summary>
    /// <param name="values">All values, starting with those of earlier calls.</param>
    public void Update(ReadOnlySpan<T> values)
    {
        var target = values.Length / BaseBlockSize;
        var baseLevel = EnsureCapacity(0, target);
        for (var block = baseLevel.Count; block < target; block++)
        {
            var extremes = MinMax<T>.Empty;
            var min = extremes.Min;
            var max = extremes.Max;
            SimdMinMax.Accumulate(values.Slice(block * BaseBlockSize, BaseBlockSize), ref min, ref max);
            baseLevel.Min[block] = min;
            baseLevel.Max[block] = max;
        }

        baseLevel.Count = target;

        for (var index = 0; index < _levels.Length; index++)
        {
            var available = _levels[index].Count / _factor;
            if (available == 0)
            {
                break;
            }

            BuildNextLevel(index, available);
        }
    }

    /// <summary>
    /// Finds the extremes of a range.
    /// </summary>
    /// <param name="values">The values, at least those of the last update.</param>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes of the finite values.</returns>
    public MinMax<T> Query(ReadOnlySpan<T> values, int start, int end)
    {
        var extremes = MinMax<T>.Empty;
        var min = extremes.Min;
        var max = extremes.Max;

        var levels = _levels;
        var firstBlock = (start + BaseBlockSize - 1) / BaseBlockSize;
        var lastBlock = Math.Min(end / BaseBlockSize, levels[0].Count);

        if (firstBlock >= lastBlock)
        {
            SimdMinMax.Accumulate(values[start..end], ref min, ref max);
            return new MinMax<T>(min, max);
        }

        SimdMinMax.Accumulate(values[start..(firstBlock * BaseBlockSize)], ref min, ref max);
        SimdMinMax.Accumulate(values[(lastBlock * BaseBlockSize)..end], ref min, ref max);

        for (var index = 0; firstBlock < lastBlock; index++) // NOSONAR: index counts along the block range
        {
            var level = levels[index];
            var next = index + 1 < levels.Length ? levels[index + 1] : null;
            if (next is null || lastBlock / _factor > next.Count)
            {
                AccumulateBlocks(level, firstBlock, lastBlock, ref min, ref max);
                break;
            }

            var alignedStart = Math.Min(AlignUp(firstBlock), lastBlock);
            var alignedEnd = Math.Max(lastBlock - (lastBlock % _factor), alignedStart);
            AccumulateBlocks(level, firstBlock, alignedStart, ref min, ref max);
            AccumulateBlocks(level, alignedEnd, lastBlock, ref min, ref max);

            firstBlock = alignedStart / _factor;
            lastBlock = alignedEnd / _factor;
        }

        return new MinMax<T>(min, max);
    }

    private static void AccumulateBlocks(Level level, int first, int last, ref T min, ref T max)
    {
        for (var block = first; block < last; block++)
        {
            min = T.Min(min, level.Min[block]);
            max = T.Max(max, level.Max[block]);
        }
    }

    private int AlignUp(int block) => (block + _factor - 1) / _factor * _factor;

    private void BuildNextLevel(int index, int target)
    {
        var source = _levels[index];
        var level = EnsureCapacity(index + 1, target);
        for (var block = level.Count; block < target; block++)
        {
            var extremes = MinMax<T>.Empty;
            var min = extremes.Min;
            var max = extremes.Max;
            AccumulateBlocks(source, block * _factor, (block + 1) * _factor, ref min, ref max);
            level.Min[block] = min;
            level.Max[block] = max;
        }

        level.Count = target;
    }

    /// <summary>
    /// Returns a level with room for the blocks. Growing copies it, queries keep using the old copy.
    /// </summary>
    private Level EnsureCapacity(int index, int blocks)
    {
        var levels = _levels;
        if (index == levels.Length)
        {
            var extended = new Level[index + 1];
            levels.CopyTo(extended, 0);
            extended[index] = new Level(Math.Max(blocks, InitialBlocks), 0);
            _levels = extended;
            return extended[index];
        }

        var level = levels[index];
        if (blocks <= level.Min.Length)
        {
            return level;
        }

        var grown = new Level(Math.Max(blocks, level.Min.Length * 2), level.Count);
        level.Min.AsSpan(0, level.Count).CopyTo(grown.Min);
        level.Max.AsSpan(0, level.Count).CopyTo(grown.Max);

        var replaced = (Level[])levels.Clone();
        replaced[index] = grown;
        _levels = replaced;
        return grown;
    }

    private sealed class Level(int capacity, int count)
    {
        private volatile int _count = count;

        public T[] Min { get; } = new T[capacity];

        public T[] Max { get; } = new T[capacity];

        public int Count
        {
            get => _count;
            set => _count = value;
        }
    }
}
