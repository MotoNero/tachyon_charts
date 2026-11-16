using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using Tachyon.Charts.Core.Resampling;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Records live values into column files on the disk, for hours: one file after another, each a column file that opens
/// on its own later. With a limit of files the oldest leave as new ones come, a ring on the disk; without one the record
/// grows. The extremes of every block of 256 values and of ever larger blocks grow with the values, so a line over the
/// record draws its whole span at once, also looking back over hours.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks>One thread writes, any thread reads.</remarks>
public sealed class ColumnRecorder<T> : IColumnSource<T>, IColumnPyramid<T>, IDisposable
    where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    /// <summary>The values of a file unless given: 64 million.</summary>
    public const long DefaultFileValues = 1L << 26;

    private readonly string _folder;
    private readonly string _name;
    private readonly double _xStart;
    private readonly double _xStep;
    private readonly int _maxFiles;
    private readonly long _fileValues;
    private readonly Lock _gate = new();
    private readonly List<Segment> _segments = [];
    private readonly Queue<Segment> _retired = new();
    private Segment[] _live = [];
    private long _first;
    private long _end;
    private int _nextFile;
    private bool _disposed;

    /// <summary>Starts a record in a folder.</summary>
    /// <param name="folder">The folder of the files.</param>
    /// <param name="name">The start of the names of the files, followed by their number.</param>
    /// <param name="xStart">The X of the first value.</param>
    /// <param name="xStep">The X step between two values.</param>
    /// <param name="maxFiles">The most files kept, the oldest leaving first; 0 to keep all.</param>
    /// <param name="fileValues">The values of each file.</param>
    public ColumnRecorder(string folder, string name, double xStart = 0, double xStep = 1, int maxFiles = 0, long fileValues = DefaultFileValues)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileValues, ColumnPyramid<T>.BlockSize);
        Directory.CreateDirectory(folder);
        (_folder, _name, _xStart, _xStep, _maxFiles, _fileValues) = (folder, name, xStart, xStep, maxFiles, fileValues);
    }

    /// <inheritdoc />
    public long Count => Volatile.Read(ref _end) - Volatile.Read(ref _first);

    /// <inheritdoc />
    /// <remarks>Moves on as the ring lets the oldest values go.</remarks>
    public double XStart => _xStart + (Volatile.Read(ref _first) * _xStep);

    /// <inheritdoc />
    public double XStep => _xStep;

    /// <inheritdoc />
    public long Version => Volatile.Read(ref _end);

    /// <summary>Gets the number of values recorded since the start, those the ring let go included.</summary>
    public long Recorded => Volatile.Read(ref _end);

    /// <summary>Gets the number of files on the disk.</summary>
    public int FileCount => Volatile.Read(ref _live).Length;

    /// <inheritdoc />
    public bool IsComplete => true;

    /// <inheritdoc />
    public double Progress => 1;

    /// <inheritdoc />
    public int BuiltPieces => 0;

    /// <inheritdoc />
    public MinMax<T> Total
    {
        get
        {
            var total = MinMax<T>.Empty;
            foreach (var segment in Volatile.Read(ref _live))
            {
                total = Combine(total, segment.Total);
            }

            return total;
        }
    }

    /// <summary>Appends values; the files follow one another as each fills.</summary>
    /// <param name="values">The values.</param>
    public void Append(ReadOnlySpan<T> values)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (!values.IsEmpty)
            {
                var current = _segments.Count > 0 && !_segments[^1].IsFull ? _segments[^1] : Open();
                var taken = current.Append(values);
                values = values[taken..];
                Volatile.Write(ref _end, _end + taken);
                if (current.IsFull)
                {
                    current.Finish();
                }
            }
        }
    }

    /// <summary>Appends a value.</summary>
    /// <param name="value">The value.</param>
    public void Append(T value) => Append(new ReadOnlySpan<T>(in value));

    /// <inheritdoc />
    public ReadOnlySpan<T> Read(long start, int maxLength)
    {
        var absolute = Volatile.Read(ref _first) + start;
        foreach (var segment in Volatile.Read(ref _live))
        {
            if (absolute >= segment.Start && absolute < segment.Start + segment.Count)
            {
                return segment.Read(absolute - segment.Start, maxLength);
            }
        }

        return [];
    }

    /// <inheritdoc />
    public MinMax<T> Query(long start, long end) => Extremes(start, end, blocksOnly: false);

    /// <inheritdoc />
    public MinMax<T> QueryBlocks(long start, long end) => Extremes(start, end, blocksOnly: true);

    /// <inheritdoc />
    public MinMax<T> QueryAvailable(long start, long end) => Query(start, end);

    /// <summary>Writes the number of values into the last file and closes all; the files stay on the disk.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var segment in _segments)
            {
                segment.Finish();
                segment.Dispose();
            }

            while (_retired.TryDequeue(out var retired))
            {
                retired.Dispose();
                retired.Delete();
            }

            Volatile.Write(ref _live, []);
        }
    }

    private static MinMax<T> Combine(MinMax<T> a, MinMax<T> b) => new(T.Min(a.Min, b.Min), T.Max(a.Max, b.Max));

    // A new file; in a ring the oldest leaves. Its mapping closes two files later, so that no reader holds it then.
    private Segment Open()
    {
        var path = Path.Combine(_folder, string.Create(CultureInfo.InvariantCulture, $"{_name}-{_nextFile++:D6}.tcol"));
        var segment = new Segment(path, _end, _fileValues, _xStart + (_end * _xStep), _xStep);
        _segments.Add(segment);
        if (_maxFiles > 0 && _segments.Count > _maxFiles)
        {
            var oldest = _segments[0];
            _segments.RemoveAt(0);
            _retired.Enqueue(oldest);
            Volatile.Write(ref _first, _segments[0].Start);
        }

        Volatile.Write(ref _live, [.. _segments]);
        while (_retired.Count > 2)
        {
            var retired = _retired.Dequeue();
            retired.Dispose();
            retired.Delete();
        }

        return segment;
    }

    private MinMax<T> Extremes(long start, long end, bool blocksOnly)
    {
        var first = Volatile.Read(ref _first);
        var (from, to) = (first + Math.Max(0, start), first + end);
        var result = MinMax<T>.Empty;
        foreach (var segment in Volatile.Read(ref _live))
        {
            var (low, high) = (Math.Max(from, segment.Start), Math.Min(to, segment.Start + segment.Count));
            if (low < high)
            {
                result = Combine(result, segment.Query(low - segment.Start, high - segment.Start, blocksOnly));
            }
        }

        return result;
    }

    // A file of the record: its values mapped into memory, and the extremes of its blocks level by level.
    private sealed unsafe class Segment : IDisposable
    {
        private const int Block = ColumnPyramid<T>.BlockSize;
        private const int Factor = 4;

        private readonly string _path;
        private readonly long _capacity;
        private readonly double _xStart;
        private readonly double _xStep;
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;

        // The values are written and read in place; the pointer lives as long as the view.
        private readonly byte* _base;
        private readonly T* _values;
        private readonly T[][] _min;
        private readonly T[][] _max;
        private long _count;
        private long _blocks;
        private MinMax<T> _total = MinMax<T>.Empty;
        private int _disposed;

        public Segment(string path, long start, long capacity, double xStart, double xStep)
        {
            (_path, Start, _capacity, _xStart, _xStep) = (path, start, capacity, xStart, xStep);
            var bytes = ColumnFile.HeaderSize + (capacity * sizeof(T));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.SetLength(bytes);
            }

            _map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, bytes, MemoryMappedFileAccess.ReadWrite);
            _view = _map.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.ReadWrite);
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            _base = pointer + _view.PointerOffset;
            _values = (T*)(_base + ColumnFile.HeaderSize);
            WriteHeader(0);

            var levels = new List<long> { (capacity + Block - 1) / Block };
            while (levels[^1] > 1)
            {
                levels.Add((levels[^1] + Factor - 1) / Factor);
            }

            (_min, _max) = ([.. levels.Select(length => new T[length])], [.. levels.Select(length => new T[length])]);
        }

        public long Start { get; }

        public long Count => Volatile.Read(ref _count);

        public bool IsFull => _count == _capacity;

        public MinMax<T> Total
        {
            get
            {
                lock (_min)
                {
                    return _total;
                }
            }
        }

        public int Append(ReadOnlySpan<T> values)
        {
            var taken = (int)Math.Min(values.Length, _capacity - _count);
            var target = new Span<T>(_values + _count, taken);
            values[..taken].CopyTo(target);
            var extremes = Extremes(target);
            lock (_min)
            {
                _total = Combine(_total, extremes);
            }

            Volatile.Write(ref _count, _count + taken);

            // Each block that is full now gets its extremes, and each level above whose blocks are full.
            while ((_blocks + 1) * Block <= _count)
            {
                var block = _blocks;
                var range = Extremes(new ReadOnlySpan<T>(_values + (block * Block), Block));
                (_min[0][block], _max[0][block]) = (range.Min, range.Max);
                for (var (level, index) = (1, block); level < _min.Length && index % Factor == Factor - 1; level++)
                {
                    var parent = index / Factor;
                    var (low, high) = (T.MaxValue, T.MinValue);
                    for (var child = parent * Factor; child < (parent + 1) * Factor; child++)
                    {
                        (low, high) = (T.Min(low, _min[level - 1][child]), T.Max(high, _max[level - 1][child]));
                    }

                    (_min[level][parent], _max[level][parent]) = (low, high);
                    index = parent;
                }

                Volatile.Write(ref _blocks, block + 1);
            }

            return taken;
        }

        public ReadOnlySpan<T> Read(long start, int maxLength) =>
            new(_values + start, (int)Math.Min(maxLength, Count - start));

        // Values at the ends of the range one by one, the whole blocks between from the coarsest level that fits.
        public MinMax<T> Query(long start, long end, bool blocksOnly)
        {
            var blocks = Volatile.Read(ref _blocks);
            var firstBlock = blocksOnly ? start / Block : (start + Block - 1) / Block;
            var lastBlock = Math.Min(blocks, blocksOnly ? (end + Block - 1) / Block : end / Block);
            if (firstBlock >= lastBlock)
            {
                return Extremes(Read(start, (int)(end - start)));
            }

            var result = blocksOnly ? MinMax<T>.Empty : Extremes(Read(start, (int)((firstBlock * Block) - start)));
            for (var block = firstBlock; block < lastBlock;)
            {
                var (level, size) = (0, 1L);
                while (level + 1 < _min.Length && block % (size * Factor) == 0 && block + (size * Factor) <= lastBlock)
                {
                    (level, size) = (level + 1, size * Factor);
                }

                var index = block / size;
                result = Combine(result, new MinMax<T>(_min[level][index], _max[level][index]));
                block += size;
            }

            var tail = lastBlock * Block;
            return tail < end ? Combine(result, Extremes(Read(tail, (int)(end - tail)))) : result;
        }

        public void Finish()
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                WriteHeader(_count);
                _view.Flush();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view.Dispose();
                _map.Dispose();
            }
        }

        public void Delete()
        {
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
                // A reader of the files outside keeps it open; it goes with the next cleaning of the folder.
            }
        }

        private static MinMax<T> Extremes(ReadOnlySpan<T> values)
        {
            var (low, high) = (T.MaxValue, T.MinValue);
            foreach (var value in values)
            {
                if (T.IsFinite(value))
                {
                    (low, high) = (T.Min(low, value), T.Max(high, value));
                }
            }

            return new MinMax<T>(low, high);
        }

        private void WriteHeader(long rows) =>
            ColumnFile.WriteHeader(new Span<byte>(_base, ColumnFile.HeaderSize), ColumnFile.TypeOf<T>(), 1, rows, _xStart, _xStep, 0);
    }
}
