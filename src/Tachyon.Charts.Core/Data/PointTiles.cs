using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Points of a scatter far larger than memory, sorted once into tiles beside the data: 1024 × 1024 tiles over the extent,
/// the points of each tile in a random order, and a count of points on a grid of 4096 × 4096. A wide view reads the
/// grid; a narrow one reads the points of its tiles, a fair share of each dense tile when there are too many, and every
/// point of a sparse one. The file is built on all cores in the background and taken again next time when it belongs
/// to the same points.
/// </summary>
public sealed unsafe class PointTiles : IDisposable
{
    /// <summary>The tiles per side.</summary>
    public const int TilesPerSide = 1024;

    /// <summary>The cells of the count grid per side.</summary>
    public const int GridSize = 4096;

    internal const int CellsPerTile = GridSize / TilesPerSide;
    private const uint Magic = 0x53545054; // "TPTS"
    private const int FormatVersion = 1;
    private const int HeaderSize = 128;
    private const int PieceSize = 1 << 22;
    private const long OffsetsBytes = ((TilesPerSide * TilesPerSide) + 1) * 8L;
    private const long GridBytes = (long)GridSize * GridSize * 4;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;

    // The mapped file: the points are read in place, which needs a pointer; released in Dispose.
    private byte* _data;
    private long[] _offsets = [];
    private uint[][] _grids = [];
    private long _done;
    private long _total = 1;
    private volatile bool _isComplete;
    private int _disposed;

    private PointTiles()
    {
    }

    /// <summary>Gets the number of points with finite coordinates, once complete.</summary>
    public long Count { get; private set; }

    /// <summary>Gets the extent of X, once complete.</summary>
    public DoubleRange XRange { get; private set; } = DoubleRange.Empty;

    /// <summary>Gets the extent of Y, once complete.</summary>
    public DoubleRange YRange { get; private set; } = DoubleRange.Empty;

    /// <summary>Gets how far the tiles are built, from 0 to 1.</summary>
    public double Progress => _isComplete ? 1 : Math.Min(0.99, (double)Interlocked.Read(ref _done) / Interlocked.Read(ref _total));

    /// <summary>Gets a value indicating whether the tiles are ready.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Gets the building, or the reading of the kept file.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Gets a value indicating whether the tiles came from a kept file.</summary>
    public bool IsFromFile { get; private set; }

    /// <summary>
    /// Starts sorting the points of two columns into tiles in the background, or reads the tiles from their file when
    /// that belongs to the same points. Points with a coordinate that is not finite are left out.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values, as many.</param>
    /// <param name="path">The file of the tiles, such as the path of the data with <c>.tpts</c>; about 8 bytes per point.</param>
    /// <param name="cancellation">Stops the building.</param>
    /// <returns>The tiles, ready once <see cref="Completion"/> is.</returns>
    public static PointTiles Build<T>(IColumnSource<T> x, IColumnSource<T> y, string path, CancellationToken cancellation = default)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (x.Count != y.Count)
        {
            throw new ArgumentException($"X has {x.Count:N0} values, Y {y.Count:N0}.", nameof(y));
        }

        // Tiles kept from before open at once; only new ones are built in the background.
        var tiles = new PointTiles();
        var fingerprint = Fingerprint(x, y);
        if (tiles.TryOpen(path, x.Count, fingerprint))
        {
            tiles.IsFromFile = true;
            tiles._isComplete = true;
            return tiles;
        }

        tiles.Completion = Task.Run(() => tiles.Make(x, y, path, fingerprint, cancellation), cancellation);
        return tiles;
    }

    /// <summary>Releases the file.</summary>
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

    /// <summary>Gets the number of points of a tile.</summary>
    internal long TileCount(int tileX, int tileY)
    {
        var tile = (tileY * TilesPerSide) + tileX;
        return _offsets[tile + 1] - _offsets[tile];
    }

    /// <summary>Gets points of a tile, each as its place inside the tile from 0 to 1.</summary>
    internal ReadOnlySpan<Vector2> TilePoints(int tileX, int tileY, int maxCount)
    {
        var tile = (tileY * TilesPerSide) + tileX;
        var start = _offsets[tile];
        var count = (int)Math.Min(maxCount, _offsets[tile + 1] - start);
        return new ReadOnlySpan<Vector2>(_data + PointsStart + (start * 8), count);
    }

    /// <summary>Gets the count grid of a level: level 0 has <see cref="GridSize"/> cells per side, each level half as many.</summary>
    internal ReadOnlySpan<uint> Grid(int level) => _grids[level];

    /// <summary>Gets the number of levels of the count grid.</summary>
    internal int GridLevels => _grids.Length;

    private static long PointsStart => HeaderSize + OffsetsBytes + GridBytes;

    private void Make<T>(IColumnSource<T> x, IColumnSource<T> y, string path, ulong fingerprint, CancellationToken cancellation)
        where T : unmanaged, INumber<T>
    {
        // Four passes over the points: extent, counts per tile, sorting into the file, then each tile on its own.
        var pieces = (int)((x.Count + PieceSize - 1) / PieceSize);
        Interlocked.Exchange(ref _total, Math.Max(1, (pieces * 3L) + (TilesPerSide * TilesPerSide / 256)));
        var options = new ParallelOptions { CancellationToken = cancellation };
        var extents = new (double MinX, double MaxX, double MinY, double MaxY, long Count)[pieces];
        Parallel.For(0, pieces, options, piece => extents[piece] = Extent(x, y, piece));
        var (minX, maxX, minY, maxY, count) = (double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity, 0L);
        foreach (var extent in extents)
        {
            (minX, maxX, minY, maxY, count) = (Math.Min(minX, extent.MinX), Math.Max(maxX, extent.MaxX), Math.Min(minY, extent.MinY), Math.Max(maxY, extent.MaxY), count + extent.Count);
        }

        if (count == 0)
        {
            (minX, maxX, minY, maxY) = (0, 1, 0, 1);
        }

        var place = new Placement(minX, maxX, minY, maxY);
        var tileCounts = new long[TilesPerSide * TilesPerSide];
        Parallel.For(0, pieces, options, () => new long[TilesPerSide * TilesPerSide], (piece, _, local) =>
        {
            CountTiles(x, y, piece, place, local);
            return local;
        }, local =>
        {
            lock (tileCounts)
            {
                for (var i = 0; i < local.Length; i++)
                {
                    tileCounts[i] += local[i];
                }
            }
        });

        var offsets = new long[tileCounts.Length + 1];
        for (var i = 0; i < tileCounts.Length; i++)
        {
            offsets[i + 1] = offsets[i] + tileCounts[i];
        }

        var part = path + ".part";
        var length = PointsStart + (count * 8);
        using (var map = MemoryMappedFile.CreateFromFile(part, FileMode.Create, null, length, MemoryMappedFileAccess.ReadWrite))
        using (var view = map.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite))
        {
            byte* data = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
            try
            {
                data += view.PointerOffset;
                var points = (Vector2*)(data + PointsStart);
                var cursors = (long[])offsets.Clone();
                Parallel.For(0, pieces, options, () => new int[TilesPerSide * TilesPerSide], (piece, _, local) =>
                {
                    Sort(x, y, piece, place, local, cursors, points);
                    return local;
                }, _ => { });

                // Each tile on its own: its points in a random order, its cells of the grid counted.
                var grid = (uint*)(data + HeaderSize + OffsetsBytes);
                new Span<byte>(grid, (int)Math.Min(int.MaxValue, GridBytes)).Clear();
                Parallel.For(0, TilesPerSide * TilesPerSide / 256, options, group =>
                {
                    for (var tile = group * 256; tile < (group + 1) * 256; tile++)
                    {
                        Shuffle(points + offsets[tile], offsets[tile + 1] - offsets[tile], tile);
                        Cells(points + offsets[tile], offsets[tile + 1] - offsets[tile], tile, grid);
                    }

                    Interlocked.Increment(ref _done);
                });

                var header = new Span<byte>(data, HeaderSize);
                header.Clear();
                BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
                BinaryPrimitives.WriteInt64LittleEndian(header[8..], x.Count);
                BinaryPrimitives.WriteInt64LittleEndian(header[16..], count);
                BinaryPrimitives.WriteUInt64LittleEndian(header[24..], fingerprint);
                BinaryPrimitives.WriteDoubleLittleEndian(header[32..], minX);
                BinaryPrimitives.WriteDoubleLittleEndian(header[40..], maxX);
                BinaryPrimitives.WriteDoubleLittleEndian(header[48..], minY);
                BinaryPrimitives.WriteDoubleLittleEndian(header[56..], maxY);
                MemoryMarshal.AsBytes(offsets.AsSpan()).CopyTo(new Span<byte>(data + HeaderSize, (int)OffsetsBytes));
                view.Flush();
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        File.Move(part, path, overwrite: true);
        if (!TryOpen(path, x.Count, fingerprint))
        {
            throw new IOException($"The tiles written to {path} cannot be read back.");
        }

        _isComplete = true;
    }

    private bool TryOpen(string path, long sourceCount, ulong fingerprint)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < PointsStart)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        var count = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != FormatVersion
            || BinaryPrimitives.ReadInt64LittleEndian(header[8..]) != sourceCount || BinaryPrimitives.ReadUInt64LittleEndian(header[24..]) != fingerprint
            || new FileInfo(path).Length < PointsStart + (count * 8))
        {
            return false;
        }

        var map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* data = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
        data += view.PointerOffset;
        (_map, _view) = (map, view);
        _data = data;
        Count = count;
        XRange = new DoubleRange(BinaryPrimitives.ReadDoubleLittleEndian(header[32..]), BinaryPrimitives.ReadDoubleLittleEndian(header[40..]));
        YRange = new DoubleRange(BinaryPrimitives.ReadDoubleLittleEndian(header[48..]), BinaryPrimitives.ReadDoubleLittleEndian(header[56..]));
        _offsets = new ReadOnlySpan<long>(data + HeaderSize, (TilesPerSide * TilesPerSide) + 1).ToArray();

        // The levels of the grid, each cell the sum of four below it, down to 64 cells per side.
        var grids = new List<uint[]> { new ReadOnlySpan<uint>(data + HeaderSize + OffsetsBytes, GridSize * GridSize).ToArray() };
        for (var size = GridSize / 2; size >= 64; size /= 2)
        {
            var below = grids[^1];
            var level = new uint[size * size];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    var at = (row * 2 * size * 2) + (column * 2);
                    level[(row * size) + column] = below[at] + below[at + 1] + below[at + (size * 2)] + below[at + (size * 2) + 1];
                }
            }

            grids.Add(level);
        }

        _grids = [.. grids];
        return true;
    }

    private (double, double, double, double, long) Extent<T>(IColumnSource<T> x, IColumnSource<T> y, int piece)
        where T : unmanaged, INumber<T>
    {
        var (minX, maxX, minY, maxY, count) = (double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity, 0L);
        ForEach(x, y, piece, (vx, vy) =>
        {
            (minX, maxX, minY, maxY) = (Math.Min(minX, vx), Math.Max(maxX, vx), Math.Min(minY, vy), Math.Max(maxY, vy));
            count++;
        });
        Interlocked.Increment(ref _done);
        return (minX, maxX, minY, maxY, count);
    }

    private void CountTiles<T>(IColumnSource<T> x, IColumnSource<T> y, int piece, Placement place, long[] counts)
        where T : unmanaged, INumber<T>
    {
        ForEach(x, y, piece, (vx, vy) => counts[place.Tile(vx, vy).Tile]++);
        Interlocked.Increment(ref _done);
    }

    // Twice over the piece: the counts reserve the room in each tile at once, then the points go to their room.
    private void Sort<T>(IColumnSource<T> x, IColumnSource<T> y, int piece, Placement place, int[] local, long[] cursors, Vector2* points)
        where T : unmanaged, INumber<T>
    {
        Array.Clear(local);
        ForEach(x, y, piece, (vx, vy) => local[place.Tile(vx, vy).Tile]++);
        var starts = new long[local.Length];
        for (var tile = 0; tile < local.Length; tile++)
        {
            if (local[tile] > 0)
            {
                starts[tile] = Interlocked.Add(ref cursors[tile], local[tile]) - local[tile];
            }
        }

        ForEach(x, y, piece, (vx, vy) =>
        {
            var (tile, inside) = place.Tile(vx, vy);
            points[starts[tile]++] = inside;
        });
        Interlocked.Increment(ref _done);
    }

    // Reads a piece of both columns and hands on each point with finite coordinates.
    private static void ForEach<T>(IColumnSource<T> x, IColumnSource<T> y, int piece, Action<double, double> take)
        where T : unmanaged, INumber<T>
    {
        var start = (long)piece * PieceSize;
        var end = Math.Min(x.Count, start + PieceSize);
        for (var at = start; at < end;)
        {
            var xs = x.Read(at, (int)(end - at));
            var ys = y.Read(at, xs.Length);
            var length = Math.Min(xs.Length, ys.Length);
            for (var i = 0; i < length; i++)
            {
                var (vx, vy) = (double.CreateTruncating(xs[i]), double.CreateTruncating(ys[i]));
                if (double.IsFinite(vx) && double.IsFinite(vy))
                {
                    take(vx, vy);
                }
            }

            if (length == 0)
            {
                break;
            }

            at += length;
        }
    }

    private static void Shuffle(Vector2* points, long count, int tile)
    {
        var random = new Random(tile); // NOSONAR: an order for fair shares, no security
        for (var i = count - 1; i > 0; i--)
        {
            var j = random.NextInt64(i + 1);
            (points[i], points[j]) = (points[j], points[i]);
        }
    }

    private static void Cells(Vector2* points, long count, int tile, uint* grid)
    {
        var (tileX, tileY) = (tile % TilesPerSide, tile / TilesPerSide);
        for (var i = 0L; i < count; i++)
        {
            var cellX = Math.Min(CellsPerTile - 1, (int)(points[i].X * CellsPerTile));
            var cellY = Math.Min(CellsPerTile - 1, (int)(points[i].Y * CellsPerTile));
            grid[(((tileY * CellsPerTile) + cellY) * (long)GridSize) + (tileX * CellsPerTile) + cellX]++;
        }
    }

    private static ulong Fingerprint<T>(IColumnSource<T> x, IColumnSource<T> y)
        where T : unmanaged, INumber<T>
    {
        var hash = 14695981039346656037UL;
        void Add(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                hash = (hash ^ b) * 1099511628211UL;
            }
        }

        Span<byte> numbers = stackalloc byte[12];
        BinaryPrimitives.WriteInt64LittleEndian(numbers, x.Count);
        BinaryPrimitives.WriteInt32LittleEndian(numbers[8..], Marshal.SizeOf<T>());
        Add(numbers);
        foreach (var column in (IColumnSource<T>[])[x, y])
        {
            for (var sample = 0; sample < 64 && column.Count > 0; sample++)
            {
                var at = column.Count * sample / 64;
                Add(MemoryMarshal.AsBytes(column.Read(at, (int)Math.Min(64, column.Count - at))));
            }
        }

        return hash;
    }

    // Where a point lies: its tile, and its place inside the tile from 0 to 1.
    private readonly struct Placement(double minX, double maxX, double minY, double maxY)
    {
        private readonly double _scaleX = TilesPerSide / (maxX > minX ? maxX - minX : 1);
        private readonly double _scaleY = TilesPerSide / (maxY > minY ? maxY - minY : 1);

        public (int Tile, Vector2 Inside) Tile(double x, double y)
        {
            var (u, v) = ((x - minX) * _scaleX, (y - minY) * _scaleY);
            var (tileX, tileY) = (Math.Clamp((int)u, 0, TilesPerSide - 1), Math.Clamp((int)v, 0, TilesPerSide - 1));
            return ((tileY * TilesPerSide) + tileX, new Vector2((float)Math.Clamp(u - tileX, 0, 1), (float)Math.Clamp(v - tileY, 0, 1)));
        }
    }
}
