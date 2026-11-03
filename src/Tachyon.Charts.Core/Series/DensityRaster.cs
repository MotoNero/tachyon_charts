using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// Counts points per pixel of the plot and shows the counts on a color scale.
/// </summary>
internal sealed class DensityRaster : IDisposable
{
    private readonly ColormapTexture _table = new();
    private IRenderDevice? _device;
    private IGpuBuffer? _gpuPoints;
    private IGpuTexture? _texture;
    private IGpuTexture? _colors;
    private Vector2[] _points = [];
    private float[] _bins = [];
    private int _count;
    private long _pointVersion = -1;
    private long _binnedVersion = -1;
    private SeriesFrame _binnedFrame;

    /// <summary>Gets the highest count of the last binning.</summary>
    public long Maximum { get; private set; }

    /// <summary>Gets the number of points.</summary>
    public int Count => _count;

    /// <summary>Gets a value indicating whether the points were taken over for this version.</summary>
    /// <param name="version">The version of the data.</param>
    /// <returns>Whether they are current.</returns>
    public bool HasPoints(long version) => _pointVersion == version;

    /// <summary>Gets a buffer for the points in the linear space of the axes.</summary>
    /// <param name="capacity">The number of points.</param>
    /// <returns>The buffer.</returns>
    public Span<Vector2> RentPoints(int capacity)
    {
        if (_points.Length < capacity)
        {
            _points = new Vector2[capacity];
        }

        return _points;
    }

    /// <summary>Takes over the points of the buffer.</summary>
    /// <param name="device">The device.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="version">The version of the data.</param>
    public void CommitPoints(IRenderDevice device, int count, long version)
    {
        _count = count;
        _pointVersion = version;
        if (device.Compute is null || count == 0)
        {
            return;
        }

        var size = (long)count * 8;
        if (_gpuPoints is null || _gpuPoints.SizeInBytes < size)
        {
            _gpuPoints?.Dispose();
            _gpuPoints = device.CreateBuffer(new BufferDesc(size, BufferUsage.Storage));
        }

        _gpuPoints.Write<Vector2>(0, _points.AsSpan(0, count));

        // The GPU has them now, and a large cloud should not stay in memory twice.
        _points = [];
    }

    /// <summary>Counts the points again if the data or the view changed.</summary>
    /// <param name="device">The device.</param>
    /// <param name="frame">The place of the series.</param>
    public void Update(IRenderDevice device, in SeriesFrame frame)
    {
        if (_device != device)
        {
            _texture?.Dispose();
            _texture = null;
            _device = device;
        }

        var size = new PixelSize(Math.Max((int)frame.Bounds.Width, 1), Math.Max((int)frame.Bounds.Height, 1));
        if (_texture is null || _texture.Size != size)
        {
            _texture?.Dispose();
            _texture = device.CreateTexture(new TextureDesc(size, TextureFormat.R32Float));
            _binnedVersion = -1;
        }

        if (_binnedVersion == _pointVersion && _binnedFrame == frame)
        {
            return;
        }

        var scale = new Vector2((float)frame.X.PixelsPerUnit, (float)frame.Y.PixelsPerUnit);
        var offset = new Vector2((float)(frame.X.ToPixelLinear(0) - frame.Bounds.X), (float)(frame.Y.ToPixelLinear(0) - frame.Bounds.Y));
        if (device.Compute is { } compute && _gpuPoints is not null)
        {
            Maximum = compute.BinPoints(_gpuPoints, _count, scale, offset, _texture);
        }
        else
        {
            Maximum = Bin(_points.AsSpan(0, _count), scale, offset, size, ref _bins);
            _texture.Write(MemoryMarshal.AsBytes(_bins.AsSpan()));
        }

        _binnedVersion = _pointVersion;
        _binnedFrame = frame;
    }

    /// <summary>Brings the table up to date.</summary>
    /// <param name="device">The device.</param>
    /// <param name="axis">The value scale.</param>
    public void UpdateTable(IRenderDevice device, ColorAxis axis) => _colors = _table.Update(device, axis.Colormap);

    /// <summary>Draws the counts.</summary>
    /// <param name="commands">The open frame.</param>
    /// <param name="frame">The place of the series.</param>
    /// <param name="axis">The value scale.</param>
    public void Draw(ICommandList commands, in SeriesFrame frame, ColorAxis axis)
    {
        if (_texture is not null && _colors is not null)
        {
            commands.DrawRaster(_texture, _colors, frame.Bounds, axis.CreateRasterStyle());
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _table.Dispose();
        _gpuPoints?.Dispose();
        _texture?.Dispose();
        _gpuPoints = null;
        _texture = null;
        _colors = null;
        _device = null;
        _pointVersion = -1;
        _binnedVersion = -1;
    }

    /// <summary>Counts points on the CPU, with the rules of density.wgsl.</summary>
    /// <param name="points">The points.</param>
    /// <param name="scale">The factors to pixels.</param>
    /// <param name="offset">The pixel of the position zero.</param>
    /// <param name="size">The size of the raster.</param>
    /// <param name="bins">Receives the counts.</param>
    /// <returns>The highest count.</returns>
    internal static long Bin(ReadOnlySpan<Vector2> points, Vector2 scale, Vector2 offset, PixelSize size, ref float[] bins)
    {
        var length = size.Width * size.Height;
        if (bins.Length != length)
        {
            bins = new float[length];
        }
        else
        {
            Array.Clear(bins);
        }

        var maximum = 0f;
        foreach (var point in points)
        {
            var pixel = (point * scale) + offset;
            if (pixel.X >= 0 && pixel.Y >= 0 && pixel.X < size.Width && pixel.Y < size.Height)
            {
                ref var bin = ref bins[((int)pixel.Y * size.Width) + (int)pixel.X];
                bin++;
                maximum = Math.Max(maximum, bin);
            }
        }

        return (long)maximum;
    }
}
