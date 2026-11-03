using System.Runtime.InteropServices;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// A render target of a <see cref="WebGpuRenderDevice"/>, as far as frame recording needs it.
/// </summary>
internal abstract unsafe class WebGpuTarget(WebGpuRenderDevice device) : WebGpuResource(device), IRenderTarget
{
    public abstract PixelSize Size { get; }

    public abstract double DpiScale { get; }

    public abstract Native.TextureFormat Format { get; }

    /// <summary>
    /// Gets the view to render the next frame into, or <see langword="null"/> to drop the frame.
    /// </summary>
    public abstract Native.TextureView* AcquireView();

    /// <summary>
    /// Finishes a frame rendered into the view from <see cref="AcquireView"/>.
    /// </summary>
    public abstract void CompleteFrame(Native.TextureView* view);
}

/// <summary>
/// A swapchain on a native window. Frames present automatically with <see cref="ICommandList.Submit"/>.
/// </summary>
internal sealed unsafe class WebGpuSurfaceTarget : WebGpuTarget, ISurfaceRenderTarget
{
    private readonly Native.TextureFormat _format;
    private readonly Native.PresentMode _unsyncedMode;
    private Native.Surface* _surface;
    private Native.Texture* _texture;
    private PixelSize _size;
    private double _dpiScale;
    private bool _vSync = true;

    public WebGpuSurfaceTarget(WebGpuRenderDevice device, NativeSurfaceHandle handle, PixelSize size, double dpiScale)
        : base(device)
    {
        _size = size;
        _dpiScale = dpiScale;
        _surface = CreateSurface(device, handle);

        var api = WebGpuRenderDevice.Api;
        Native.SurfaceCapabilities capabilities = default;
        api.SurfaceGetCapabilities(_surface, device.Adapter, &capabilities);
        var formats = new ReadOnlySpan<Native.TextureFormat>(capabilities.Formats, (int)capabilities.FormatCount);
        var modes = new ReadOnlySpan<Native.PresentMode>(capabilities.PresentModes, (int)capabilities.PresentModeCount);

        // Non-sRGB formats: the renderer blends in the color space of its input values.
        _format = formats.Contains(Native.TextureFormat.Rgba8Unorm)
            ? Native.TextureFormat.Rgba8Unorm
            : Native.TextureFormat.Bgra8Unorm;
        _unsyncedMode = Native.PresentMode.Fifo;
        if (modes.Contains(Native.PresentMode.Mailbox))
        {
            _unsyncedMode = Native.PresentMode.Mailbox;
        }

        if (modes.Contains(Native.PresentMode.Immediate))
        {
            _unsyncedMode = Native.PresentMode.Immediate;
        }

        api.SurfaceCapabilitiesFreeMembers(capabilities);

        Configure();
        device.ThrowIfDeviceError();
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

    public override Native.TextureFormat Format => _format;

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
            if (_vSync != value)
            {
                ThrowIfFrameOpen();
                _vSync = value;
                Configure();
            }
        }
    }

    public void Resize(PixelSize size, double dpiScale)
    {
        ThrowIfDisposed();
        RenderGuard.PositiveSize(size, Device.Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);
        ThrowIfFrameOpen();

        var sizeChanged = size != _size;
        _size = size;
        _dpiScale = dpiScale;
        if (sizeChanged)
        {
            Configure();
        }
    }

    public override Native.TextureView* AcquireView()
    {
        var api = WebGpuRenderDevice.Api;

        // One retry: an outdated swapchain, for example after a window change, recovers by reconfiguring.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Native.SurfaceTexture surfaceTexture = default;
            api.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
            switch (surfaceTexture.Status)
            {
                case Native.SurfaceGetCurrentTextureStatus.Success:
                    _texture = surfaceTexture.Texture;
                    return api.TextureCreateView(_texture, null);

                case Native.SurfaceGetCurrentTextureStatus.Timeout:
                    ReleaseTexture(surfaceTexture.Texture);
                    return null;

                case Native.SurfaceGetCurrentTextureStatus.Outdated:
                case Native.SurfaceGetCurrentTextureStatus.Lost:
                    ReleaseTexture(surfaceTexture.Texture);
                    Configure();
                    break;

                case Native.SurfaceGetCurrentTextureStatus.DeviceLost:
                    ReleaseTexture(surfaceTexture.Texture);
                    Device.SimulateLoss();
                    throw new DeviceLostException();

                default:
                    ReleaseTexture(surfaceTexture.Texture);
                    throw new InvalidOperationException($"Acquiring the surface texture failed: {surfaceTexture.Status}.");
            }
        }

        return null;
    }

    public override void CompleteFrame(Native.TextureView* view)
    {
        var api = WebGpuRenderDevice.Api;
        api.SurfacePresent(_surface);
        api.TextureViewRelease(view);
        ReleaseTexture(_texture);
        _texture = null;
    }

    protected override void OnDisposed()
    {
        var api = WebGpuRenderDevice.Api;
        api.SurfaceUnconfigure(_surface);
        api.SurfaceRelease(_surface);
        _surface = null;
    }

    private static Native.Surface* CreateSurface(WebGpuRenderDevice device, NativeSurfaceHandle handle)
    {
        var source = new Native.SurfaceDescriptorFromWindowsHWND
        {
            Chain = new Native.ChainedStruct { SType = Native.SType.SurfaceDescriptorFromWindowsHwnd },
            Hinstance = (void*)Marshal.GetHINSTANCE(typeof(WebGpuSurfaceTarget).Module),
            Hwnd = (void*)handle.Window,
        };
        var descriptor = new Native.SurfaceDescriptor { NextInChain = &source.Chain };

        var surface = WebGpuRenderDevice.Api.InstanceCreateSurface(device.Instance, &descriptor);
        return surface is not null ? surface : throw new InvalidOperationException("Creating the WebGPU surface failed.");
    }

    private static void ReleaseTexture(Native.Texture* texture)
    {
        if (texture is not null)
        {
            WebGpuRenderDevice.Api.TextureRelease(texture);
        }
    }

    private void Configure()
    {
        var configuration = new Native.SurfaceConfiguration
        {
            Device = Device.Device,
            Format = _format,
            Usage = Native.TextureUsage.RenderAttachment,
            AlphaMode = Native.CompositeAlphaMode.Auto,
            Width = (uint)_size.Width,
            Height = (uint)_size.Height,
            PresentMode = _vSync ? Native.PresentMode.Fifo : _unsyncedMode,
        };
        WebGpuRenderDevice.Api.SurfaceConfigure(_surface, &configuration);
    }

    private void ThrowIfFrameOpen()
    {
        if (Device.IsOpenFrameOn(this))
        {
            throw new InvalidOperationException("A frame on this target is still open.");
        }
    }
}
