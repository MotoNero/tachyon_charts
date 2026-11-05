using System.Numerics;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// The handle of one frame, reused by the next one. Recording happens in the <see cref="FrameRecorder"/>
/// shared by all frames of a device.
/// </summary>
internal sealed class WebGpuCommandList(WebGpuRenderDevice device) : WebGpuResource(device), ICommandList
{
    private bool _submitted;

    public WebGpuTarget? Target { get; private set; }

    public void Begin(WebGpuTarget target)
    {
        Reopen();
        Target = target;
        _submitted = false;
    }

    public void Clear(Rgba8 color)
    {
        ThrowIfNotRecording();
        Device.Recorder.Clear(color);
    }

    public void FillRectangle(PixelRect bounds, Rgba8 color)
    {
        ThrowIfNotRecording();
        Device.Recorder.FillRectangle(bounds, color);
    }

    public void FillLinearGradient(PixelRect bounds, Rgba8 startColor, Rgba8 endColor, bool vertical)
    {
        ThrowIfNotRecording();
        Device.Recorder.FillLinearGradient(bounds, startColor, endColor, vertical);
    }

    public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color)
    {
        ThrowIfNotRecording();
        Device.Recorder.FillTriangle(a, b, c, color);
    }

    public void FillMesh(ReadOnlySpan<MeshVertex> vertices, PixelRect? clip)
    {
        ThrowIfNotRecording();
        Device.Recorder.FillMesh(vertices, clip);
    }

    public void SetQuarterTurn(float? sum)
    {
        ThrowIfNotRecording();
        Device.Recorder.SetQuarterTurn(sum);
    }

    public void DrawPolyline(IGpuBuffer points, int firstPoint, int pointCount, in PolylineStyle style)
    {
        ArgumentNullException.ThrowIfNull(points);
        ThrowIfNotRecording();
        if (points is not WebGpuBuffer buffer || buffer.Device != Device)
        {
            throw new ArgumentException("The buffer belongs to another device.", nameof(points));
        }

        if ((buffer.Usage & BufferUsage.Vertex) == 0)
        {
            throw new ArgumentException("The buffer lacks vertex usage.", nameof(points));
        }

        RenderGuard.Polyline(firstPoint, pointCount, style.WidthInPixels, buffer.SizeInBytes);
        Device.Recorder.DrawPolyline(buffer, firstPoint, pointCount, style);
    }

    public void DrawMarkers(IGpuBuffer markers, int firstMarker, int markerCount, in MarkerStyle style)
    {
        ArgumentNullException.ThrowIfNull(markers);
        ThrowIfNotRecording();
        if (markers is not WebGpuBuffer buffer || buffer.Device != Device)
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
            Device.Recorder.DrawMarkers(buffer, firstMarker, markerCount, style);
        }
    }

    public void DrawTextPlaceholder(PixelRect bounds, Rgba8 color) => FillRectangle(bounds, color);

    public void DrawGlyphs(IGpuTexture atlas, ReadOnlySpan<GlyphInstance> glyphs)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ThrowIfNotRecording();
        if (atlas is not WebGpuTexture texture || texture.Device != Device)
        {
            throw new ArgumentException("The atlas belongs to another device.", nameof(atlas));
        }

        if (texture.Format != TextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("The atlas must be RGBA.", nameof(atlas));
        }

        Device.Recorder.DrawGlyphs(texture, glyphs);
    }

    public void DrawImage(IGpuTexture image, PixelRect bounds)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfNotRecording();
        if (image is not WebGpuTexture texture || texture.Device != Device)
        {
            throw new ArgumentException("The image belongs to another device.", nameof(image));
        }

        if (texture.Format != TextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("The image must be RGBA.", nameof(image));
        }

        if (RenderGuard.IsDrawable(bounds))
        {
            Device.Recorder.DrawImage(texture, bounds);
        }
    }

    public void DrawRaster(IGpuTexture values, IGpuTexture colormap, PixelRect bounds, in RasterStyle style)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(colormap);
        ThrowIfNotRecording();
        if (values is not WebGpuTexture valueTexture || valueTexture.Device != Device || colormap is not WebGpuTexture table || table.Device != Device)
        {
            throw new ArgumentException("The texture belongs to another device.", nameof(values));
        }

        RenderGuard.RasterFormats(values, colormap);
        if (RenderGuard.IsDrawable(bounds))
        {
            Device.Recorder.DrawRaster(valueTexture, table, bounds, style);
        }
    }

    public void Submit()
    {
        ThrowIfNotRecording();
        Target!.ThrowIfDisposed();
        Device.Recorder.Submit(Target);
        _submitted = true;
        Device.OnFrameClosed(this);
        Device.ThrowIfDeviceError();
    }

    protected override void OnDisposed()
    {
        if (!Device.IsDisposed)
        {
            Device.OnFrameClosed(this);
            Device.ReturnFrame(this);
        }
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
