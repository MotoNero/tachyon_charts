using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// A render device without output. It enforces the rules of the render API, which makes it the device
/// for testing code above the render abstraction. Read-back pixels are always transparent black.
/// </summary>
public sealed class NullRenderDevice : IRenderDevice
{
    private bool _isLost;

    private bool _disposed;
    private CommandList? _openFrame;
    private CommandList? _freeFrame;

    /// <summary>
    /// Initializes a new instance of the <see cref="NullRenderDevice"/> class.
    /// </summary>
    /// <param name="capabilities">The capabilities to report, or <see langword="null"/> for the defaults.</param>
    public NullRenderDevice(DeviceCapabilities? capabilities = null)
    {
        Capabilities = capabilities ?? new DeviceCapabilities
        {
            BackendName = "Null",
            SupportsCompute = false,
            MaxBufferSize = 1L << 30,
            MaxTextureDimension = 16384,
            MaxSampleCount = 1,
        };
    }

    /// <inheritdoc />
    public DeviceCapabilities Capabilities { get; }

    /// <summary>
    /// Gets the number of created resources and frames that are not disposed yet.
    /// </summary>
    public int LiveResourceCount { get; private set; }

    /// <summary>
    /// Gets the number of frames submitted so far.
    /// </summary>
    public int SubmittedFrameCount { get; private set; }

    /// <inheritdoc />
    public long DrawCallCount { get; private set; }

    /// <inheritdoc />
    public long UploadedByteCount { get; private set; }

    private void ThrowIfLost()
    {
        if (IsLost)
        {
            throw new DeviceLostException();
        }
    }

    /// <inheritdoc />
    public IComputeOperations? Compute => null;

    /// <inheritdoc />
    public bool IsLost => Volatile.Read(ref _isLost);

    /// <inheritdoc />
    public void SimulateLoss() => Volatile.Write(ref _isLost, true);

    /// <inheritdoc />
    public IGpuBuffer CreateBuffer(BufferDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(desc.SizeInBytes, Capabilities.MaxBufferSize, nameof(desc));

        return new Buffer(this, desc);
    }

    /// <inheritdoc />
    public IGpuTexture CreateTexture(TextureDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ObjectDisposedException.ThrowIf(_disposed, this);
        RenderGuard.PositiveSize(desc.Size, Capabilities.MaxTextureDimension);

        return new Texture(this, desc);
    }

    /// <inheritdoc />
    public IOffscreenRenderTarget CreateOffscreenTarget(PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RenderGuard.PositiveSize(size, Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);

        return new RenderTarget(this, size, dpiScale);
    }

    /// <inheritdoc />
    public ISurfaceRenderTarget CreateSurfaceTarget(NativeSurfaceHandle handle, PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (handle.Window == 0)
        {
            throw new ArgumentException("The surface handle is zero.", nameof(handle));
        }

        RenderGuard.PositiveSize(size, Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);

        return new RenderTarget(this, size, dpiScale);
    }

    /// <inheritdoc />
    public ICommandList BeginFrame(IRenderTarget target)
    {
        ThrowIfLost();
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target is not RenderTarget renderTarget || renderTarget.Device != this)
        {
            throw new ArgumentException("The target belongs to another device.", nameof(target));
        }

        renderTarget.ThrowIfDisposed();
        if (_openFrame is not null)
        {
            throw new InvalidOperationException("Another frame is still open.");
        }

        var frame = _freeFrame ?? new CommandList(this);
        _freeFrame = null;
        frame.Begin(renderTarget);
        _openFrame = frame;
        return frame;
    }

    /// <inheritdoc />
    public void WaitForIdle() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    private abstract class Resource : IDisposable
    {
        private bool _disposed;

        protected Resource(NullRenderDevice device)
        {
            Device = device;
            device.LiveResourceCount++;
        }

        public NullRenderDevice Device { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Device.LiveResourceCount--;
            OnDisposed();
        }

        protected void Reopen()
        {
            if (_disposed)
            {
                _disposed = false;
                Device.LiveResourceCount++;
            }
        }

        public void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ObjectDisposedException.ThrowIf(Device._disposed, Device);
        }

        protected virtual void OnDisposed()
        {
        }
    }

    private sealed class Buffer(NullRenderDevice device, BufferDesc desc) : Resource(device), IGpuBuffer
    {
        public long SizeInBytes
        {
            get
            {
                ThrowIfDisposed();
                return desc.SizeInBytes;
            }
        }

        public BufferUsage Usage
        {
            get
            {
                ThrowIfDisposed();
                return desc.Usage;
            }
        }

        public void Write<T>(long offsetInBytes, ReadOnlySpan<T> data)
            where T : unmanaged
        {
            ThrowIfDisposed();
            var byteCount = (long)data.Length * Unsafe.SizeOf<T>();
            RenderGuard.BufferWrite(offsetInBytes, byteCount, desc.SizeInBytes);
            Device.UploadedByteCount += byteCount;
        }
    }

    private sealed class Texture(NullRenderDevice device, TextureDesc desc) : Resource(device), IGpuTexture
    {
        public PixelSize Size
        {
            get
            {
                ThrowIfDisposed();
                return desc.Size;
            }
        }

        public TextureFormat Format
        {
            get
            {
                ThrowIfDisposed();
                return desc.Format;
            }
        }

        public void Write(ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed();
            if (pixels.Length != desc.SizeInBytes)
            {
                throw new ArgumentException("The length does not match the texture.", nameof(pixels));
            }

            Device.UploadedByteCount += pixels.Length;
        }

        public void WriteRow(int row, ReadOnlySpan<byte> pixels)
        {
            ThrowIfDisposed();
            RenderGuard.TextureRow(row, pixels.Length, desc.Size, desc.SizeInBytes / desc.Size.Height);
            Device.UploadedByteCount += pixels.Length;
        }
    }

    private sealed class RenderTarget(NullRenderDevice device, PixelSize size, double dpiScale)
        : Resource(device), IOffscreenRenderTarget, ISurfaceRenderTarget
    {
        private bool _vSync = true;

        public bool VSync
        {
            get
            {
                ThrowIfDisposed();
                return _vSync;
            }

            set
            {
                ThrowIfDisposed();
                ThrowIfFrameOpen();
                _vSync = value;
            }
        }

        public PixelSize Size
        {
            get
            {
                ThrowIfDisposed();
                return size;
            }
        }

        public double DpiScale
        {
            get
            {
                ThrowIfDisposed();
                return dpiScale;
            }
        }

        public void Resize(PixelSize newSize, double newDpiScale) // NOSONAR: size is the field of this class
        {
            ThrowIfDisposed();
            RenderGuard.PositiveSize(newSize, Device.Capabilities.MaxTextureDimension);
            RenderGuard.DpiScale(newDpiScale);
            ThrowIfFrameOpen();
            size = newSize;
            dpiScale = newDpiScale;
        }

        public void ReadPixels(Span<Rgba8> destination)
        {
            ThrowIfDisposed();
            if (destination.Length != size.PixelCount)
            {
                throw new ArgumentException("The length does not match the target.", nameof(destination));
            }

            ThrowIfFrameOpen();
            destination.Clear();
        }

        private void ThrowIfFrameOpen()
        {
            if (Device._openFrame?.Target == this)
            {
                throw new InvalidOperationException("A frame on this target is still open.");
            }
        }
    }

    private sealed class CommandList(NullRenderDevice device) : Resource(device), ICommandList
    {
        private bool _submitted;

        public RenderTarget? Target { get; private set; }

        public void Begin(RenderTarget target)
        {
            Reopen();
            Target = target;
            _submitted = false;
        }

        public void Clear(Rgba8 color) => ThrowIfNotRecording();

        public void FillRectangle(PixelRect bounds, Rgba8 color) => CountDraw();

        public void FillLinearGradient(PixelRect bounds, Rgba8 startColor, Rgba8 endColor, bool vertical) => CountDraw();

        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color) => CountDraw();

        public void FillMesh(ReadOnlySpan<MeshVertex> vertices, PixelRect? clip) => CountDraw();

        public void DrawTextPlaceholder(PixelRect bounds, Rgba8 color) => CountDraw();

        public void DrawImage(IGpuTexture image, PixelRect bounds)
        {
            ArgumentNullException.ThrowIfNull(image);
            ThrowIfNotRecording();
            if (image is not Texture texture || texture.Device != Device)
            {
                throw new ArgumentException("The image belongs to another device.", nameof(image));
            }

            if (texture.Format != TextureFormat.Rgba8Unorm)
            {
                throw new ArgumentException("The image must be RGBA.", nameof(image));
            }

            if (RenderGuard.IsDrawable(bounds))
            {
                CountDraw();
            }
        }

        public void DrawRaster(IGpuTexture values, IGpuTexture colormap, PixelRect bounds, in RasterStyle style)
        {
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(colormap);
            ThrowIfNotRecording();
            if (values is not Texture first || first.Device != Device || colormap is not Texture second || second.Device != Device)
            {
                throw new ArgumentException("The texture belongs to another device.", nameof(values));
            }

            RenderGuard.RasterFormats(values, colormap);
            if (RenderGuard.IsDrawable(bounds))
            {
                CountDraw();
            }
        }

        public void DrawGlyphs(IGpuTexture atlas, ReadOnlySpan<GlyphInstance> glyphs)
        {
            ArgumentNullException.ThrowIfNull(atlas);
            ThrowIfNotRecording();
            if (atlas is not Texture texture || texture.Device != Device)
            {
                throw new ArgumentException("The atlas belongs to another device.", nameof(atlas));
            }

            if (texture.Format != TextureFormat.Rgba8Unorm)
            {
                throw new ArgumentException("The atlas must be RGBA.", nameof(atlas));
            }

            if (!glyphs.IsEmpty)
            {
                CountDraw();
            }
        }

        public void DrawPolyline(IGpuBuffer points, int firstPoint, int pointCount, in PolylineStyle style)
        {
            ArgumentNullException.ThrowIfNull(points);
            ThrowIfNotRecording();
            if (points is not Buffer buffer || buffer.Device != Device)
            {
                throw new ArgumentException("The buffer belongs to another device.", nameof(points));
            }

            if ((buffer.Usage & BufferUsage.Vertex) == 0)
            {
                throw new ArgumentException("The buffer lacks vertex usage.", nameof(points));
            }

            RenderGuard.Polyline(firstPoint, pointCount, style.WidthInPixels, buffer.SizeInBytes);
            CountDraw();
        }

        public void DrawMarkers(IGpuBuffer markers, int firstMarker, int markerCount, in MarkerStyle style)
        {
            ArgumentNullException.ThrowIfNull(markers);
            ThrowIfNotRecording();
            if (markers is not Buffer buffer || buffer.Device != Device)
            {
                throw new ArgumentException("The buffer belongs to another device.", nameof(markers));
            }

            if ((buffer.Usage & BufferUsage.Vertex) == 0)
            {
                throw new ArgumentException("The buffer lacks vertex usage.", nameof(markers));
            }

            RenderGuard.Markers(firstMarker, markerCount, style, buffer.SizeInBytes);
            if (markerCount > 0)
            {
                CountDraw();
            }
        }

        public void Submit()
        {
            ThrowIfNotRecording();
            _submitted = true;
            Device.SubmittedFrameCount++;
            Device._openFrame = null;
        }

        protected override void OnDisposed()
        {
            if (Device._openFrame == this)
            {
                Device._openFrame = null;
            }

            Device._freeFrame = this;
        }

        private void CountDraw()
        {
            ThrowIfNotRecording();
            Device.DrawCallCount++;
        }

        private void ThrowIfNotRecording()
        {
            ThrowIfDisposed();
            if (_submitted)
            {
                throw new InvalidOperationException("The frame was already submitted.");
            }
        }
    }
}
