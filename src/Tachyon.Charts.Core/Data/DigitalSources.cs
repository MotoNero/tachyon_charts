using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// A signal of two levels, such as a logic channel, a switch or a bus line, at an even X step. It answers two questions
/// fast, whatever its length: the level at a place, and the next place where the level changes.
/// </summary>
public interface IDigitalSource
{
    /// <summary>Gets the number of points.</summary>
    long Count { get; }

    /// <summary>Gets the X of the first point.</summary>
    double XStart { get; }

    /// <summary>Gets the X step between two points.</summary>
    double XStep { get; }

    /// <summary>Gets a number that changes whenever the data change.</summary>
    long Version { get; }

    /// <summary>Gets the level at a point.</summary>
    /// <param name="index">The point, from 0 to <see cref="Count"/> − 1.</param>
    /// <returns><see langword="true"/> for the high level.</returns>
    bool LevelAt(long index);

    /// <summary>Finds the first point of a range whose level differs from the point before it.</summary>
    /// <param name="from">The first point to look at; a point 0 has no point before it and never counts.</param>
    /// <param name="to">The point behind the last one to look at.</param>
    /// <returns>The point, or −1 when the level stays the same over the range.</returns>
    long NextChange(long from, long to);
}

/// <summary>
/// A digital signal kept as the places where its level changes: a billion points with a thousand changes take a few
/// kilobytes. Writable from one thread while others read, without locks for the readers; points come one by one, in runs
/// of one level, or as packed bits.
/// </summary>
/// <param name="xStart">The X of the first point.</param>
/// <param name="xStep">The X step between two points.</param>
public sealed class DigitalTransitions(double xStart = 0, double xStep = 1) : IDigitalSource
{
    private readonly Lock _gate = new();
    private long[] _changes = new long[1024];
    private int _changeCount;
    private long _count;
    private long _version;
    private bool _initial;
    private bool _level;

    /// <inheritdoc />
    public long Count => Volatile.Read(ref _count);

    /// <inheritdoc />
    public double XStart { get; } = xStart;

    /// <inheritdoc />
    public double XStep { get; } = xStep;

    /// <inheritdoc />
    public long Version => Volatile.Read(ref _version);

    /// <summary>Gets the number of changes of the level.</summary>
    public int ChangeCount => Volatile.Read(ref _changeCount);

    /// <summary>Appends points of one level.</summary>
    /// <param name="level">The level, <see langword="true"/> for high.</param>
    /// <param name="points">How many points.</param>
    public void Append(bool level, long points = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        if (points == 0)
        {
            return;
        }

        lock (_gate)
        {
            AppendRun(level, points);
            Volatile.Write(ref _version, _version + 1);
        }
    }

    /// <summary>Appends points as packed bits: bit 0 of the first word is the first point, a set bit the high level.</summary>
    /// <param name="words">The bits.</param>
    /// <param name="points">How many bits of them are points.</param>
    public void AppendBits(ReadOnlySpan<ulong> words, long points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(points, words.Length * 64L);
        if (points == 0)
        {
            return;
        }

        lock (_gate)
        {
            var start = _count;
            if (start == 0)
            {
                (_initial, _level) = ((words[0] & 1) != 0, (words[0] & 1) != 0);
            }

            var previous = _level ? 1UL : 0UL;
            var wordCount = (int)((points + 63) / 64);
            for (var w = 0; w < wordCount; w++)
            {
                var word = words[w];

                // A set bit where a point differs from the one before it.
                var changes = word ^ ((word << 1) | previous);
                var valid = points - (w * 64L);
                if (valid < 64)
                {
                    changes &= (1UL << (int)valid) - 1;
                }

                while (changes != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(changes);
                    AddChange(start + (w * 64L) + bit);
                    changes &= changes - 1;
                }

                previous = word >> 63;
            }

            var last = points - 1;
            _level = ((words[(int)(last / 64)] >> (int)(last % 64)) & 1) != 0;
            Volatile.Write(ref _count, start + points);
            Volatile.Write(ref _version, _version + 1);
        }
    }

    /// <inheritdoc />
    public bool LevelAt(long index)
    {
        var (changes, count) = Snapshot();
        return _initial ^ ((UpperBound(changes, count, index) & 1) != 0);
    }

    /// <inheritdoc />
    public long NextChange(long from, long to)
    {
        var (changes, count) = Snapshot();
        var at = LowerBound(changes, count, Math.Max(from, 1));
        return at < count && changes[at] < to ? changes[at] : -1;
    }

    /// <summary>
    /// Describes pixel columns of a range in one pass: for each, the level at its start, whether the level changes inside
    /// it, and the level at its end. A cursor runs through the changes, so a column costs a short search from the one
    /// before, not a search through all changes.
    /// </summary>
    /// <param name="first">The first point.</param>
    /// <param name="last">The point behind the last one.</param>
    /// <param name="states">Receives per column: bit 0 the level at the start, bit 1 a change inside, bit 2 the level at the end.</param>
    internal void DescribeColumns(long first, long last, Span<byte> states)
    {
        var (changes, count) = Snapshot();
        var span = last - first;
        var columns = states.Length;

        // Changes at or before the start of the column.
        var before = UpperBound(changes, count, first);
        for (var column = 0; column < columns; column++)
        {
            var start = first + (span * column / columns);
            var end = first + (span * (column + 1) / columns);
            if (column > 0)
            {
                before = Gallop(changes, count, before, start);
            }

            var startLevel = _initial ^ ((before & 1) != 0);
            if (end <= start)
            {
                states[column] = (byte)(startLevel ? 0b101 : 0);
                continue;
            }

            var atEnd = Gallop(changes, count, before, end - 1);
            var endLevel = _initial ^ ((atEnd & 1) != 0);
            states[column] = (byte)((startLevel ? 1 : 0) | (atEnd > before ? 2 : 0) | (endLevel ? 4 : 0));
            before = atEnd;
        }
    }

    // The number of changes at or before a point, searched forward from a number known to be at most that.
    private static int Gallop(long[] changes, int count, int from, long index)
    {
        var step = 1;
        var high = from;
        while (high < count && changes[high] <= index)
        {
            from = high + 1;
            high = (int)Math.Min(count, (long)high + step);
            step <<= 1;
        }

        // The answer lies in [from, high]: changes before from are at or before the point, the one at high is behind it.
        while (from < high)
        {
            var middle = (int)(((uint)from + (uint)high) >> 1);
            if (changes[middle] <= index)
            {
                from = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return from;
    }

    // The number of changes first, then the array: an array taken after the number holds at least that many.
    private (long[] Changes, int Count) Snapshot()
    {
        var count = Volatile.Read(ref _changeCount);
        return (Volatile.Read(ref _changes), count);
    }

    // The number of changes at or before a point.
    private static int UpperBound(long[] changes, int count, long index)
    {
        var (low, high) = (0, count);
        while (low < high)
        {
            var middle = (int)(((uint)low + (uint)high) >> 1);
            if (changes[middle] <= index)
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

    // The first change at or after a point.
    private static int LowerBound(long[] changes, int count, long index)
    {
        var (low, high) = (0, count);
        while (low < high)
        {
            var middle = (int)(((uint)low + (uint)high) >> 1);
            if (changes[middle] < index)
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

    private void AppendRun(bool level, long points)
    {
        if (_count == 0)
        {
            (_initial, _level) = (level, level);
        }
        else if (level != _level)
        {
            AddChange(_count);
            _level = level;
        }

        Volatile.Write(ref _count, _count + points);
    }

    private void AddChange(long index)
    {
        var count = _changeCount;
        if (count == _changes.Length)
        {
            if (count == Array.MaxLength)
            {
                throw new InvalidOperationException($"A signal holds at most {Array.MaxLength:N0} changes; keep longer ones as packed bits.");
            }

            var larger = new long[(int)Math.Min((long)count * 2, Array.MaxLength)];
            Array.Copy(_changes, larger, count);
            Volatile.Write(ref _changes, larger);
        }

        _changes[count] = index;
        Volatile.Write(ref _changeCount, count + 1);
    }
}

/// <summary>
/// A digital signal of one bit per point, in memory or in a column file of the type <see cref="ColumnType.Bit"/>: a
/// hundred billion points take 12.5 GB. An index of one bit per 4096 points tells where the level changes; it is built
/// on all cores in the background, a thousandth of a bit per point. Until it is complete, narrow views are exact and wide
/// views show what is indexed so far.
/// </summary>
public sealed class PackedBits : IDigitalSource
{
    /// <summary>The points of a block of the index.</summary>
    public const int BlockSize = 4096;

    // A piece of the building: 64 blocks, so that each piece owns whole words of the index.
    private const int PieceBlocks = 64 * 64;

    private readonly WordReader _words;
    private readonly ulong[] _index;
    private readonly int[] _builtPieces;
    private readonly long _blocks;
    private long _builtBlocks;
    private int _pieceCount;
    private volatile bool _isComplete;

    private PackedBits(WordReader words, long count, double xStart, double xStep)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        (_words, Count, XStart, XStep) = (words, count, xStart, xStep);
        _blocks = (count + BlockSize - 1) / BlockSize;
        _index = new ulong[(_blocks + 63) / 64];
        _builtPieces = new int[(int)((_blocks + PieceBlocks - 1) / PieceBlocks)];
    }

    /// <inheritdoc />
    public long Count { get; }

    /// <inheritdoc />
    public double XStart { get; }

    /// <inheritdoc />
    public double XStep { get; }

    /// <inheritdoc />
    /// <remarks>Grows while the index is built, so that a chart draws the finer picture.</remarks>
    public long Version => _isComplete ? 1L << 40 : Volatile.Read(ref _pieceCount);

    /// <summary>Gets how far the index is built, from 0 to 1.</summary>
    public double Progress => _blocks == 0 ? 1 : (double)Interlocked.Read(ref _builtBlocks) / _blocks;

    /// <summary>Gets a value indicating whether the index is complete.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Gets the building of the index.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Starts a signal over bits in memory: bit 0 of the first word is the first point.</summary>
    /// <param name="words">The bits; they must not change while the signal is in use.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="xStart">The X of the first point.</param>
    /// <param name="xStep">The X step between two points.</param>
    /// <param name="cancellation">Stops building the index.</param>
    /// <returns>The signal; its index is complete once <see cref="Completion"/> is.</returns>
    public static PackedBits FromWords(ReadOnlyMemory<ulong> words, long count, double xStart = 0, double xStep = 1, CancellationToken cancellation = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, words.Length * 64L);
        return Start(new PackedBits(new MemoryWords(words), count, xStart, xStep), cancellation);
    }

    /// <summary>Starts a signal over a column file of bits; the file stays open as long as the signal is in use.</summary>
    /// <param name="file">The file, of the type <see cref="ColumnType.Bit"/>.</param>
    /// <param name="cancellation">Stops building the index.</param>
    /// <returns>The signal; its index is complete once <see cref="Completion"/> is.</returns>
    public static PackedBits Open(ColumnFile file, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Type != ColumnType.Bit)
        {
            throw new InvalidOperationException($"The file holds {file.Type} values, not bits.");
        }

        return Start(new PackedBits(new FileWords(file), file.RowCount, file.XStart, file.XStep), cancellation);
    }

    /// <inheritdoc />
    public bool LevelAt(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return ((Word(index >> 6) >> (int)(index & 63)) & 1) != 0;
    }

    /// <inheritdoc />
    public long NextChange(long from, long to) => Find(from, to, exact: true);

    /// <summary>Finds the next change in what the index knows so far; parts not yet indexed count as without change.</summary>
    /// <param name="from">The first point to look at.</param>
    /// <param name="to">The point behind the last one.</param>
    /// <returns>The point, or −1.</returns>
    internal long NextIndexedChange(long from, long to) => Find(from, to, exact: false);

    private static PackedBits Start(PackedBits bits, CancellationToken cancellation)
    {
        bits.Completion = Task.Run(() => bits.Build(cancellation), cancellation);
        return bits;
    }

    private void Build(CancellationToken cancellation)
    {
        Parallel.For(0, _builtPieces.Length, new ParallelOptions { CancellationToken = cancellation }, piece =>
        {
            var first = (long)piece * PieceBlocks;
            var end = Math.Min(_blocks, first + PieceBlocks);
            for (var block = first; block < end; block++)
            {
                var start = block * BlockSize;
                if (ScanRaw(start == 0 ? 1 : start, Math.Min(Count, start + BlockSize)) >= 0)
                {
                    // A piece owns whole words of the index, so no other piece writes this one.
                    _index[block >> 6] |= 1UL << (int)(block & 63);
                }
            }

            Volatile.Write(ref _builtPieces[piece], 1);
            Interlocked.Add(ref _builtBlocks, end - first);
            Interlocked.Increment(ref _pieceCount);
        });
        _isComplete = true;
    }

    private long Find(long from, long to, bool exact)
    {
        from = Math.Max(from, 1);
        to = Math.Min(to, Count);
        if (from >= to)
        {
            return -1;
        }

        // The rest of the first block point by point, then the blocks the index marks.
        var block = from / BlockSize;
        var change = ScanRaw(from, Math.Min(to, (block + 1) * BlockSize));
        if (change >= 0)
        {
            return change;
        }

        var lastBlock = (to - 1) / BlockSize;
        var next = block + 1;
        while (next <= lastBlock)
        {
            // While the index grows, a piece not yet built is read point by point, or passed over for a quick look.
            var limit = _isComplete ? lastBlock : Math.Min(lastBlock, (((next / PieceBlocks) + 1) * PieceBlocks) - 1);
            if (!_isComplete && Volatile.Read(ref _builtPieces[next / PieceBlocks]) == 0)
            {
                change = exact ? ScanRaw(next * BlockSize, Math.Min(to, (limit + 1) * BlockSize)) : -1;
                next = limit + 1;
            }
            else
            {
                (change, next) = ScanMarked(next, limit, to);
            }

            if (change >= 0)
            {
                return change;
            }
        }

        return -1;
    }

    // The next block the index marks up to the limit, read point by point; and where to go on.
    private (long Change, long Next) ScanMarked(long next, long limit, long to)
    {
        var marked = NextMarked(next, limit);
        if (marked < 0)
        {
            return (-1, limit + 1);
        }

        return (ScanRaw(marked * BlockSize, Math.Min(to, (marked + 1) * BlockSize)), marked + 1);
    }

    // The first block from one to a limit that the index marks, or −1.
    private long NextMarked(long from, long limit)
    {
        for (var word = from >> 6; word <= limit >> 6; word++)
        {
            var bits = Volatile.Read(ref _index[word]);
            if (word == from >> 6)
            {
                bits &= ulong.MaxValue << (int)(from & 63);
            }

            if (bits != 0)
            {
                var marked = (word << 6) + BitOperations.TrailingZeroCount(bits);
                return marked <= limit ? marked : -1;
            }
        }

        return -1;
    }

    // The first point of a range that differs from the point before it, read word by word.
    private long ScanRaw(long from, long to)
    {
        if (from >= to)
        {
            return -1;
        }

        var word = from >> 6;
        // The bit before the first word; the first point has none and compares with itself.
        var previous = word == 0 ? Word(0) & 1 : Word(word - 1) >> 63;
        var lastWord = (to - 1) >> 6;
        while (word <= lastWord)
        {
            var span = _words.Read(word, (int)Math.Min(lastWord - word + 1, 1 << 16));
            for (var i = 0; i < span.Length; i++, word++)
            {
                var value = span[i];
                var changes = value ^ ((value << 1) | previous);
                previous = value >> 63;
                var low = word * 64;
                if (from > low)
                {
                    changes &= ulong.MaxValue << (int)(from - low);
                }

                if (to < low + 64)
                {
                    changes &= (1UL << (int)(to - low)) - 1;
                }

                if (changes != 0)
                {
                    return low + BitOperations.TrailingZeroCount(changes);
                }
            }
        }

        return -1;
    }

    private ulong Word(long index) => _words.Read(index, 1)[0];

    // Where the words come from: memory or a mapped file.
    private abstract class WordReader
    {
        public abstract ReadOnlySpan<ulong> Read(long first, int maxLength);
    }

    private sealed class MemoryWords(ReadOnlyMemory<ulong> words) : WordReader
    {
        public override ReadOnlySpan<ulong> Read(long first, int maxLength) => words.Span.Slice((int)first, (int)Math.Min(maxLength, words.Length - first));
    }

    private sealed class FileWords(ColumnFile file) : WordReader
    {
        public override ReadOnlySpan<ulong> Read(long first, int maxLength) => file.ReadWords(first, maxLength);
    }
}

/// <summary>
/// Writes a column file of bits, one per point, as the data come and without holding them; <see cref="PackedBits.Open"/>
/// reads it. The file is complete when the writer is disposed.
/// </summary>
public sealed class BitFileWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly ulong[] _buffer = new ulong[8192];
    private readonly double _xStart;
    private readonly double _xStep;
    private int _buffered;
    private ulong _word;
    private int _bits;
    private bool _disposed;

    /// <summary>Starts a new file, replacing one of the same name.</summary>
    /// <param name="path">The path.</param>
    /// <param name="xStart">The X of the first point.</param>
    /// <param name="xStep">The X step between two points.</param>
    public BitFileWriter(string path, double xStart = 0, double xStep = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        (_xStart, _xStep) = (xStart, xStep);
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 22);
        _stream.Write(new byte[ColumnFile.HeaderSize]);
    }

    /// <summary>Gets the number of points written.</summary>
    public long RowCount { get; private set; }

    /// <summary>Appends points of one level.</summary>
    /// <param name="level">The level.</param>
    /// <param name="points">How many points.</param>
    public void Append(bool level, long points = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        var value = level ? ulong.MaxValue : 0;
        while (points > 0)
        {
            if (_bits == 0 && points >= 64)
            {
                var words = points / 64;
                for (var i = 0L; i < words; i++)
                {
                    Push(value);
                }

                RowCount += words * 64;
                points -= words * 64;
                continue;
            }

            var take = (int)Math.Min(points, 64);
            AddBits(value, take);
            points -= take;
        }
    }

    /// <summary>Appends packed bits: bit 0 of the first word is the next point.</summary>
    /// <param name="words">The bits.</param>
    /// <param name="points">How many bits of them are points.</param>
    public void AppendBits(ReadOnlySpan<ulong> words, long points)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(points, words.Length * 64L);
        var w = 0;
        while (points > 0)
        {
            var take = (int)Math.Min(points, 64);
            AddBits(words[w++], take);
            points -= take;
        }
    }

    /// <summary>Writes the last word and the header and closes the file.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_bits > 0)
        {
            _buffer[_buffered++] = _word;
        }

        Flush();
        Span<byte> header = stackalloc byte[ColumnFile.HeaderSize];
        ColumnFile.WriteHeader(header, ColumnType.Bit, 1, RowCount, _xStart, _xStep, 0);
        _stream.Position = 0;
        _stream.Write(header);
        _stream.Dispose();
    }

    private void AddBits(ulong value, int count)
    {
        if (count < 64)
        {
            value &= (1UL << count) - 1;
        }

        _word |= value << _bits;
        var free = 64 - _bits;
        if (count >= free)
        {
            Push(_word);
            _word = count == free ? 0 : value >> free;
            _bits = count - free;
        }
        else
        {
            _bits += count;
        }

        RowCount += count;
    }

    private void Push(ulong word)
    {
        _buffer[_buffered++] = word;
        if (_buffered == _buffer.Length)
        {
            Flush();
        }
    }

    private void Flush()
    {
        _stream.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_buffer.AsSpan(0, _buffered)));
        _buffered = 0;
    }
}
