using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Geo;

/// <summary>
/// Tiles of a server over a map on the plane of <see cref="GeoProjection.WebMercator"/>. Tiles load in the background
/// while the map stays usable; until a tile arrives, a coarser one that is there stands in for it, and where none is, the
/// map below shows through, such as the countries of <see cref="WorldMap"/> without a network.
/// </summary>
public sealed class TileLayerSeries : ChartSeries, IDisposable
{
    private const int Workers = 6;
    private const int Coarser = 6;
    private static readonly TimeSpan _retry = TimeSpan.FromSeconds(30);

    private readonly TileSource _source;
    private readonly ConcurrentDictionary<(int Z, int X, int Y), TileImage> _tiles = new();
    private readonly ConcurrentDictionary<(int Z, int X, int Y), long> _failed = new();
    private readonly ConcurrentDictionary<(int Z, int X, int Y), byte> _pending = new();
    private readonly ConcurrentStack<(int Z, int X, int Y)> _wanted = new();
    private readonly ConcurrentQueue<(int Z, int X, int Y)> _order = new();
    private readonly CancellationTokenSource _stop = new();

    // Taken once, so that workers still running after Dispose meet a cancelled token, not a disposed source.
    private readonly CancellationToken _token;
    private readonly Action<int> _fillBand;
    private IGpuTexture? _texture;
    private IRenderDevice? _device;
    private Rgba8[] _pixels = [];
    private long _arrived;
    private long _built = -1;
    private SeriesFrame _builtFrame;
    private int _running;
    private (int Zoom, int Width, int Height, double X0, double Y0, double Dx, double Dy, int Bands) _job;

    /// <summary>Starts a layer of tiles.</summary>
    /// <param name="source">Where the tiles come from.</param>
    /// <param name="xAxis">The X axis, on the plane of <see cref="GeoProjection.WebMercator"/>.</param>
    /// <param name="yAxis">The Y axis, on the same plane.</param>
    public TileLayerSeries(TileSource source, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _token = _stop.Token;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _fillBand = FillBand;
    }

    /// <summary>Gets where the tiles come from.</summary>
    public TileSource Source => _source;

    /// <summary>Gets the level of tiles of the last frame.</summary>
    public int Zoom => _job.Zoom;

    /// <summary>Gets the picture of the last frame, row by row from the top, for tests.</summary>
    internal ReadOnlySpan<Rgba8> Pixels => _pixels;

    /// <summary>Gets the number of tiles in memory.</summary>
    public int TileCount => _tiles.Count;

    /// <inheritdoc />
    protected override long Version => Interlocked.Read(ref _arrived);

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _texture?.Dispose();
        (_texture, _device) = (null, null);
    }

    /// <inheritdoc />
    protected override void ReleaseResources()
    {
        _texture?.Dispose();
        (_texture, _device) = (null, null);
        _built = -1;
    }

    /// <inheritdoc />
    protected override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        (x, y) = (new DoubleRange(-Math.PI, Math.PI), new DoubleRange(-Math.PI, Math.PI));
        return true;
    }

    /// <inheritdoc />
    protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var (width, height) = (Math.Max((int)frame.Bounds.Width, 1), Math.Max((int)frame.Bounds.Height, 1));
        if (_device != device || _texture is null || _texture.Size != new PixelSize(width, height))
        {
            _texture?.Dispose();
            _texture = device.CreateTexture(new TextureDesc(new PixelSize(width, height), TextureFormat.Rgba8Unorm));
            (_device, _built) = (device, -1);
        }

        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        (_built, _builtFrame) = (version, frame);
        if (_pixels.Length != width * height)
        {
            _pixels = new Rgba8[width * height];
        }

        // The level whose tiles come closest to 256 pixels; a tile spans 2π / 2^z of the plane.
        var unitsPerPixel = Math.Abs(frame.X.ToData(frame.Bounds.X + 1) - frame.X.ToData(frame.Bounds.X));
        var zoom = Math.Clamp((int)Math.Ceiling(Math.Log2(2 * Math.PI / (256 * unitsPerPixel))), 0, _source.MaxZoom);
        var x0 = frame.X.ToData(frame.Bounds.X + 0.5);
        var y0 = frame.Y.ToData(frame.Bounds.Y + 0.5);
        var dx = frame.X.ToData(frame.Bounds.X + 1.5) - x0;
        var dy = frame.Y.ToData(frame.Bounds.Y + 1.5) - y0;
        _job = (zoom, width, height, x0, y0, dx, dy, Math.Clamp(Environment.ProcessorCount, 1, 8));
        Want(zoom, x0, y0, x0 + (dx * width), y0 + (dy * height));
        Parallel.For(0, _job.Bands, _fillBand);
        _texture.Write(MemoryMarshal.AsBytes(_pixels.AsSpan()));
    }

    /// <inheritdoc />
    protected override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is not null)
        {
            commands.DrawImage(_texture, frame.Bounds);
        }
    }

    // The tiles of the view at its level, asked for when they are neither here, on their way, nor failed a short while ago.
    private void Want(int zoom, double left, double top, double right, double bottom)
    {
        var count = 1 << zoom;
        var (tx0, tx1) = (Column(Math.Min(left, right), zoom), Column(Math.Max(left, right), zoom));
        var (ty0, ty1) = (Row(Math.Max(top, bottom), zoom), Row(Math.Min(top, bottom), zoom));
        var now = Environment.TickCount64;
        for (var ty = Math.Max(0, ty0); ty <= Math.Min(count - 1, ty1); ty++)
        {
            for (var tx = tx0; tx <= tx1; tx++)
            {
                var key = (zoom, ((tx % count) + count) % count, ty);
                if (_tiles.ContainsKey(key) || _pending.ContainsKey(key) || (_failed.TryGetValue(key, out var failedAt) && now - failedAt < _retry.TotalMilliseconds))
                {
                    continue;
                }

                _pending[key] = 0;
                _wanted.Push(key);
            }
        }

        while (Volatile.Read(ref _running) < Workers && !_wanted.IsEmpty)
        {
            Interlocked.Increment(ref _running);
            _ = Task.Run(Work, _token);
        }
    }

    // A worker takes the newest wish first, so that the tiles of the current view come before older ones.
    private async Task Work()
    {
        try
        {
            while (!_token.IsCancellationRequested && _wanted.TryPop(out var key))
            {
                try
                {
                    if (await _source.LoadAsync(key.Z, key.X, key.Y, _token).ConfigureAwait(false) is { } image)
                    {
                        Keep(key, image);
                    }
                    else
                    {
                        _failed[key] = Environment.TickCount64;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
                {
                    // Without a network the map below shows; the tile is asked for again after a while.
                    _failed[key] = Environment.TickCount64;
                }
                finally
                {
                    _pending.TryRemove(key, out _);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private void Keep((int Z, int X, int Y) key, TileImage image)
    {
        _tiles[key] = image;
        _order.Enqueue(key);
        while (_tiles.Count > _source.MemoryTiles && _order.TryDequeue(out var oldest))
        {
            _tiles.TryRemove(oldest, out _);
        }

        Interlocked.Increment(ref _arrived);
    }

    // Each pixel takes the finest tile there is at its place, from the level of the view up to six coarser ones.
    private void FillBand(int band)
    {
        var (zoom, width, height, x0, y0, dx, dy, bands) = _job;
        for (var row = height * band / bands; row < height * (band + 1) / bands; row++)
        {
            var y = y0 + (dy * row);
            var line = _pixels.AsSpan(row * width, width);
            if (Math.Abs(y) > Math.PI)
            {
                line.Clear();
                continue;
            }

            var ty = (Math.PI - y) / (2 * Math.PI);
            for (var column = 0; column < width; column++)
            {
                var tx = ((x0 + (dx * column)) + Math.PI) / (2 * Math.PI);
                tx -= Math.Floor(tx);
                line[column] = Sample(zoom, tx, ty);
            }
        }
    }

    private Rgba8 Sample(int zoom, double tx, double ty)
    {
        for (var z = zoom; z >= Math.Max(0, zoom - Coarser); z--)
        {
            var count = 1 << z;
            var (fx, fy) = (tx * count, ty * count);
            var (x, y) = ((int)fx, (int)fy);
            if (y < 0 || y >= count || !_tiles.TryGetValue((z, Math.Min(x, count - 1), y), out var tile))
            {
                continue;
            }

            var px = Math.Min(tile.Width - 1, (int)((fx - x) * tile.Width));
            var py = Math.Min(tile.Height - 1, (int)((fy - y) * tile.Height));
            return tile.Pixels[(py * tile.Width) + px];
        }

        return default;
    }

    private static int Column(double x, int zoom) => (int)Math.Floor((x + Math.PI) / (2 * Math.PI) * (1 << zoom));

    private static int Row(double y, int zoom) => (int)Math.Floor((Math.PI - y) / (2 * Math.PI) * (1 << zoom));
}

/// <summary>The notice of the source of a map, in the lower right corner of the plot, as tile servers ask for it.</summary>
/// <param name="text">The notice.</param>
public sealed class AttributionOverlay(string text) : ChartOverlay
{
    private const double Margin = 4;

    /// <summary>Gets the notice.</summary>
    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var plot = canvas.Layout.PlotArea;
        var width = canvas.MeasureWidth(Text, canvas.LabelSize) + 8;
        var height = canvas.GetLineHeight(canvas.LabelSize) + 8;
        canvas.DrawLabel(Text, plot.Right - width - Margin, plot.Bottom - height - Margin, canvas.BackgroundColor with { A = 200 }, canvas.TextColor);
    }
}
