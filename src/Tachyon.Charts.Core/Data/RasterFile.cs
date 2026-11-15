using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// A grid of floats far larger than memory, row by row in a file, such as a heatmap of 64k × 64k cells or a spectrogram
/// of millions of rows. The file is mapped into memory: the operating system reads the rows a view needs.
/// </summary>
/// <remarks>
/// The format: a header of 64 bytes (<c>TRAS</c>, version 1, the width as int32, the height as int64, then the left
/// edge and width of a column and the lower edge and height of a row as doubles), then the rows, little-endian floats.
/// </remarks>
public sealed unsafe class RasterFile : IDisposable
{
    /// <summary>The size of the header in bytes.</summary>
    public const int HeaderSize = 64;

    internal const uint Magic = 0x53415254; // "TRAS"
    private const int FormatVersion = 1;

    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;

    // The rows are read in place, which needs a pointer into the mapped file; released in Dispose.
    private readonly byte* _data;
    private int _disposed;

    private RasterFile(string path, MemoryMappedFile map, MemoryMappedViewAccessor view, byte* data, int width, long height, double xStart, double xStep, double yStart, double yStep)
    {
        (Path, _map, _view) = (path, map, view);
        _data = data;
        (Width, Height, XStart, XStep, YStart, YStep) = (width, height, xStart, xStep, yStart, yStep);
    }

    /// <summary>Gets the path.</summary>
    public string Path { get; }

    /// <summary>Gets the number of columns.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public long Height { get; }

    /// <summary>Gets the left edge of the first column.</summary>
    public double XStart { get; }

    /// <summary>Gets the width of a column.</summary>
    public double XStep { get; }

    /// <summary>Gets the lower edge of the first row.</summary>
    public double YStart { get; }

    /// <summary>Gets the height of a row.</summary>
    public double YStep { get; }

    /// <summary>Opens a file and maps it into memory; it stays open until disposed.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidDataException">The file is no raster file, or shorter than its header says.</exception>
    public static RasterFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Span<byte> header = stackalloc byte[HeaderSize];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length < HeaderSize)
            {
                throw new InvalidDataException($"{path} is shorter than the header of a raster file.");
            }

            stream.ReadExactly(header);
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != FormatVersion)
        {
            throw new InvalidDataException($"{path} is no raster file of version {FormatVersion}.");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        var height = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        if (width < 1 || height < 0 || new FileInfo(path).Length < HeaderSize + (width * height * 4))
        {
            throw new InvalidDataException($"{path} is shorter than {width:N0} × {height:N0} floats.");
        }

        var map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* data = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
        return new RasterFile(path, map, view, data + view.PointerOffset, width, height,
            BinaryPrimitives.ReadDoubleLittleEndian(header[24..]), BinaryPrimitives.ReadDoubleLittleEndian(header[32..]),
            BinaryPrimitives.ReadDoubleLittleEndian(header[40..]), BinaryPrimitives.ReadDoubleLittleEndian(header[48..]));
    }

    /// <summary>Reads a row without copying it.</summary>
    /// <param name="row">The row, from 0 to <see cref="Height"/> − 1.</param>
    /// <returns>The values of the row.</returns>
    public ReadOnlySpan<float> Row(long row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Height);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new ReadOnlySpan<float>(_data + HeaderSize + (row * Width * 4), Width);
    }

    /// <summary>Releases the mapping.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _map.Dispose();
        }
    }

    internal static void WriteHeader(Span<byte> header, int width, long height, double xStart, double xStep, double yStart, double yStep)
    {
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], width);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], height);
        BinaryPrimitives.WriteDoubleLittleEndian(header[24..], xStart);
        BinaryPrimitives.WriteDoubleLittleEndian(header[32..], xStep);
        BinaryPrimitives.WriteDoubleLittleEndian(header[40..], yStart);
        BinaryPrimitives.WriteDoubleLittleEndian(header[48..], yStep);
    }
}

/// <summary>Writes a raster file row by row, without holding the rows; the file is complete when the writer is disposed.</summary>
public sealed class RasterFileWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _width;
    private readonly (double XStart, double XStep, double YStart, double YStep) _cells;
    private bool _disposed;

    /// <summary>Starts a new file, replacing one of the same name.</summary>
    /// <param name="path">The path.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="xStart">The left edge of the first column.</param>
    /// <param name="xStep">The width of a column.</param>
    /// <param name="yStart">The lower edge of the first row.</param>
    /// <param name="yStep">The height of a row.</param>
    public RasterFileWriter(string path, int width, double xStart = 0, double xStep = 1, double yStart = 0, double yStep = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        (_width, _cells) = (width, (xStart, xStep, yStart, yStep));
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 22);
        _stream.Write(new byte[RasterFile.HeaderSize]);
    }

    /// <summary>Gets the number of rows written.</summary>
    public long Height { get; private set; }

    /// <summary>Appends whole rows.</summary>
    /// <param name="rows">The values, a whole number of rows.</param>
    public void Append(ReadOnlySpan<float> rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (rows.Length % _width != 0)
        {
            throw new ArgumentException($"{rows.Length} values are no whole number of rows of {_width}.", nameof(rows));
        }

        _stream.Write(MemoryMarshal.AsBytes(rows));
        Height += rows.Length / _width;
    }

    /// <summary>Writes the header and closes the file.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Span<byte> header = stackalloc byte[RasterFile.HeaderSize];
        RasterFile.WriteHeader(header, _width, Height, _cells.XStart, _cells.XStep, _cells.YStart, _cells.YStep);
        _stream.Position = 0;
        _stream.Write(header);
        _stream.Dispose();
    }
}

/// <summary>
/// The stages of a raster file, each half as fine in each direction that is longer than 1024 cells and not far shorter
/// than the other, built once on
/// all cores in the background and kept beside the file. A view reads the stage with about one cell per pixel; the
/// stages together hold about a third of the file.
/// </summary>
public sealed unsafe class RasterPyramid : IDisposable
{
    private const uint Magic = 0x50415254; // "TRAP"
    private const int FormatVersion = 1;
    private const int HeaderSize = 128;
    private const int Floor = 1024;

    private readonly RasterFile _source;
    private (int Width, long Height, long Offset)[] _levels = [];
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;

    // The stages are read in place; released in Dispose.
    private byte* _data;
    private long _done;
    private long _total = 1;
    private volatile bool _isComplete;
    private int _disposed;

    private RasterPyramid(RasterFile source) => _source = source;

    /// <summary>Gets the raster.</summary>
    public RasterFile Source => _source;

    /// <summary>Gets how the stages sum up the cells.</summary>
    public RasterReduction Reduction { get; private init; }

    /// <summary>Gets the lowest and highest finite value, once complete.</summary>
    public DoubleRange Range { get; private set; } = DoubleRange.Empty;

    /// <summary>Gets how far the stages are built, from 0 to 1.</summary>
    public double Progress => _isComplete ? 1 : Math.Min(0.99, (double)Interlocked.Read(ref _done) / Interlocked.Read(ref _total));

    /// <summary>Gets a value indicating whether the stages are ready.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Gets a value indicating whether the stages came from their kept file.</summary>
    public bool IsFromFile { get; private set; }

    /// <summary>Gets the building.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the number of stages, the raster itself included.</summary>
    internal int LevelCount => _levels.Length + 1;

    /// <summary>
    /// Starts building the stages of a raster in the background, or opens them from their file at once when that
    /// belongs to the same raster.
    /// </summary>
    /// <param name="source">The raster; it stays open as long as the stages are in use.</param>
    /// <param name="path">The file of the stages, such as the path of the raster with <c>.tmip</c>.</param>
    /// <param name="reduction">How a stage sums up the cells below it: the mean for fields, the maximum to keep peaks.</param>
    /// <param name="cancellation">Stops the building.</param>
    /// <returns>The stages, ready once <see cref="Completion"/> is.</returns>
    public static RasterPyramid Build(RasterFile source, string path, RasterReduction reduction = RasterReduction.Mean, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var pyramid = new RasterPyramid(source) { Reduction = reduction };
        var fingerprint = pyramid.Fingerprint();
        if (pyramid.TryOpen(path, fingerprint))
        {
            pyramid.IsFromFile = true;
            pyramid._isComplete = true;
            return pyramid;
        }

        pyramid.Completion = Task.Run(() => pyramid.Make(path, fingerprint, cancellation), cancellation);
        return pyramid;
    }

    /// <summary>Releases the file of the stages; the raster stays open.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _view is not null)
        {
            _isComplete = false;
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _map!.Dispose();
        }
    }

    /// <summary>Gets the size of a stage; stage 0 is the raster.</summary>
    internal (int Width, long Height) Size(int level) => level == 0 ? (_source.Width, _source.Height) : (_levels[level - 1].Width, _levels[level - 1].Height);

    /// <summary>Reads a row of a stage without copying it.</summary>
    internal ReadOnlySpan<float> Row(int level, long row)
    {
        if (level == 0)
        {
            return _source.Row(row);
        }

        var (width, _, offset) = _levels[level - 1];
        return new ReadOnlySpan<float>(_data + offset + (row * width * 4), width);
    }

    // The sizes of the stages: each halves a direction that is longer than the floor and not far shorter than the other,
    // until none is. A long spectrogram keeps all its bins while its rows come down to their number.
    private static (int Width, long Height, long Offset)[] Plan(int width, long height)
    {
        var levels = new List<(int, long, long)>();
        var offset = (long)HeaderSize;
        while (width > Floor || height > Floor)
        {
            var halvesWidth = width > Floor && width * 2L > height;
            var halvesHeight = height > Floor && height * 2L > width;
            width = halvesWidth ? (width + 1) / 2 : width;
            height = halvesHeight ? (height + 1) / 2 : height;
            levels.Add((width, height, offset));
            offset += width * height * 4;
        }

        return [.. levels];
    }

    private void Make(string path, ulong fingerprint, CancellationToken cancellation)
    {
        var levels = Plan(_source.Width, _source.Height);
        var length = levels.Length == 0 ? HeaderSize : levels[^1].Offset + (levels[^1].Width * levels[^1].Height * 4);
        long total = 0;
        foreach (var level in levels)
        {
            total += level.Height;
        }

        Interlocked.Exchange(ref _total, Math.Max(1, total));
        var part = path + ".part";
        var (low, high) = (double.PositiveInfinity, double.NegativeInfinity);
        using (var map = MemoryMappedFile.CreateFromFile(part, FileMode.Create, null, length, MemoryMappedFileAccess.ReadWrite))
        using (var view = map.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite))
        {
            byte* data = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
            try
            {
                data += view.PointerOffset;
                var options = new ParallelOptions { CancellationToken = cancellation };
                var gate = new Lock();
                for (var index = 0; index < levels.Length; index++)
                {
                    var (width, height, offset) = levels[index];
                    var (belowWidth, belowHeight) = index == 0 ? (_source.Width, _source.Height) : (levels[index - 1].Width, levels[index - 1].Height);
                    var below = index == 0 ? null : data + levels[index - 1].Offset;
                    var target = (float*)(data + offset);
                    var first = index == 0;
                    Parallel.For(0, (int)((height + 63) / 64), options, () => (double.PositiveInfinity, double.NegativeInfinity), (band, _, range) =>
                    {
                        for (var row = band * 64L; row < Math.Min(height, (band + 1) * 64L); row++)
                        {
                            range = Reduce(below, belowWidth, belowHeight, width, height, row, target + (row * width), range, first);
                        }

                        Interlocked.Add(ref _done, Math.Min(height, (band + 1) * 64L) - (band * 64L));
                        return range;
                    }, range =>
                    {
                        lock (gate)
                        {
                            (low, high) = (Math.Min(low, range.Item1), Math.Max(high, range.Item2));
                        }
                    });
                }

                if (levels.Length == 0)
                {
                    // A raster small enough has no stages; its range comes from the rows themselves.
                    for (var row = 0L; row < _source.Height; row++)
                    {
                        foreach (var value in _source.Row(row))
                        {
                            if (float.IsFinite(value))
                            {
                                (low, high) = (Math.Min(low, value), Math.Max(high, value));
                            }
                        }
                    }
                }

                var header = new Span<byte>(data, HeaderSize);
                header.Clear();
                BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
                BinaryPrimitives.WriteInt32LittleEndian(header[8..], (int)Reduction);
                BinaryPrimitives.WriteUInt64LittleEndian(header[16..], fingerprint);
                BinaryPrimitives.WriteDoubleLittleEndian(header[24..], low);
                BinaryPrimitives.WriteDoubleLittleEndian(header[32..], high);
                view.Flush();
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        // A kept file still open elsewhere cannot be replaced; the new stages are then read from where they were built.
        var built = path;
        try
        {
            File.Move(part, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            built = part;
        }

        if (!TryOpen(built, fingerprint))
        {
            throw new IOException($"The stages written to {path} cannot be read back.");
        }

        _isComplete = true;
    }

    // One row of a stage from the two rows below it (or one, where the height is not halved), and the range of the
    // raster itself while the first stage reads it.
    private (double, double) Reduce(byte* below, int belowWidth, long belowHeight, int width, long height, long row, float* target, (double Low, double High) range, bool first)
    {
        var halvesRows = height < belowHeight;
        var halvesColumns = width < belowWidth;
        var top = halvesRows ? row * 2 : row;
        var upper = Row(below, belowWidth, top);
        var lower = halvesRows && top + 1 < belowHeight ? Row(below, belowWidth, top + 1) : default;
        if (first)
        {
            range = Extend(range, upper);
            range = Extend(range, lower);
        }

        var max = Reduction == RasterReduction.Maximum;
        var nearest = Reduction == RasterReduction.Nearest;
        for (var column = 0; column < width; column++)
        {
            var left = halvesColumns ? column * 2 : column;
            var right = halvesColumns && left + 1 < belowWidth ? left + 1 : -1;
            var (sum, count, highest) = (0.0, 0, float.NegativeInfinity);
            Take(upper[left], ref sum, ref count, ref highest);
            if (right >= 0)
            {
                Take(upper[right], ref sum, ref count, ref highest);
            }

            if (!lower.IsEmpty)
            {
                Take(lower[left], ref sum, ref count, ref highest);
                if (right >= 0)
                {
                    Take(lower[right], ref sum, ref count, ref highest);
                }
            }

            target[column] = nearest ? upper[left] : count == 0 ? float.NaN : max ? highest : (float)(sum / count);
        }

        return range;
    }

    private ReadOnlySpan<float> Row(byte* below, int width, long row) => below is null ? _source.Row(row) : new ReadOnlySpan<float>(below + (row * width * 4), width);

    private static void Take(float value, ref double sum, ref int count, ref float highest)
    {
        if (float.IsFinite(value))
        {
            sum += value;
            count++;
            highest = Math.Max(highest, value);
        }
    }

    private static (double, double) Extend((double Low, double High) range, ReadOnlySpan<float> values)
    {
        foreach (var value in values)
        {
            if (float.IsFinite(value))
            {
                range = (Math.Min(range.Low, value), Math.Max(range.High, value));
            }
        }

        return range;
    }

    private bool TryOpen(string path, ulong fingerprint)
    {
        var levels = Plan(_source.Width, _source.Height);
        var length = levels.Length == 0 ? HeaderSize : levels[^1].Offset + (levels[^1].Width * levels[^1].Height * 4);
        if (!File.Exists(path) || new FileInfo(path).Length < length)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != FormatVersion
            || BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != (int)Reduction || BinaryPrimitives.ReadUInt64LittleEndian(header[16..]) != fingerprint)
        {
            return false;
        }

        var map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* data = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
        (_map, _view, _levels) = (map, view, levels);
        _data = data + view.PointerOffset;
        var (low, high) = (BinaryPrimitives.ReadDoubleLittleEndian(header[24..]), BinaryPrimitives.ReadDoubleLittleEndian(header[32..]));
        Range = low <= high ? new DoubleRange(low, high) : DoubleRange.Empty;
        return true;
    }

    // The size and cells of the raster and samples of its rows: changed values of a kept raster are noticed there.
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

        Span<byte> numbers = stackalloc byte[44];
        BinaryPrimitives.WriteInt32LittleEndian(numbers, _source.Width);
        BinaryPrimitives.WriteInt64LittleEndian(numbers[4..], _source.Height);
        BinaryPrimitives.WriteDoubleLittleEndian(numbers[12..], _source.XStart);
        BinaryPrimitives.WriteDoubleLittleEndian(numbers[20..], _source.XStep);
        BinaryPrimitives.WriteDoubleLittleEndian(numbers[28..], _source.YStart);
        BinaryPrimitives.WriteDoubleLittleEndian(numbers[36..], _source.YStep);
        Add(numbers);
        for (var sample = 0; sample < 64 && _source.Height > 0; sample++)
        {
            var row = _source.Row(_source.Height * sample / 64);
            Add(MemoryMarshal.AsBytes(row[..Math.Min(row.Length, 256)]));
        }

        return hash;
    }
}
