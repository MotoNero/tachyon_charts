using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Dispose rules shared by all objects of a <see cref="SkiaRenderDevice"/>.
/// </summary>
internal abstract class SkiaResource(SkiaRenderDevice device) : IDisposable
{
    private bool _disposed;

    public SkiaRenderDevice Device { get; } = device;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            OnDisposed();
        }
    }

    public void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(Device.IsDisposed, Device);
    }

    protected void Reopen() => _disposed = false;

    protected virtual void OnDisposed()
    {
    }
}

internal sealed class SkiaBuffer(SkiaRenderDevice device, BufferDesc desc) : SkiaResource(device), IGpuBuffer
{
    public byte[] Data { get; } = new byte[desc.SizeInBytes];

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
        MemoryMarshal.AsBytes(data).CopyTo(Data.AsSpan((int)offsetInBytes));
        Device.UploadedByteCount += byteCount;
    }
}

internal sealed class SkiaTexture(SkiaRenderDevice device, TextureDesc desc) : SkiaResource(device), IGpuTexture
{
    public byte[] Data { get; } = new byte[desc.SizeInBytes];

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
        if (pixels.Length != Data.Length)
        {
            throw new ArgumentException("The length does not match the texture.", nameof(pixels));
        }

        pixels.CopyTo(Data);
        Device.UploadedByteCount += pixels.Length;
    }

    public void WriteRow(int row, ReadOnlySpan<byte> pixels)
    {
        ThrowIfDisposed();
        var rowBytes = desc.SizeInBytes / desc.Size.Height;
        RenderGuard.TextureRow(row, pixels.Length, desc.Size, rowBytes);
        pixels.CopyTo(Data.AsSpan((int)(row * rowBytes)));
        Device.UploadedByteCount += pixels.Length;
    }
}

internal sealed class SkiaOffscreenTarget : SkiaResource, IOffscreenRenderTarget
{
    private readonly PixelSize _size;
    private readonly double _dpiScale;
    private readonly SKBitmap _bitmap;

    public SkiaOffscreenTarget(SkiaRenderDevice device, PixelSize size, double dpiScale, bool keepsCommands = false)
        : base(device)
    {
        _size = size;
        _dpiScale = dpiScale;
        KeepsCommands = keepsCommands;
        _bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _bitmap.Erase(SKColors.Transparent);
        Canvas = new SKCanvas(_bitmap);
    }

    public SKCanvas Canvas { get; }

    public bool KeepsCommands { get; }

    public SKPicture? Commands { get; private set; }

    public PixelSize Size
    {
        get
        {
            ThrowIfDisposed();
            return _size;
        }
    }

    public double DpiScale
    {
        get
        {
            ThrowIfDisposed();
            return _dpiScale;
        }
    }

    public void Keep(SKPicture commands)
    {
        Commands?.Dispose();
        Commands = commands;
    }

    public unsafe void ReadPixels(Span<Rgba8> destination)
    {
        ThrowIfDisposed();
        if (destination.Length != _size.PixelCount)
        {
            throw new ArgumentException("The length does not match the target.", nameof(destination));
        }

        if (Device.IsOpenFrameOn(this))
        {
            throw new InvalidOperationException("A frame on this target is still open.");
        }

        var info = new SKImageInfo(_size.Width, _size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        fixed (Rgba8* pointer = destination)
        {
            if (!_bitmap.PeekPixels().ReadPixels(info, (nint)pointer, info.RowBytes))
            {
                throw new InvalidOperationException("Reading the Skia pixels failed.");
            }
        }
    }

    protected override void OnDisposed()
    {
        Commands?.Dispose();
        Canvas.Dispose();
        _bitmap.Dispose();
    }
}
