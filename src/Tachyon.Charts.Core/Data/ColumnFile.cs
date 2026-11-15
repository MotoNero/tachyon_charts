using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tachyon.Charts.Core.Data;

/// <summary>The type of the values in a <see cref="ColumnFile"/>.</summary>
public enum ColumnType
{
    /// <summary>64-bit floating point.</summary>
    Double = 1,

    /// <summary>32-bit floating point.</summary>
    Single = 2,

    /// <summary>16-bit integer.</summary>
    Int16 = 3,

    /// <summary>32-bit integer.</summary>
    Int32 = 4,
}

/// <summary>
/// Values at an even X step that may be far larger than memory, read in pieces without copying. The count is a
/// <see cref="long"/>: there is no limit of 2^31 points.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks>Members are safe to use from any thread.</remarks>
public interface IColumnSource<T>
    where T : unmanaged, INumber<T>
{
    /// <summary>Gets the number of values.</summary>
    long Count { get; }

    /// <summary>Gets the X of the first value.</summary>
    double XStart { get; }

    /// <summary>Gets the X distance between two values.</summary>
    double XStep { get; }

    /// <summary>Gets the version, which grows with every change.</summary>
    long Version { get; }

    /// <summary>
    /// Reads values from a place on: as many as lie one after the other there, at most <paramref name="maxLength"/>. A
    /// source stored in blocks returns fewer at the end of a block; the caller reads on from where it stopped.
    /// </summary>
    /// <param name="start">The index of the first value.</param>
    /// <param name="maxLength">The most values wanted.</param>
    /// <returns>The values, valid as long as the source is open; empty at or after the end.</returns>
    ReadOnlySpan<T> Read(long start, int maxLength);
}

/// <summary>
/// A file of columns of numbers at an even X step, mapped into memory: the operating system reads what is used and keeps
/// it in its cache, so files larger than memory open at once. Write one with <see cref="ColumnFileWriter{T}"/>.
/// </summary>
/// <remarks>
/// The file begins with a header of 64 bytes, all little-endian: the text <c>TCOL</c>, the version 1 (int32), the
/// <see cref="ColumnType"/> (int32), the number of columns (int32), the number of rows (int64), the X of the first row
/// and the X step (two doubles), the rows per block (int64; 0 for one block of all rows). Each block holds its rows of
/// the first column, then of the second and so on; every block but the last has its full rows, the last as many as are
/// left. Members are safe to use from any thread; reading after <see cref="Dispose"/> is not.
/// </remarks>
public sealed unsafe class ColumnFile : IDisposable
{
    /// <summary>The size of the header in bytes.</summary>
    public const int HeaderSize = 64;

    internal const uint Magic = 0x4C4F4354; // "TCOL"
    internal const int FormatVersion = 1;

    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;

    // Mapping the file is the point of this class: spans point straight into the pages of the file, which unsafe code
    // needs; the pointer lives as long as the view and is released in Dispose.
    private readonly byte* _data;
    private int _disposed;

    private ColumnFile(string path, MemoryMappedFile map, MemoryMappedViewAccessor view, byte* data, ColumnType type, int columns, long rows, double xStart, double xStep, long blockRows)
    {
        (Path, _map, _view) = (path, map, view);
        _data = data;
        (Type, ColumnCount, RowCount, XStart, XStep, BlockRows) = (type, columns, rows, xStart, xStep, blockRows);
    }

    /// <summary>Gets the path of the file.</summary>
    public string Path { get; }

    /// <summary>Gets the type of the values.</summary>
    public ColumnType Type { get; }

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount { get; }

    /// <summary>Gets the number of rows.</summary>
    public long RowCount { get; }

    /// <summary>Gets the X of the first row.</summary>
    public double XStart { get; }

    /// <summary>Gets the X distance between two rows.</summary>
    public double XStep { get; }

    /// <summary>Gets the rows per block; the whole file is one block when it has one column.</summary>
    public long BlockRows { get; }

    /// <summary>Opens a file for reading.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidDataException">The file is no column file, or shorter than its header says.</exception>
    public static ColumnFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var length = new FileInfo(path).Length;
        if (length < HeaderSize)
        {
            throw new InvalidDataException($"{path} is shorter than the header of a column file.");
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        var (type, columns, rows, xStart, xStep, blockRows) = ReadHeader(header, path);
        var size = SizeOf(type);
        var block = blockRows == 0 ? rows : blockRows;
        var blocks = block == 0 ? 0 : (rows + block - 1) / block;
        var needed = HeaderSize + (blocks == 0 ? 0 : ((blocks - 1) * block * columns * size) + ((rows - ((blocks - 1) * block)) * columns * size));
        if (length < needed)
        {
            throw new InvalidDataException($"{path} has {length:N0} bytes, its header needs {needed:N0}.");
        }

        var map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* data = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
        return new ColumnFile(path, map, view, data + view.PointerOffset, type, columns, rows, xStart, xStep, blockRows);
    }

    /// <summary>Gets a column.</summary>
    /// <typeparam name="T">The type of its values, which must match <see cref="Type"/>.</typeparam>
    /// <param name="index">The number of the column from 0.</param>
    /// <returns>The column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such column.</exception>
    /// <exception cref="InvalidOperationException">The values have another type.</exception>
    public FileColumn<T> Column<T>(int index = 0)
        where T : unmanaged, INumber<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ColumnCount);
        if (TypeOf<T>() != Type)
        {
            throw new InvalidOperationException($"The file holds {Type} values, not {typeof(T).Name}.");
        }

        return new FileColumn<T>(this, index);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _map.Dispose();
        }
    }

    /// <summary>Gets the type of a value type.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <returns>The column type.</returns>
    /// <exception cref="NotSupportedException">Columns cannot hold that type.</exception>
    internal static ColumnType TypeOf<T>() =>
        typeof(T) == typeof(double) ? ColumnType.Double
        : typeof(T) == typeof(float) ? ColumnType.Single
        : typeof(T) == typeof(short) ? ColumnType.Int16
        : typeof(T) == typeof(int) ? ColumnType.Int32
        : throw new NotSupportedException($"Columns hold double, float, short or int, not {typeof(T).Name}.");

    internal static int SizeOf(ColumnType type) => type switch
    {
        ColumnType.Double => 8,
        ColumnType.Single or ColumnType.Int32 => 4,
        _ => 2,
    };

    internal static void WriteHeader(Span<byte> header, ColumnType type, int columns, long rows, double xStart, double xStep, long blockRows)
    {
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], (int)type);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], columns);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], rows);
        BinaryPrimitives.WriteDoubleLittleEndian(header[24..], xStart);
        BinaryPrimitives.WriteDoubleLittleEndian(header[32..], xStep);
        BinaryPrimitives.WriteInt64LittleEndian(header[40..], blockRows);
    }

    /// <summary>Reads values of a column from a row on, as many as lie together in its block.</summary>
    internal ReadOnlySpan<T> Read<T>(int column, long start, int maxLength)
        where T : unmanaged
    {
        if (start < 0 || maxLength <= 0 || start >= RowCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(start);
            return [];
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var block = BlockRows == 0 ? RowCount : BlockRows;
        var index = start / block;
        var within = start - (index * block);
        var rowsInBlock = Math.Min(block, RowCount - (index * block));
        var length = (int)Math.Min(maxLength, rowsInBlock - within);
        var offset = HeaderSize + (((index * block * ColumnCount) + (column * rowsInBlock) + within) * sizeof(T));
        return new ReadOnlySpan<T>(_data + offset, length);
    }

    private static (ColumnType Type, int Columns, long Rows, double XStart, double XStep, long BlockRows) ReadHeader(ReadOnlySpan<byte> header, string path)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
        {
            throw new InvalidDataException($"{path} is no column file.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"{path} has the version {version}; this library reads version {FormatVersion}.");
        }

        var type = (ColumnType)BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        var columns = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        var rows = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        var blockRows = BinaryPrimitives.ReadInt64LittleEndian(header[40..]);
        if (!Enum.IsDefined(type) || columns < 1 || rows < 0 || blockRows < 0 || (blockRows == 0 && columns > 1))
        {
            throw new InvalidDataException($"{path} has a header that cannot be read: type {(int)type}, {columns} columns, {rows} rows, {blockRows} rows per block.");
        }

        return (type, columns, rows, BinaryPrimitives.ReadDoubleLittleEndian(header[24..]), BinaryPrimitives.ReadDoubleLittleEndian(header[32..]), blockRows);
    }
}

/// <summary>A column of a <see cref="ColumnFile"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class FileColumn<T> : IColumnSource<T>
    where T : unmanaged, INumber<T>
{
    private readonly ColumnFile _file;
    private readonly int _index;

    internal FileColumn(ColumnFile file, int index) => (_file, _index) = (file, index);

    /// <inheritdoc />
    public long Count => _file.RowCount;

    /// <inheritdoc />
    public double XStart => _file.XStart;

    /// <inheritdoc />
    public double XStep => _file.XStep;

    /// <inheritdoc />
    public long Version => 0;

    /// <inheritdoc />
    public ReadOnlySpan<T> Read(long start, int maxLength) => _file.Read<T>(_index, start, maxLength);
}

/// <summary>
/// Writes a <see cref="ColumnFile"/> as the values come, without holding them: one column goes straight to the file,
/// several are gathered block by block.
/// </summary>
/// <typeparam name="T">The value type: double, float, short or int.</typeparam>
/// <remarks>The header is written on <see cref="Dispose"/>, with the number of rows; a file is complete after it.</remarks>
public sealed class ColumnFileWriter<T> : IDisposable
    where T : unmanaged, INumber<T>
{
    /// <summary>The rows per block for several columns.</summary>
    public const int DefaultBlockRows = 1 << 20;

    private readonly FileStream _stream;
    private readonly ColumnType _type;
    private readonly int _columns;
    private double _xStart;
    private double _xStep;
    private readonly int _blockRows;
    private readonly T[]? _block;
    private int _rowsInBlock;
    private bool _disposed;

    /// <summary>Creates the file, replacing one that is there.</summary>
    /// <param name="path">The path.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="xStart">The X of the first row.</param>
    /// <param name="xStep">The X distance between two rows.</param>
    /// <param name="blockRows">The rows per block when there are several columns.</param>
    /// <exception cref="NotSupportedException">Columns cannot hold <typeparamref name="T"/>.</exception>
    public ColumnFileWriter(string path, int columns = 1, double xStart = 0, double xStep = 1, int blockRows = DefaultBlockRows)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockRows, 1);
        (_type, _columns, _xStart, _xStep) = (ColumnFile.TypeOf<T>(), columns, xStart, xStep);
        _blockRows = columns == 1 ? 0 : blockRows;
        _block = columns == 1 ? null : new T[(long)blockRows * columns];
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 22);
        _stream.Write(new byte[ColumnFile.HeaderSize]);
    }

    /// <summary>Gets the number of rows written.</summary>
    public long RowCount { get; private set; }

    /// <summary>Appends rows: the values of a row next to each other, then the next row.</summary>
    /// <param name="rows">The values, a whole number of rows.</param>
    /// <exception cref="ArgumentException">The values are not a whole number of rows.</exception>
    public void Append(ReadOnlySpan<T> rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (rows.Length % _columns != 0)
        {
            throw new ArgumentException($"{rows.Length} values are no whole number of rows of {_columns} columns.", nameof(rows));
        }

        if (_block is null)
        {
            _stream.Write(MemoryMarshal.AsBytes(rows));
            RowCount += rows.Length;
            return;
        }

        var count = rows.Length / _columns;
        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                _block[(column * _blockRows) + _rowsInBlock] = rows[(row * _columns) + column];
            }

            RowCount++;
            if (++_rowsInBlock == _blockRows)
            {
                FlushBlock();
            }
        }
    }

    /// <summary>Sets the X of the first row and the X step, when they are known only after the rows, as from an X column.</summary>
    /// <param name="xStart">The X of the first row.</param>
    /// <param name="xStep">The X distance between two rows.</param>
    public void SetX(double xStart, double xStep)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        (_xStart, _xStep) = (xStart, xStep);
    }

    /// <summary>Completes the file: the last block and the header.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_rowsInBlock > 0)
        {
            FlushBlock();
        }

        Span<byte> header = stackalloc byte[ColumnFile.HeaderSize];
        ColumnFile.WriteHeader(header, _type, _columns, RowCount, _xStart, _xStep, _blockRows);
        _stream.Position = 0;
        _stream.Write(header);
        _stream.Dispose();
    }

    // A block holds its rows of each column in turn; the last block only as many as it got.
    private void FlushBlock()
    {
        for (var column = 0; column < _columns; column++)
        {
            _stream.Write(MemoryMarshal.AsBytes(_block.AsSpan(column * _blockRows, _rowsInBlock)));
        }

        _rowsInBlock = 0;
    }
}
