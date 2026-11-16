using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// The extremes of a large column in levels of growing blocks: blocks of 256 values, then of four blocks each, up to
/// one. It answers the lowest and highest value of any range by reading at most two partial blocks of the column, so a
/// chart of billions of values draws each pixel column at once. It is built in the background on all cores and can be
/// kept beside the data, so that the next opening only reads it.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks>Members are safe to use from any thread. The values must not change while the pyramid lives.</remarks>
public sealed class ColumnPyramid<T> : IColumnPyramid<T>
    where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    /// <summary>The number of values of a block of the lowest level.</summary>
    public const int BlockSize = 256;

    /// <summary>The number of blocks of one level that form a block of the next.</summary>
    public const int Factor = 4;

    private const uint Magic = 0x52595054; // "TPYR"
    private const int FormatVersion = 1;
    private const int HeaderSize = 64;
    private const int ChunkBlocks = 1 << 14;

    private readonly IColumnSource<T> _source;
    private readonly long _blocks;
    private T[][] _min = [];
    private T[][] _max = [];
    private long _builtBlocks;
    private volatile bool _isComplete;

    // While building: the lowest level as far as read, which pieces of the column are done, and the extremes of each.
    private T[] _baseMin = [];
    private T[] _baseMax = [];
    private int[] _pieceDone = [];
    private T[] _pieceMin = [];
    private T[] _pieceMax = [];
    private int _builtPieces;

    private ColumnPyramid(IColumnSource<T> source)
    {
        _source = source;
        _blocks = (source.Count + BlockSize - 1) / BlockSize;
    }

    /// <summary>Gets the share of the column covered so far, from 0 to 1.</summary>
    public double Progress => _isComplete || _blocks == 0 ? 1 : (double)Interlocked.Read(ref _builtBlocks) / _blocks;

    /// <summary>Gets the number of pieces of <see cref="PieceSize"/> values read so far; it grows while the pyramid is built.</summary>
    public int BuiltPieces => Volatile.Read(ref _builtPieces);

    /// <summary>Gets the number of values the pyramid reads at once while it is built.</summary>
    public static long PieceSize => (long)ChunkBlocks * BlockSize;

    /// <summary>Gets a value indicating whether the pyramid is complete; before that, queries read the column.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Gets a value indicating whether the pyramid came from its file, without reading the column.</summary>
    public bool IsFromFile { get; private set; }

    /// <summary>Gets the task that completes with the pyramid.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the memory of the levels in bytes.</summary>
    public long ByteCount
    {
        get
        {
            long values = 0;
            foreach (var level in _min)
            {
                values += level.Length;
            }

            return values * 2 * Marshal.SizeOf<T>();
        }
    }

    /// <summary>Gets the extremes of the whole column, or empty ones before the pyramid is complete.</summary>
    public MinMax<T> Total => _isComplete && _min.Length > 0 ? new MinMax<T>(_min[^1][0], _max[^1][0]) : MinMax<T>.Empty;

    /// <summary>
    /// Starts building the pyramid of a column in the background, or reads it from its file when that belongs to the
    /// same values. A new pyramid is written to the file when it is complete.
    /// </summary>
    /// <param name="source">The column.</param>
    /// <param name="filePath">The file to keep it in, such as the path of the data with <c>.tpyr</c>; <see langword="null"/> to keep it in memory only.</param>
    /// <param name="cancellation">Stops the building.</param>
    /// <returns>The pyramid, complete once <see cref="Completion"/> is.</returns>
    public static ColumnPyramid<T> Build(IColumnSource<T> source, string? filePath = null, CancellationToken cancellation = default) =>
        Build(source, filePath, Environment.ProcessorCount, cancellation);

    /// <summary>Starts building with a number of workers, for tests that look at a pyramid half built.</summary>
    internal static ColumnPyramid<T> Build(IColumnSource<T> source, string? filePath, int workers, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        var pyramid = new ColumnPyramid<T>(source);
        pyramid.Completion = Task.Run(() => pyramid.Make(filePath, workers, cancellation), cancellation);
        return pyramid;
    }

    /// <summary>Finds the extremes of a range of the column, ignoring values that are not finite.</summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes; empty ones (min above max) for a range without finite values.</returns>
    public MinMax<T> Query(long start, long end)
    {
        start = Math.Max(0, start);
        end = Math.Min(end, _source.Count);
        var min = T.MaxValue;
        var max = T.MinValue;
        if (start >= end)
        {
            return new MinMax<T>(min, max);
        }

        if (!_isComplete)
        {
            ReadExtremes(start, end, ref min, ref max);
            return new MinMax<T>(min, max);
        }

        // Whole blocks come from the levels; the last block of the column is whole up to its end.
        var firstBlock = (start + BlockSize - 1) / BlockSize;
        var lastBlock = end == _source.Count ? _blocks : end / BlockSize;
        if (firstBlock >= lastBlock)
        {
            ReadExtremes(start, end, ref min, ref max);
            return new MinMax<T>(min, max);
        }

        ReadExtremes(start, firstBlock * BlockSize, ref min, ref max);
        ReadExtremes(Math.Min(lastBlock * BlockSize, end), end, ref min, ref max);
        AccumulateLevels(firstBlock, lastBlock, ref min, ref max);
        return new MinMax<T>(min, max);
    }

    // Whole blocks from the coarsest levels that fit, the blocks at both ends from the finer ones.
    private void AccumulateLevels(long firstBlock, long lastBlock, ref T min, ref T max)
    {
        var level = 0;
        while (firstBlock < lastBlock)
        {
            var length = _min[level].Length;
            if (level + 1 == _min.Length)
            {
                Accumulate(level, firstBlock, lastBlock, ref min, ref max);
                break;
            }

            // The blocks before the next aligned one and after the last aligned one stay on this level; the rest goes
            // up. A range reaching the end of the level takes the last, partial block of the next level.
            var alignedStart = Math.Min((firstBlock + Factor - 1) / Factor * Factor, lastBlock);
            Accumulate(level, firstBlock, alignedStart, ref min, ref max);
            var alignedEnd = lastBlock == length ? length : lastBlock - (lastBlock % Factor);
            if (alignedStart >= lastBlock || alignedEnd <= alignedStart)
            {
                // Nothing whole is left for the next level: the rest stays here.
                Accumulate(level, alignedStart, lastBlock, ref min, ref max);
                break;
            }

            if (alignedEnd < lastBlock)
            {
                Accumulate(level, alignedEnd, lastBlock, ref min, ref max);
            }

            firstBlock = alignedStart / Factor;
            lastBlock = alignedEnd == length ? (length + Factor - 1) / Factor : alignedEnd / Factor;
            level++;
        }
    }

    /// <summary>
    /// Finds the extremes of a range widened to whole blocks of <see cref="BlockSize"/> values, so that it reads no value
    /// of the column. For ranges of many blocks, such as the pixel columns of a wide view, the difference is less than a
    /// pixel, and no outlier is lost: at most it shows one pixel column further. Before the pyramid is complete it reads
    /// the exact range.
    /// </summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes; empty ones (min above max) for a range without finite values.</returns>
    public MinMax<T> QueryBlocks(long start, long end)
    {
        if (!_isComplete)
        {
            return Query(start, end);
        }

        start = Math.Max(0, start);
        end = Math.Min(end, _source.Count);
        return start >= end ? MinMax<T>.Empty : Query(start / BlockSize * BlockSize, Math.Min(_source.Count, (end + BlockSize - 1) / BlockSize * BlockSize));
    }

    /// <summary>
    /// Finds the extremes of a range from what is read so far: before the pyramid is complete, from the pieces of the
    /// column already done, spread over the whole column from the start; after it, as <see cref="QueryBlocks"/>. A
    /// chart draws a coarse picture with it at once and refines it while the pyramid grows; an outlier in a piece not
    /// yet read shows once it is.
    /// </summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes; empty ones (min above max) when nothing of the range is read yet.</returns>
    public MinMax<T> QueryAvailable(long start, long end)
    {
        if (_isComplete)
        {
            return QueryBlocks(start, end);
        }

        start = Math.Max(0, start);
        end = Math.Min(end, _source.Count);
        var min = T.MaxValue;
        var max = T.MinValue;
        var done = _pieceDone;
        if (start >= end || done.Length == 0)
        {
            return new MinMax<T>(min, max);
        }

        var size = PieceSize;
        for (var piece = start / size; piece <= (end - 1) / size; piece++)
        {
            if (Volatile.Read(ref done[piece]) == 0)
            {
                continue;
            }

            var (first, last) = (piece * size, Math.Min((piece + 1) * size, _source.Count));
            if (start <= first && last <= end)
            {
                (min, max) = (T.Min(min, _pieceMin[piece]), T.Max(max, _pieceMax[piece]));
                continue;
            }

            // Part of a piece: its blocks there, whole.
            for (long block = Math.Max(start, first) / BlockSize, lastBlock = (Math.Min(end, last) + BlockSize - 1) / BlockSize; block < lastBlock; block++)
            {
                (min, max) = (T.Min(min, _baseMin[block]), T.Max(max, _baseMax[block]));
            }
        }

        return new MinMax<T>(min, max);
    }

    // The order of the pieces: bit-reversed, so that the first pieces read lie spread over the whole column and a
    // coarse picture of all of it shows after a fraction of the time.
    internal static int[] SpreadOrder(int pieces)
    {
        var bits = 0;
        while ((1L << bits) < pieces)
        {
            bits++;
        }

        var order = new int[pieces];
        var next = 0;
        for (var i = 0L; i < (1L << bits) && next < pieces; i++)
        {
            var reversed = 0L;
            for (var b = 0; b < bits; b++)
            {
                reversed |= ((i >> b) & 1) << (bits - 1 - b);
            }

            if (reversed < pieces)
            {
                order[next++] = (int)reversed;
            }
        }

        return order;
    }

    private void Accumulate(int level, long first, long last, ref T min, ref T max)
    {
        var mins = _min[level];
        var maxs = _max[level];
        for (var i = first; i < last; i++)
        {
            if (mins[i] < min)
            {
                min = mins[i];
            }

            if (maxs[i] > max)
            {
                max = maxs[i];
            }
        }
    }

    private void ReadExtremes(long start, long end, ref T min, ref T max)
    {
        while (start < end)
        {
            var values = _source.Read(start, (int)Math.Min(end - start, int.MaxValue));
            if (values.IsEmpty)
            {
                return;
            }

            SimdMinMax.Accumulate(values, ref min, ref max);
            start += values.Length;
        }
    }

    // The extremes of every block of a piece, and of the piece; it counts as built afterwards.
    private void BuildPiece(long chunk, T[] baseMin, T[] baseMax, CancellationToken cancellation)
    {
        var last = Math.Min(_blocks, (chunk + 1) * ChunkBlocks);
        var (pieceMin, pieceMax) = (T.MaxValue, T.MinValue);
        for (var block = chunk * ChunkBlocks; block < last; block++)
        {
            if ((block & 1023) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            var min = T.MaxValue;
            var max = T.MinValue;
            ReadExtremes(block * BlockSize, Math.Min((block + 1) * BlockSize, _source.Count), ref min, ref max);
            baseMin[block] = min;
            baseMax[block] = max;
            (pieceMin, pieceMax) = (T.Min(pieceMin, min), T.Max(pieceMax, max));
        }

        (_pieceMin[chunk], _pieceMax[chunk]) = (pieceMin, pieceMax);
        Volatile.Write(ref _pieceDone[chunk], 1);
        Interlocked.Increment(ref _builtPieces);
        Interlocked.Add(ref _builtBlocks, last - (chunk * ChunkBlocks));
    }

    // The next level: the extremes of every group of blocks below.
    private static (T[] Min, T[] Max) Coarser(T[] lowerMin, T[] lowerMax)
    {
        var length = (lowerMin.Length + Factor - 1) / Factor;
        var (upperMin, upperMax) = (new T[length], new T[length]);
        for (var i = 0; i < length; i++)
        {
            var min = T.MaxValue;
            var max = T.MinValue;
            for (var j = i * Factor; j < Math.Min(lowerMin.Length, (i + 1) * Factor); j++)
            {
                min = T.Min(min, lowerMin[j]);
                max = T.Max(max, lowerMax[j]);
            }

            (upperMin[i], upperMax[i]) = (min, max);
        }

        return (upperMin, upperMax);
    }

    private void Make(string? filePath, int workers, CancellationToken cancellation)
    {
        if (filePath is not null && TryLoad(filePath))
        {
            IsFromFile = true;
            _isComplete = true;
            return;
        }

        var baseMin = new T[_blocks];
        var baseMax = new T[_blocks];
        var chunks = (int)((_blocks + ChunkBlocks - 1) / ChunkBlocks);
        (_baseMin, _baseMax, _pieceMin, _pieceMax) = (baseMin, baseMax, new T[chunks], new T[chunks]);
        _pieceDone = new int[chunks];
        var order = SpreadOrder(chunks);
        var next = -1;

        // Each worker takes the next piece in the spread order, so the pieces done stay spread at any moment.
        Parallel.For(0, Math.Min(workers, Math.Max(1, chunks)), new ParallelOptions { CancellationToken = cancellation }, _ =>
        {
            for (var taken = Interlocked.Increment(ref next); taken < chunks; taken = Interlocked.Increment(ref next))
            {
                cancellation.ThrowIfCancellationRequested();
                BuildPiece(order[taken], baseMin, baseMax, cancellation);
            }
        });

        var mins = new List<T[]> { baseMin };
        var maxs = new List<T[]> { baseMax };
        while (mins[^1].Length > 1)
        {
            var (upperMin, upperMax) = Coarser(mins[^1], maxs[^1]);
            mins.Add(upperMin);
            maxs.Add(upperMax);
        }

        (_min, _max) = ([.. mins], [.. maxs]);
        _isComplete = true;
        if (filePath is not null)
        {
            TrySave(filePath);
        }
    }

    // A fingerprint of the values: their count, X and the first and last values, so that a kept pyramid of other or
    // changed data is not taken.
    private ulong Fingerprint()
    {
        var hash = 14695981039346656037UL;
        void Add(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                hash = (hash ^ b) * 1099511628211UL;
            }
        }

        Span<byte> numbers = stackalloc byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(numbers, _source.Count);
        BinaryPrimitives.WriteDoubleLittleEndian(numbers[8..], _source.XStart);
        BinaryPrimitives.WriteDoubleLittleEndian(numbers[16..], _source.XStep);
        Add(numbers);
        foreach (var start in new[] { 0L, Math.Max(0, _source.Count - 4096) })
        {
            for (long at = start, end = Math.Min(_source.Count, start + 4096); at < end;)
            {
                var values = _source.Read(at, (int)(end - at));
                Add(MemoryMarshal.AsBytes(values));
                at += values.Length;
            }
        }

        return hash;
    }

    private bool TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[HeaderSize];
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != FormatVersion
                || BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != Marshal.SizeOf<T>() || BinaryPrimitives.ReadInt64LittleEndian(header[16..]) != _source.Count
                || BinaryPrimitives.ReadUInt64LittleEndian(header[24..]) != Fingerprint())
            {
                return false;
            }

            var levels = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
            var (mins, maxs) = (new T[levels][], new T[levels][]);
            var length = _blocks;
            for (var level = 0; level < levels; level++)
            {
                (mins[level], maxs[level]) = (new T[length], new T[length]);
                stream.ReadExactly(MemoryMarshal.AsBytes(mins[level].AsSpan()));
                stream.ReadExactly(MemoryMarshal.AsBytes(maxs[level].AsSpan()));
                length = (length + Factor - 1) / Factor;
            }

            (_min, _max) = (mins, maxs);
            Interlocked.Exchange(ref _builtBlocks, _blocks);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            // A damaged or unreadable file is built anew.
            return false;
        }
    }

    private void TrySave(string path)
    {
        try
        {
            var temporary = path + ".part";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                Span<byte> header = stackalloc byte[HeaderSize];
                header.Clear();
                BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
                BinaryPrimitives.WriteInt32LittleEndian(header[8..], Marshal.SizeOf<T>());
                BinaryPrimitives.WriteInt32LittleEndian(header[12..], _min.Length);
                BinaryPrimitives.WriteInt64LittleEndian(header[16..], _source.Count);
                BinaryPrimitives.WriteUInt64LittleEndian(header[24..], Fingerprint());
                stream.Write(header);
                for (var level = 0; level < _min.Length; level++)
                {
                    stream.Write(MemoryMarshal.AsBytes(_min[level].AsSpan()));
                    stream.Write(MemoryMarshal.AsBytes(_max[level].AsSpan()));
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without a place to keep it, the pyramid is built again next time.
        }
    }
}
