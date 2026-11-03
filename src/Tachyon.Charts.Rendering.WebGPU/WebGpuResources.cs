using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// Dispose rules shared by all objects of a <see cref="WebGpuRenderDevice"/>.
/// </summary>
internal abstract class WebGpuResource(WebGpuRenderDevice device) : IDisposable
{
    private bool _disposed;

    public WebGpuRenderDevice Device { get; } = device;

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

    protected abstract void OnDisposed();
}

internal sealed unsafe class WebGpuBuffer : WebGpuResource, IGpuBuffer
{
    private readonly BufferDesc _desc;
    private Native.Buffer* _buffer;

    public WebGpuBuffer(WebGpuRenderDevice device, BufferDesc desc)
        : base(device)
    {
        _desc = desc;

        var usage = Native.BufferUsage.CopyDst;
        usage |= (desc.Usage & BufferUsage.Vertex) != 0 ? Native.BufferUsage.Vertex : 0;
        usage |= (desc.Usage & BufferUsage.Uniform) != 0 ? Native.BufferUsage.Uniform : 0;
        usage |= (desc.Usage & BufferUsage.Storage) != 0 ? Native.BufferUsage.Storage | Native.BufferUsage.CopySrc : 0;

        var descriptor = new Native.BufferDescriptor
        {
            Usage = usage,
            Size = (ulong)RenderGuard.AlignUp(desc.SizeInBytes, RenderGuard.BufferWriteAlignment),
        };
        _buffer = WebGpuRenderDevice.Api.DeviceCreateBuffer(device.Device, &descriptor);
    }

    public Native.Buffer* Handle => _buffer;

    public long SizeInBytes
    {
        get
        {
            ThrowIfDisposed();
            return _desc.SizeInBytes;
        }
    }

    public BufferUsage Usage
    {
        get
        {
            ThrowIfDisposed();
            return _desc.Usage;
        }
    }

    public void Write<T>(long offsetInBytes, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ThrowIfDisposed();
        var byteCount = (long)data.Length * Unsafe.SizeOf<T>();
        RenderGuard.BufferWrite(offsetInBytes, byteCount, _desc.SizeInBytes);
        if (byteCount == 0)
        {
            return;
        }

        if (Device.Uploads.Accepts(offsetInBytes, byteCount))
        {
            Device.Uploads.Add(_buffer, (ulong)offsetInBytes, MemoryMarshal.AsBytes(data));
            Device.UploadedByteCount += byteCount;
            return;
        }

        Device.Uploads.Flush();
        fixed (T* pointer = data)
        {
            WebGpuRenderDevice.Api.QueueWriteBuffer(Device.Queue, _buffer, (ulong)offsetInBytes, pointer, (nuint)byteCount);
        }

        Device.UploadedByteCount += byteCount;
    }

    protected override void OnDisposed()
    {
        Device.Uploads.Flush();
        WebGpuRenderDevice.Api.BufferRelease(_buffer);
        _buffer = null;
    }
}

internal sealed unsafe class WebGpuTexture : WebGpuResource, IGpuTexture
{
    private readonly TextureDesc _desc;
    private Native.Texture* _texture;
    private Native.TextureView* _view;
    private Native.BindGroup* _bindGroup;

    public WebGpuTexture(WebGpuRenderDevice device, TextureDesc desc, bool isRenderTarget = false)
        : base(device)
    {
        _desc = desc;

        var descriptor = new Native.TextureDescriptor
        {
            // Compute shaders write values into float textures.
            Usage = Native.TextureUsage.TextureBinding | Native.TextureUsage.CopyDst | (desc.Format == TextureFormat.R32Float ? Native.TextureUsage.StorageBinding : 0) | (isRenderTarget ? Native.TextureUsage.RenderAttachment : 0),
            Dimension = Native.TextureDimension.Dimension2D,
            Size = new Native.Extent3D((uint)desc.Size.Width, (uint)desc.Size.Height, 1),
            Format = ToNative(desc.Format),
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _texture = WebGpuRenderDevice.Api.DeviceCreateTexture(device.Device, &descriptor);
    }

    public PixelSize Size
    {
        get
        {
            ThrowIfDisposed();
            return _desc.Size;
        }
    }

    public Native.TextureView* View
    {
        get
        {
            if (_view is null)
            {
                _view = WebGpuRenderDevice.Api.TextureCreateView(_texture, null);
            }

            return _view;
        }
    }

    public TextureFormat Format
    {
        get
        {
            ThrowIfDisposed();
            return _desc.Format;
        }
    }

    public void Write(ReadOnlySpan<byte> pixels)
    {
        ThrowIfDisposed();
        if (pixels.Length != _desc.SizeInBytes)
        {
            throw new ArgumentException("The length does not match the texture.", nameof(pixels));
        }

        var destination = new Native.ImageCopyTexture
        {
            Texture = _texture,
            Aspect = Native.TextureAspect.All,
        };
        var layout = new Native.TextureDataLayout
        {
            BytesPerRow = (uint)(_desc.SizeInBytes / _desc.Size.Height),
            RowsPerImage = (uint)_desc.Size.Height,
        };
        var extent = new Native.Extent3D((uint)_desc.Size.Width, (uint)_desc.Size.Height, 1);

        fixed (byte* pointer = pixels)
        {
            WebGpuRenderDevice.Api.QueueWriteTexture(Device.Queue, &destination, pointer, (nuint)pixels.Length, &layout, &extent);
        }

        Device.UploadedByteCount += pixels.Length;
    }

    public void WriteRow(int row, ReadOnlySpan<byte> pixels)
    {
        ThrowIfDisposed();
        var rowBytes = _desc.SizeInBytes / _desc.Size.Height;
        RenderGuard.TextureRow(row, pixels.Length, _desc.Size, rowBytes);

        var destination = new Native.ImageCopyTexture
        {
            Texture = _texture,
            Aspect = Native.TextureAspect.All,
            Origin = new Native.Origin3D(0, (uint)row, 0),
        };
        var layout = new Native.TextureDataLayout { BytesPerRow = (uint)rowBytes, RowsPerImage = 1 };
        var extent = new Native.Extent3D((uint)_desc.Size.Width, 1, 1);

        fixed (byte* pointer = pixels)
        {
            WebGpuRenderDevice.Api.QueueWriteTexture(Device.Queue, &destination, pointer, (nuint)pixels.Length, &layout, &extent);
        }

        Device.UploadedByteCount += pixels.Length;
    }

    /// <summary>
    /// Gets the bind group of this texture as an atlas, created on first use.
    /// </summary>
    public Native.BindGroup* GetBindGroup(Native.BindGroupLayout* layout)
    {
        if (_bindGroup is null)
        {
            var api = WebGpuRenderDevice.Api;
            var entry = new Native.BindGroupEntry { Binding = 0, TextureView = View };
            var descriptor = new Native.BindGroupDescriptor { Layout = layout, EntryCount = 1, Entries = &entry };
            _bindGroup = api.DeviceCreateBindGroup(Device.Device, &descriptor);
        }

        return _bindGroup;
    }

    protected override void OnDisposed()
    {
        var api = WebGpuRenderDevice.Api;
        if (_bindGroup is not null)
        {
            api.BindGroupRelease(_bindGroup);
        }

        if (_view is not null)
        {
            api.TextureViewRelease(_view);
        }

        api.TextureRelease(_texture);
        _bindGroup = null;
        _view = null;
        _texture = null;
    }

    private static Native.TextureFormat ToNative(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm => Native.TextureFormat.Rgba8Unorm,
        TextureFormat.R8Unorm => Native.TextureFormat.R8Unorm,
        TextureFormat.R32Float => Native.TextureFormat.R32float,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown texture format."),
    };
}

internal sealed unsafe class WebGpuOffscreenTarget : WebGpuTarget, IOffscreenRenderTarget
{
    public const Native.TextureFormat PixelFormat = Native.TextureFormat.Rgba8Unorm;

    private const int BytesPerPixel = 4;
    private const int CopyRowAlignment = 256;

    private readonly PixelSize _size;
    private readonly double _dpiScale;
    private Native.Texture* _texture;
    private Native.TextureView* _view;

    public WebGpuOffscreenTarget(WebGpuRenderDevice device, PixelSize size, double dpiScale)
        : base(device)
    {
        _size = size;
        _dpiScale = dpiScale;

        var descriptor = new Native.TextureDescriptor
        {
            Usage = Native.TextureUsage.RenderAttachment | Native.TextureUsage.CopySrc,
            Dimension = Native.TextureDimension.Dimension2D,
            Size = new Native.Extent3D((uint)size.Width, (uint)size.Height, 1),
            Format = PixelFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _texture = WebGpuRenderDevice.Api.DeviceCreateTexture(device.Device, &descriptor);
        _view = WebGpuRenderDevice.Api.TextureCreateView(_texture, null);
    }

    public override PixelSize Size
    {
        get
        {
            ThrowIfDisposed();
            return _size;
        }
    }

    public override double DpiScale
    {
        get
        {
            ThrowIfDisposed();
            return _dpiScale;
        }
    }

    public override Native.TextureFormat Format => PixelFormat;

    public override Native.TextureView* AcquireView() => _view;

    public override void CompleteFrame(Native.TextureView* view)
    {
    }

    public void ReadPixels(Span<Rgba8> destination)
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

        var api = WebGpuRenderDevice.Api;
        var bytesPerRow = (int)RenderGuard.AlignUp(_size.Width * (long)BytesPerPixel, CopyRowAlignment);
        var stagingSize = (long)bytesPerRow * _size.Height;
        var stagingDescriptor = new Native.BufferDescriptor
        {
            Usage = Native.BufferUsage.MapRead | Native.BufferUsage.CopyDst,
            Size = (ulong)stagingSize,
        };
        var staging = api.DeviceCreateBuffer(Device.Device, &stagingDescriptor);
        try
        {
            CopyToStaging(staging, bytesPerRow);

            var status = Native.BufferMapAsyncStatus.Unknown;
            api.BufferMapAsync(
                staging,
                Native.MapMode.Read,
                0,
                (nuint)stagingSize,
                new Native.PfnBufferMapCallback(&OnBufferMapped),
                &status);
            WebGpuRenderDevice.Extension.DevicePoll(Device.Device, true, null);
            Device.ThrowIfDeviceError();
            if (status != Native.BufferMapAsyncStatus.Success)
            {
                throw new InvalidOperationException($"Mapping the readback buffer failed: {status}.");
            }

            var mapped = (byte*)api.BufferGetConstMappedRange(staging, 0, (nuint)stagingSize);
            for (var row = 0; row < _size.Height; row++)
            {
                var source = new ReadOnlySpan<Rgba8>(mapped + ((long)row * bytesPerRow), _size.Width);
                source.CopyTo(destination.Slice(row * _size.Width));
            }

            api.BufferUnmap(staging);
        }
        finally
        {
            api.BufferRelease(staging);
        }
    }

    protected override void OnDisposed()
    {
        WebGpuRenderDevice.Api.TextureViewRelease(_view);
        WebGpuRenderDevice.Api.TextureRelease(_texture);
        _view = null;
        _texture = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBufferMapped(Native.BufferMapAsyncStatus status, void* userData) =>
        *(Native.BufferMapAsyncStatus*)userData = status;

    private void CopyToStaging(Native.Buffer* staging, int bytesPerRow)
    {
        var api = WebGpuRenderDevice.Api;
        var encoder = api.DeviceCreateCommandEncoder(Device.Device, null);
        var source = new Native.ImageCopyTexture { Texture = _texture, Aspect = Native.TextureAspect.All };
        var target = new Native.ImageCopyBuffer
        {
            Buffer = staging,
            Layout = new Native.TextureDataLayout { BytesPerRow = (uint)bytesPerRow, RowsPerImage = (uint)_size.Height },
        };
        var extent = new Native.Extent3D((uint)_size.Width, (uint)_size.Height, 1);

        api.CommandEncoderCopyTextureToBuffer(encoder, &source, &target, &extent);
        var commands = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(Device.Queue, 1, &commands);
        api.CommandBufferRelease(commands);
        api.CommandEncoderRelease(encoder);
    }
}
