using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A scatter of points far larger than memory as a density map: each pixel shows how many points fall into it. Wide
/// views add up the count grid of the tiles; narrow ones count the points of the visible tiles, at most
/// <see cref="PointBudget"/> per frame: a dense tile gives a fair share of its points, weighted to its full count, and a
/// sparse one all of them, so that a single point far out stays visible.
/// </summary>
public sealed class PointDensitySeries : ChartSeries, IDisposable, Overlays.IRefiningSeries
{
    private readonly PointTiles _tiles;
    private readonly ColorAxis _colorAxis;
    private readonly ColormapTexture _table = new();
    private readonly Action<int> _countPart;
    private IGpuTexture? _texture;
    private IGpuTexture? _colors;
    private IRenderDevice? _device;
    private float[] _bins = [];
    private float[] _bandMaxima = [];
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private long _appearanceVersion;
    private int _pointBudget = 4_000_000;
    private int _drawn;
    private double _maximum;
    private double _scaledMaximum;

    // The work of a frame, shared by the parts that count in parallel.
    private Job _job;

    /// <summary>Starts a series.</summary>
    /// <param name="tiles">The points.</param>
    /// <param name="xAxis">The X axis.</param>
    /// <param name="yAxis">The Y axis.</param>
    /// <param name="colorAxis">The scale of the counts.</param>
    public PointDensitySeries(PointTiles tiles, AxisBase xAxis, AxisBase yAxis, ColorAxis colorAxis)
        : base(xAxis, yAxis)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _colorAxis = colorAxis ?? throw new ArgumentNullException(nameof(colorAxis));
        _countPart = CountPart;
    }

    /// <summary>Gets or sets the most points read from the tiles per frame; more make narrow views finer and slower.</summary>
    public int PointBudget
    {
        get => _pointBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1000);
            _pointBudget = value;
            Interlocked.Increment(ref _appearanceVersion);
        }
    }

    /// <summary>Gets the counts of the last frame, row by row from the top, for tests.</summary>
    internal ReadOnlySpan<float> Bins => _bins;

    /// <summary>Gets the highest count of a pixel in the last frame.</summary>
    public double Maximum => _maximum;

    /// <inheritdoc />
    public double Progress => _tiles.Progress;

    /// <inheritdoc />
    public override ColorAxis? ColorAxis => _colorAxis;

    /// <inheritdoc />
    protected internal override long Version => (_tiles.IsComplete ? 1L << 40 : (long)(_tiles.Progress * 1000)) + Volatile.Read(ref _appearanceVersion);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _drawn;

    /// <inheritdoc />
    protected internal override long SourcePointCount => _tiles.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        _table.Dispose();
        _texture?.Dispose();
        (_texture, _colors, _device) = (null, null, null);
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources()
    {
        Dispose();
        _builtVersion = -1;
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        (x, y) = (_tiles.XRange, _tiles.YRange);
        return _tiles.IsComplete && _tiles.Count > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryGetValueRange(out DoubleRange range)
    {
        // Pixels without a point stay below the range, which lets the plot show through.
        range = new DoubleRange(1, Math.Max(_maximum, 2));
        return _maximum > 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        if (_device != device)
        {
            _texture?.Dispose();
            (_texture, _device) = (null, device);
        }

        var width = Math.Max((int)frame.Bounds.Width, 1);
        var height = Math.Max((int)frame.Bounds.Height, 1);
        var size = new PixelSize(width, height);
        if (_texture is null || _texture.Size != size)
        {
            _texture?.Dispose();
            _texture = device.CreateTexture(new TextureDesc(size, TextureFormat.R32Float));
            _builtVersion = -1;
        }

        _colors = _table.Update(device, _colorAxis.Colormap);
        var version = Version;
        if (version == _builtVersion && frame == _builtFrame)
        {
            return;
        }

        (_builtVersion, _builtFrame) = (version, frame);
        if (_bins.Length != width * height)
        {
            _bins = new float[width * height];
        }

        if (_tiles.IsComplete && _tiles.Count > 0 && frame.X.IsLinear && frame.Y.IsLinear)
        {
            Count(frame, width, height);
        }
        else
        {
            Array.Clear(_bins);
            (_maximum, _drawn) = (0, 0);
        }

        _texture.Write(MemoryMarshal.AsBytes(_bins.AsSpan()));

        // The scale of the counts took the highest count of the frame before; a new one asks for one more frame, which
        // draws the same counts on the new scale without counting them again.
        if (_maximum != _scaledMaximum)
        {
            _scaledMaximum = _maximum;
            Interlocked.Increment(ref _appearanceVersion);
            _builtVersion = Version;
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is not null && _colors is not null && _maximum > 0)
        {
            commands.DrawRaster(_texture, _colors, frame.Bounds, _colorAxis.CreateRasterStyle());
        }
    }

    // Chooses the grid or the points, counts in parts on all cores, and adds the parts up.
    private void Count(in SeriesFrame frame, int width, int height)
    {
        var (xRange, yRange) = (_tiles.XRange, _tiles.YRange);
        var spanX = xRange.Length > 0 ? xRange.Length : 1;
        var spanY = yRange.Length > 0 ? yRange.Length : 1;
        var origin = XAxis is DateTimeAxis time ? time.OriginTicks : 0;

        // The view in data, and the part of the extent it covers.
        var (left, right) = Order(frame.X.ToData(frame.Bounds.X) + origin, frame.X.ToData(frame.Bounds.X + frame.Bounds.Width) + origin);
        var (bottom, top) = Order(frame.Y.ToData(frame.Bounds.Y + frame.Bounds.Height), frame.Y.ToData(frame.Bounds.Y));
        var (shareX, shareY) = ((right - left) / spanX, (top - bottom) / spanY);
        var parts = Math.Clamp(Environment.ProcessorCount, 1, 8);
        // The finest level of the grid that still has a cell or more per pixel.
        var level = -1;
        for (var candidate = _tiles.GridLevels - 1; candidate >= 0; candidate--)
        {
            var cells = PointTiles.GridSize >> candidate;
            if (cells * shareX >= width && cells * shareY >= height)
            {
                level = candidate;
                break;
            }
        }

        _job = new Job
        {
            Frame = frame,
            Width = width,
            Height = height,
            Origin = origin,
            Parts = parts,
            Level = level,
            Left = left,
            Right = right,
            Bottom = bottom,
            Top = top,
        };
        if (level < 0)
        {
            PlanPoints(spanX, spanY);
        }

        if (_bandMaxima.Length != parts)
        {
            _bandMaxima = new float[parts];
        }

        Array.Clear(_bandMaxima);
        Parallel.For(0, _job.Parts, _countPart);
        var maximum = 0f;
        foreach (var band in _bandMaxima)
        {
            maximum = Math.Max(maximum, band);
        }

        _maximum = maximum;
        _drawn = level >= 0 ? 0 : (int)Math.Min(int.MaxValue, _job.Planned);
    }

    // The visible tiles and how many points of each to read: all of a sparse tile, a share of a dense one, within the budget.
    private void PlanPoints(double spanX, double spanY)
    {
        ref var job = ref _job;
        var (xRange, yRange) = (_tiles.XRange, _tiles.YRange);
        job.TileX0 = Math.Clamp((int)Math.Floor((job.Left - xRange.Min) / spanX * PointTiles.TilesPerSide), 0, PointTiles.TilesPerSide - 1);
        job.TileX1 = Math.Clamp((int)Math.Floor((job.Right - xRange.Min) / spanX * PointTiles.TilesPerSide), 0, PointTiles.TilesPerSide - 1);
        job.TileY0 = Math.Clamp((int)Math.Floor((job.Bottom - yRange.Min) / spanY * PointTiles.TilesPerSide), 0, PointTiles.TilesPerSide - 1);
        job.TileY1 = Math.Clamp((int)Math.Floor((job.Top - yRange.Min) / spanY * PointTiles.TilesPerSide), 0, PointTiles.TilesPerSide - 1);
        long total = 0, largest = 0;
        for (var ty = job.TileY0; ty <= job.TileY1; ty++)
        {
            for (var tx = job.TileX0; tx <= job.TileX1; tx++)
            {
                var count = _tiles.TileCount(tx, ty);
                total += count;
                largest = Math.Max(largest, count);
            }
        }

        if (total <= _pointBudget)
        {
            (job.Cap, job.Planned) = (largest, total);
            return;
        }

        // The highest share per tile that keeps the sum within the budget.
        long low = 1, high = largest;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            long sum = 0;
            for (var ty = job.TileY0; ty <= job.TileY1 && sum <= _pointBudget; ty++)
            {
                for (var tx = job.TileX0; tx <= job.TileX1; tx++)
                {
                    sum += Math.Min(middle, _tiles.TileCount(tx, ty));
                }
            }

            if (sum <= _pointBudget)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        job.Cap = low;
        job.Planned = 0;
        for (var ty = job.TileY0; ty <= job.TileY1; ty++)
        {
            for (var tx = job.TileX0; tx <= job.TileX1; tx++)
            {
                job.Planned += Math.Min(low, _tiles.TileCount(tx, ty));
            }
        }
    }

    // Each part counts a band of pixel rows straight into the result, so no part writes where another does.
    private void CountPart(int part)
    {
        var start = (int)((long)_job.Height * part / _job.Parts);
        var end = (int)((long)_job.Height * (part + 1) / _job.Parts);
        var band = _bins.AsSpan(start * _job.Width, (end - start) * _job.Width);
        band.Clear();
        if (_job.Level >= 0)
        {
            CountGrid(start, end, _bins);
        }
        else
        {
            CountPoints(start, end, _bins);
        }

        _bandMaxima[part] = Highest(band);
    }

    // The cells of a level, each shared out over the pixels it covers by area: a cell is at most a pixel wide, so it
    // touches at most two in each direction. A part writes only the shares that fall into its band of pixel rows.
    private void CountGrid(int firstRow, int endRow, float[] bins)
    {
        ref readonly var job = ref _job;
        var (xRange, yRange) = (_tiles.XRange, _tiles.YRange);
        var size = PointTiles.GridSize >> job.Level;
        var grid = _tiles.Grid(job.Level);
        var cellWidth = (xRange.Length > 0 ? xRange.Length : 1) / size;
        var cellHeight = (yRange.Length > 0 ? yRange.Length : 1) / size;
        var column0 = Math.Clamp((int)Math.Floor((job.Left - xRange.Min) / cellWidth), 0, size - 1);
        var column1 = Math.Clamp((int)Math.Ceiling((job.Right - xRange.Min) / cellWidth), 0, size - 1);
        var row0 = Math.Clamp((int)Math.Floor((job.Bottom - yRange.Min) / cellHeight), 0, size - 1);
        var row1 = Math.Clamp((int)Math.Ceiling((job.Top - yRange.Min) / cellHeight), 0, size - 1);
        var frame = job.Frame;
        var x0 = frame.X.ToPixel(xRange.Min - job.Origin) - frame.Bounds.X;
        var dx = frame.X.ToPixel(xRange.Min + cellWidth - job.Origin) - frame.Bounds.X - x0;
        var y0 = frame.Y.ToPixel(yRange.Min) - frame.Bounds.Y;
        var dy = frame.Y.ToPixel(yRange.Min + cellHeight) - frame.Bounds.Y - y0;
        var (width, height) = (Math.Abs(dx), Math.Abs(dy));
        for (var row = row0; row <= row1; row++)
        {
            // The pixel rows the cell row covers, and the share of each.
            var top = Math.Min(y0 + (row * dy), y0 + ((row + 1) * dy));
            var upper = (int)Math.Floor(top);
            var upperShare = Math.Min(1, (upper + 1 - top) / height);
            var takesUpper = upper >= firstRow && upper < endRow;
            var takesLower = upperShare < 1 && upper + 1 >= firstRow && upper + 1 < endRow;
            if (!takesUpper && !takesLower)
            {
                continue;
            }

            var line = grid.Slice(row * size, size);
            for (var column = column0; column <= column1; column++)
            {
                var count = line[column];
                if (count == 0)
                {
                    continue;
                }

                var left = Math.Min(x0 + (column * dx), x0 + ((column + 1) * dx));
                var first = (int)Math.Floor(left);
                var firstShare = Math.Min(1, (first + 1 - left) / width);
                if (takesUpper)
                {
                    Add(bins, job.Width, upper, first, firstShare, count * upperShare);
                }

                if (takesLower)
                {
                    Add(bins, job.Width, upper + 1, first, firstShare, count * (1 - upperShare));
                }
            }
        }
    }

    // Adds a count to a pixel and, by its share, to the one right of it.
    private static void Add(float[] bins, int width, int row, int column, double share, double count)
    {
        var offset = row * width;
        if (column >= 0 && column < width)
        {
            bins[offset + column] += (float)(count * share);
        }

        if (share < 1 && column + 1 >= 0 && column + 1 < width)
        {
            bins[offset + column + 1] += (float)(count * (1 - share));
        }
    }

    // The points of the visible tiles that fall into a band of pixel rows; tiles beside the band are passed over.
    private void CountPoints(int firstRow, int endRow, float[] bins)
    {
        ref readonly var job = ref _job;
        var (xRange, yRange) = (_tiles.XRange, _tiles.YRange);
        var tileWidth = (xRange.Length > 0 ? xRange.Length : 1) / PointTiles.TilesPerSide;
        var tileHeight = (yRange.Length > 0 ? yRange.Length : 1) / PointTiles.TilesPerSide;
        var frame = job.Frame;
        for (var ty = job.TileY0; ty <= job.TileY1; ty++)
        {
            // Inside a tile a point maps to a pixel along a straight line in each direction.
            var bottom = yRange.Min + (ty * tileHeight);
            var ay = (float)(frame.Y.ToPixel(bottom) - frame.Bounds.Y);
            var by = (float)(frame.Y.ToPixel(bottom + tileHeight) - frame.Bounds.Y) - ay;
            if (Math.Max(ay, ay + by) < firstRow || Math.Min(ay, ay + by) >= endRow)
            {
                continue;
            }

            for (var tx = job.TileX0; tx <= job.TileX1; tx++)
            {
                var count = _tiles.TileCount(tx, ty);
                if (count == 0)
                {
                    continue;
                }

                var points = _tiles.TilePoints(tx, ty, (int)Math.Min(count, job.Cap));
                var weight = (float)((double)count / points.Length);
                var left = xRange.Min + (tx * tileWidth) - job.Origin;
                var ax = (float)(frame.X.ToPixel(left) - frame.Bounds.X);
                var bx = (float)(frame.X.ToPixel(left + tileWidth) - frame.Bounds.X) - ax;
                foreach (var point in points)
                {
                    var pixelY = ay + (point.Y * by);
                    var pixelX = ax + (point.X * bx);
                    if (pixelY >= firstRow && pixelY < endRow && pixelX >= 0 && pixelX < job.Width)
                    {
                        bins[((int)pixelY * job.Width) + (int)pixelX] += weight;
                    }
                }
            }
        }
    }

    private static float Highest(ReadOnlySpan<float> values)
    {
        var highest = Vector<float>.Zero;
        var i = 0;
        for (; i <= values.Length - Vector<float>.Count; i += Vector<float>.Count)
        {
            highest = Vector.Max(highest, new Vector<float>(values[i..]));
        }

        var result = 0f;
        for (var lane = 0; lane < Vector<float>.Count; lane++)
        {
            result = Math.Max(result, highest[lane]);
        }

        for (; i < values.Length; i++)
        {
            result = Math.Max(result, values[i]);
        }

        return result;
    }

    private static (double Low, double High) Order(double a, double b) => a <= b ? (a, b) : (b, a);

    private struct Job
    {
        public SeriesFrame Frame;
        public int Width;
        public int Height;
        public double Origin;
        public int Parts;
        public int Level;
        public double Left;
        public double Right;
        public double Bottom;
        public double Top;
        public int TileX0;
        public int TileX1;
        public int TileY0;
        public int TileY1;
        public long Cap;
        public long Planned;
    }
}
