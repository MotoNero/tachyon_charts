using SkiaSharp;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// A CPU render device on SkiaSharp raster surfaces, the fallback and the reference for visual tests.
/// </summary>
/// <remarks>
/// Only offscreen rendering is implemented. <see cref="CreateSurfaceTarget"/> arrives with the host spikes.
/// </remarks>
public sealed class SkiaRenderDevice : IRenderDevice
{
    private bool _isLost;

    private readonly List<PendingPicture> _pending = [];
    private bool _isBatching;
    private SkiaCommandList? _openFrame;
    private SkiaCommandList? _freeFrame;

    /// <inheritdoc />
    public DeviceCapabilities Capabilities { get; } = new()
    {
        BackendName = "Skia",
        SupportsCompute = false,
        MaxBufferSize = Array.MaxLength,
        MaxTextureDimension = 16384,
        MaxSampleCount = 1,
    };

    /// <inheritdoc />
    public long DrawCallCount { get; internal set; }

    /// <inheritdoc />
    public long UploadedByteCount { get; internal set; }

    internal bool IsDisposed { get; private set; }

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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(desc.SizeInBytes, Capabilities.MaxBufferSize, nameof(desc));

        return new SkiaBuffer(this, desc);
    }

    /// <inheritdoc />
    public IGpuTexture CreateTexture(TextureDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        RenderGuard.PositiveSize(desc.Size, Capabilities.MaxTextureDimension);

        return new SkiaTexture(this, desc);
    }

    /// <inheritdoc />
    public IOffscreenRenderTarget CreateOffscreenTarget(PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        RenderGuard.PositiveSize(size, Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);

        return new SkiaOffscreenTarget(this, size, dpiScale);
    }

    /// <summary>
    /// Creates a target that keeps its frame as drawing commands for <see cref="DrawLastFrame"/>, in
    /// which meshes are paths: SVG and PDF do not know vertices.
    /// </summary>
    /// <param name="size">The size in pixels.</param>
    /// <param name="dpiScale">The scale from device-independent units to pixels.</param>
    /// <returns>The target.</returns>
    public IOffscreenRenderTarget CreateVectorTarget(PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        RenderGuard.PositiveSize(size, Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);

        return new SkiaOffscreenTarget(this, size, dpiScale, keepsCommands: true);
    }

    /// <summary>Draws the last frame of a vector target onto a canvas, such as one for SVG or PDF.</summary>
    /// <param name="target">A target from <see cref="CreateVectorTarget"/>.</param>
    /// <param name="canvas">The canvas.</param>
    /// <exception cref="ArgumentException">The target keeps no frame.</exception>
    public void DrawLastFrame(IOffscreenRenderTarget target, SKCanvas canvas) // NOSONAR: public API of the instance
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (target is not SkiaOffscreenTarget { Commands: { } commands })
        {
            throw new ArgumentException("The target keeps no frame.", nameof(target));
        }

        canvas.DrawPicture(commands);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. Surfaces arrive with the host spikes.</exception>
    public ISurfaceRenderTarget CreateSurfaceTarget(NativeSurfaceHandle handle, PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        throw new NotSupportedException("Surface targets are not implemented yet.");
    }

    /// <inheritdoc />
    public ICommandList BeginFrame(IRenderTarget target)
    {
        if (IsLost)
        {
            throw new DeviceLostException();
        }

        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (target is not SkiaOffscreenTarget offscreen || offscreen.Device != this)
        {
            throw new ArgumentException("The target belongs to another device.", nameof(target));
        }

        offscreen.ThrowIfDisposed();
        if (_openFrame is not null)
        {
            throw new InvalidOperationException("Another frame is still open.");
        }

        var frame = _freeFrame ?? new SkiaCommandList(this);
        _freeFrame = null;
        frame.Begin(offscreen);
        _openFrame = frame;
        return frame;
    }

    /// <inheritdoc />
    public void BeginBatch() => _isBatching = true;

    /// <inheritdoc />
    public void EndBatch()
    {
        _isBatching = false;

        // Each target has its own pixels and its own picture, so the charts can share the cores.
        if (_pending.Count > 1)
        {
            Parallel.ForEach(_pending, PlayPending);
        }
        else
        {
            _pending.ForEach(PlayPending);
        }

        _pending.Clear();
    }

    internal void Play(SkiaOffscreenTarget target, SKPicture picture)
    {
        if (_isBatching)
        {
            _pending.Add(new PendingPicture(target, picture));
        }
        else
        {
            target.Canvas.DrawPicture(picture);
            picture.Dispose();
        }
    }

    private static void PlayPending(PendingPicture pending)
    {
        pending.Target.Canvas.DrawPicture(pending.Picture);
        pending.Picture.Dispose();
    }

    /// <inheritdoc />
    public void WaitForIdle() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    /// <inheritdoc />
    public void Dispose()
    {
        IsDisposed = true;
        _freeFrame?.Release();
        _freeFrame = null;
    }

    private readonly record struct PendingPicture(SkiaOffscreenTarget Target, SKPicture Picture);

    internal bool IsOpenFrameOn(SkiaOffscreenTarget target) => _openFrame?.Target == target;

    internal void ReturnFrame(SkiaCommandList frame) => _freeFrame = frame;

    internal void OnFrameClosed(SkiaCommandList frame)
    {
        if (_openFrame == frame)
        {
            _openFrame = null;
        }
    }
}
