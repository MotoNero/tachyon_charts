using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>
/// The points of a cloud far larger than memory, sorted once into pages beside the data: along a curve through space,
/// so that a page holds points that lie together, at most 32,768 to a page, each page in a random order so that its
/// first points are a fair sample of it. A view loads the pages it sees, as many points of each as its size on screen
/// asks for; the file is built on all cores in the background and taken again next time.
/// </summary>
public sealed unsafe class CloudPages : IDisposable
{
    /// <summary>The most points of a page.</summary>
    public const int PageSize = 32_768;

    private const uint Magic = 0x444C4354; // "TCLD"
    private const int FormatVersion = 1;
    private const int HeaderSize = 128;
    private const int PageBytes = 40;
    private const int Bits = 8;
    private const int Side = 1 << Bits;
    private const int PieceSize = 1 << 22;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;

    // The points are read in place; released in Dispose.
    private byte* _data;
    private Page[] _pages = [];
    private long _done;
    private long _total = 1;
    private volatile bool _isComplete;
    private int _disposed;

    private CloudPages()
    {
    }

    /// <summary>Gets the number of points with finite coordinates, once complete.</summary>
    public long Count { get; private set; }

    /// <summary>Gets the lower corner of the points, once complete.</summary>
    public Point3D Low { get; private set; }

    /// <summary>Gets the upper corner of the points, once complete.</summary>
    public Point3D High { get; private set; }

    /// <summary>Gets the number of pages, once complete.</summary>
    public int PageCount => _pages.Length;

    /// <summary>Gets how far the pages are built, from 0 to 1.</summary>
    public double Progress => _isComplete ? 1 : Math.Min(0.99, (double)Interlocked.Read(ref _done) / Interlocked.Read(ref _total));

    /// <summary>Gets a value indicating whether the pages are ready.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Gets a value indicating whether the pages came from their kept file.</summary>
    public bool IsFromFile { get; private set; }

    /// <summary>Gets the building.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts sorting the points of three columns into pages in the background, or opens the pages at once when their
    /// file belongs to the same points. Points with a coordinate that is not finite are left out.
    /// </summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values, as many.</param>
    /// <param name="z">The Z values, as many.</param>
    /// <param name="path">The file of the pages, such as the path of the data with <c>.tcld</c>; about 12 bytes per point.</param>
    /// <param name="cancellation">Stops the building.</param>
    /// <returns>The pages, ready once <see cref="Completion"/> is.</returns>
    public static CloudPages Build<T>(IColumnSource<T> x, IColumnSource<T> y, IColumnSource<T> z, string path, CancellationToken cancellation = default)
        where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (x.Count != y.Count || x.Count != z.Count)
        {
            throw new ArgumentException($"X, Y and Z have {x.Count:N0}, {y.Count:N0} and {z.Count:N0} values.", nameof(z));
        }

        var pages = new CloudPages();
        var fingerprint = Fingerprint(x, y, z);
        if (pages.TryOpen(path, x.Count, fingerprint))
        {
            pages.IsFromFile = true;
            pages._isComplete = true;
            return pages;
        }

        pages.Completion = Task.Run(() => pages.Make(x, y, z, path, fingerprint, cancellation), cancellation);
        return pages;
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

    /// <summary>Gets a page: where its points start, how many, and its box with corners from 0 to 1 of the whole.</summary>
    internal ref readonly Page GetPage(int page) => ref _pages[page];

    /// <summary>Gets points of a page, each from 0 to 1 of the whole in every direction.</summary>
    internal ReadOnlySpan<Vector3> Points(int page, int maxCount)
    {
        ref readonly var entry = ref _pages[page];
        return new ReadOnlySpan<Vector3>(_data + entry.Offset, Math.Min(maxCount, entry.Count));
    }

    private void Make<T>(IColumnSource<T> x, IColumnSource<T> y, IColumnSource<T> z, string path, ulong fingerprint, CancellationToken cancellation)
        where T : unmanaged, INumber<T>
    {
        var pieces = (int)((x.Count + PieceSize - 1) / PieceSize);
        Interlocked.Exchange(ref _total, Math.Max(1, pieces * 4L));
        var options = new ParallelOptions { CancellationToken = cancellation };

        // The box of all finite points.
        var boxes = new (Point3D Low, Point3D High, long Count)[pieces];
        Parallel.For(0, pieces, options, piece =>
        {
            var (low, high, count) = (new Point3D(double.MaxValue, double.MaxValue, double.MaxValue), new Point3D(double.MinValue, double.MinValue, double.MinValue), 0L);
            ForEach(x, y, z, piece, (px, py, pz) =>
            {
                (low, high) = (new Point3D(Math.Min(low.X, px), Math.Min(low.Y, py), Math.Min(low.Z, pz)), new Point3D(Math.Max(high.X, px), Math.Max(high.Y, py), Math.Max(high.Z, pz)));
                count++;
            });
            boxes[piece] = (low, high, count);
            Interlocked.Increment(ref _done);
        });
        var (lowAll, highAll, total) = (new Point3D(double.MaxValue, double.MaxValue, double.MaxValue), new Point3D(double.MinValue, double.MinValue, double.MinValue), 0L);
        foreach (var box in boxes)
        {
            (lowAll, highAll, total) = (new Point3D(Math.Min(lowAll.X, box.Low.X), Math.Min(lowAll.Y, box.Low.Y), Math.Min(lowAll.Z, box.Low.Z)), new Point3D(Math.Max(highAll.X, box.High.X), Math.Max(highAll.Y, box.High.Y), Math.Max(highAll.Z, box.High.Z)), total + box.Count);
        }

        if (total == 0)
        {
            (lowAll, highAll) = (new Point3D(0, 0, 0), new Point3D(1, 1, 1));
        }

        var place = new Placement(lowAll, highAll);

        // How many points fall into each cell of a grid of 256 cells per side.
        var cells = new uint[Side * Side * Side];
        Parallel.For(0, pieces, options, piece =>
        {
            ForEach(x, y, z, piece, (px, py, pz) => Interlocked.Increment(ref cells[place.Cell(px, py, pz).Cell]));
            Interlocked.Increment(ref _done);
        });

        // The cells along the curve that passes them in order, with the points before each, and the pages: the nodes of
        // an octree over the cells, each split while it holds more than a page, so a page is a small cube of space; a
        // single cell of more points fills pages of its own.
        var starts = new long[cells.Length];
        var before = new long[cells.Length + 1];
        for (var code = 0; code < cells.Length; code++)
        {
            var cell = Decode(code);
            starts[cell] = before[code];
            before[code + 1] = before[code] + cells[cell];
        }

        var pages = new List<(long First, int Count)>();
        Split(before, 0, cells.Length, pages);
        var pointsStart = HeaderSize + (pages.Count * (long)PageBytes);
        var length = pointsStart + (total * 12);
        var part = path + ".part";
        using (var map = MemoryMappedFile.CreateFromFile(part, FileMode.Create, null, length, MemoryMappedFileAccess.ReadWrite))
        using (var view = map.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite))
        {
            byte* data = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref data);
            try
            {
                data += view.PointerOffset;
                var points = (Vector3*)(data + pointsStart);

                // Each point to the next free place of its cell.
                Parallel.For(0, pieces, options, piece =>
                {
                    ForEach(x, y, z, piece, (px, py, pz) =>
                    {
                        var (cell, inside) = place.Cell(px, py, pz);
                        points[Interlocked.Increment(ref starts[cell]) - 1] = inside;
                    });
                    Interlocked.Increment(ref _done);
                });

                // Each page in a random order, and its box.
                var table = (byte*)(data + HeaderSize);
                Parallel.For(0, pages.Count, options, () => 0L, (index, _, done) =>
                {
                    var (first, count) = pages[index];
                    var page = points + first;
                    var random = new Random(index); // NOSONAR: an order for fair samples, no security
                    var (low, high) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
                    for (var i = count - 1; i >= 0; i--)
                    {
                        var j = random.Next(i + 1);
                        (page[i], page[j]) = (page[j], page[i]);
                        (low, high) = (Vector3.Min(low, page[i]), Vector3.Max(high, page[i]));
                    }

                    var entry = new Span<byte>(table + (index * (long)PageBytes), PageBytes);
                    BinaryPrimitives.WriteInt64LittleEndian(entry, pointsStart + (first * 12));
                    BinaryPrimitives.WriteInt32LittleEndian(entry[8..], count);
                    MemoryMarshal.Write(entry[12..], in low);
                    MemoryMarshal.Write(entry[24..], in high);
                    return done + count;
                }, done => Interlocked.Add(ref _done, done * pieces / Math.Max(1, total)));

                var header = new Span<byte>(data, HeaderSize);
                header.Clear();
                BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
                BinaryPrimitives.WriteInt64LittleEndian(header[8..], x.Count);
                BinaryPrimitives.WriteInt64LittleEndian(header[16..], total);
                BinaryPrimitives.WriteInt32LittleEndian(header[24..], pages.Count);
                BinaryPrimitives.WriteUInt64LittleEndian(header[32..], fingerprint);
                BinaryPrimitives.WriteDoubleLittleEndian(header[40..], lowAll.X);
                BinaryPrimitives.WriteDoubleLittleEndian(header[48..], lowAll.Y);
                BinaryPrimitives.WriteDoubleLittleEndian(header[56..], lowAll.Z);
                BinaryPrimitives.WriteDoubleLittleEndian(header[64..], highAll.X);
                BinaryPrimitives.WriteDoubleLittleEndian(header[72..], highAll.Y);
                BinaryPrimitives.WriteDoubleLittleEndian(header[80..], highAll.Z);
                view.Flush();
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        // A kept file still open elsewhere cannot be replaced; the new pages are then read from where they were built.
        var built = path;
        try
        {
            File.Move(part, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            built = part;
        }

        if (!TryOpen(built, x.Count, fingerprint))
        {
            throw new IOException($"The pages written to {path} cannot be read back.");
        }

        _isComplete = true;
    }

    // A node of the octree as a range of codes: a page when it holds few enough points, else its eight children.
    private static void Split(long[] before, int start, int size, List<(long First, int Count)> pages)
    {
        var count = before[start + size] - before[start];
        if (count == 0)
        {
            return;
        }

        if (count <= PageSize || size == 1)
        {
            for (var first = before[start]; first < before[start + size]; first += PageSize)
            {
                pages.Add((first, (int)Math.Min(PageSize, before[start + size] - first)));
            }

            return;
        }

        var child = size / 8;
        for (var i = 0; i < 8; i++)
        {
            Split(before, start + (i * child), child, pages);
        }
    }

    private bool TryOpen(string path, long sourceCount, ulong fingerprint)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < HeaderSize)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        var count = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
        var pageCount = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != FormatVersion
            || BinaryPrimitives.ReadInt64LittleEndian(header[8..]) != sourceCount || BinaryPrimitives.ReadUInt64LittleEndian(header[32..]) != fingerprint
            || new FileInfo(path).Length < HeaderSize + (pageCount * (long)PageBytes) + (count * 12))
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
        Low = new Point3D(BinaryPrimitives.ReadDoubleLittleEndian(header[40..]), BinaryPrimitives.ReadDoubleLittleEndian(header[48..]), BinaryPrimitives.ReadDoubleLittleEndian(header[56..]));
        High = new Point3D(BinaryPrimitives.ReadDoubleLittleEndian(header[64..]), BinaryPrimitives.ReadDoubleLittleEndian(header[72..]), BinaryPrimitives.ReadDoubleLittleEndian(header[80..]));
        var pages = new Page[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            var entry = new ReadOnlySpan<byte>(data + HeaderSize + (i * (long)PageBytes), PageBytes);
            pages[i] = new Page(BinaryPrimitives.ReadInt64LittleEndian(entry), BinaryPrimitives.ReadInt32LittleEndian(entry[8..]), MemoryMarshal.Read<Vector3>(entry[12..]), MemoryMarshal.Read<Vector3>(entry[24..]));
        }

        _pages = pages;
        return true;
    }

    // Reads a piece of the three columns and hands on each point with finite coordinates.
    private static void ForEach<T>(IColumnSource<T> x, IColumnSource<T> y, IColumnSource<T> z, int piece, Action<double, double, double> take)
        where T : unmanaged, INumber<T>
    {
        var start = (long)piece * PieceSize;
        var end = Math.Min(x.Count, start + PieceSize);
        for (var at = start; at < end;)
        {
            var xs = x.Read(at, (int)(end - at));
            var ys = y.Read(at, xs.Length);
            var zs = z.Read(at, Math.Min(xs.Length, ys.Length));
            var length = Math.Min(Math.Min(xs.Length, ys.Length), zs.Length);
            if (length == 0)
            {
                break;
            }

            for (var i = 0; i < length; i++)
            {
                var (px, py, pz) = (double.CreateTruncating(xs[i]), double.CreateTruncating(ys[i]), double.CreateTruncating(zs[i]));
                if (double.IsFinite(px) && double.IsFinite(py) && double.IsFinite(pz))
                {
                    take(px, py, pz);
                }
            }

            at += length;
        }
    }

    // The cell of the code along the curve: every third bit of the code belongs to one direction.
    private static int Decode(int code)
    {
        int cellX = 0, cellY = 0, cellZ = 0;
        for (var bit = 0; bit < Bits; bit++)
        {
            cellX |= ((code >> (3 * bit)) & 1) << bit;
            cellY |= ((code >> ((3 * bit) + 1)) & 1) << bit;
            cellZ |= ((code >> ((3 * bit) + 2)) & 1) << bit;
        }

        return (((cellZ * Side) + cellY) * Side) + cellX;
    }

    private static ulong Fingerprint<T>(IColumnSource<T> x, IColumnSource<T> y, IColumnSource<T> z)
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
        foreach (var column in (IColumnSource<T>[])[x, y, z])
        {
            for (var sample = 0; sample < 64 && column.Count > 0; sample++)
            {
                var start = column.Count * sample / 64;
                Add(MemoryMarshal.AsBytes(column.Read(start, (int)Math.Min(64, column.Count - start))));
            }
        }

        return hash;
    }

    /// <summary>A page: where its points start in the file, how many, and its box from 0 to 1 of the whole.</summary>
    internal readonly record struct Page(long Offset, int Count, Vector3 Low, Vector3 High);

    // Where a point lies: its cell, and its place from 0 to 1 of the whole.
    private readonly struct Placement(Point3D low, Point3D high)
    {
        private readonly Point3D _scale = new(1 / Span(low.X, high.X), 1 / Span(low.Y, high.Y), 1 / Span(low.Z, high.Z));

        public (int Cell, Vector3 Inside) Cell(double x, double y, double z)
        {
            var (u, v, w) = ((x - low.X) * _scale.X, (y - low.Y) * _scale.Y, (z - low.Z) * _scale.Z);
            var (cx, cy, cz) = (Math.Clamp((int)(u * Side), 0, Side - 1), Math.Clamp((int)(v * Side), 0, Side - 1), Math.Clamp((int)(w * Side), 0, Side - 1));
            return ((((cz * Side) + cy) * Side) + cx, new Vector3((float)u, (float)v, (float)w));
        }

        private static double Span(double low, double high) => high > low ? high - low : 1;
    }
}
