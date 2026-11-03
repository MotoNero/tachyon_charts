using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Records a frame into an <see cref="SKPicture"/>, so that an unsubmitted frame leaves the target untouched.
/// The next frame reuses the object.
/// </summary>
/// <remarks>
/// Only lines are anti-aliased, to match the WebGPU backend.
/// </remarks>
internal sealed class SkiaCommandList(SkiaRenderDevice device) : SkiaResource(device), ICommandList
{
    private const int GlowLayers = 5;
    private const int MinimumPointsPerColumn = 3;

    private readonly SKPictureRecorder _recorder = new();
    private readonly SKPaint _paint = new() { IsAntialias = false };
    private readonly SKPaint _strokePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };
    private readonly SKPaint _markerPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _trianglePath = CreateMarkerPath(MarkerShape.Triangle);
    private readonly SKPath _diamondPath = CreateMarkerPath(MarkerShape.Diamond);
    private readonly SKPath _crossPath = CreateMarkerPath(MarkerShape.Cross);
    private readonly SKPath _plusPath = CreateMarkerPath(MarkerShape.Plus);
    private readonly SKColor[] _gradientColors = new SKColor[2];

    private readonly List<SkiaBuffer> _usedBuffers = [];
    private readonly List<SKImage> _glyphImages = [];
    private SKCanvas _canvas = null!;
    private bool _submitted;

    public SkiaOffscreenTarget? Target { get; private set; }

    public void Begin(SkiaOffscreenTarget target)
    {
        Reopen();
        Target = target;
        _submitted = false;
        _canvas = _recorder.BeginRecording(new SKRect(0, 0, target.Size.Width, target.Size.Height));
    }

    /// <summary>
    /// Frees the native objects when the device goes away.
    /// </summary>
    public void Release()
    {
        _paint.Dispose();
        _strokePaint.Dispose();
        _markerPaint.Dispose();
        _trianglePath.Dispose();
        _diamondPath.Dispose();
        _crossPath.Dispose();
        _plusPath.Dispose();
        _recorder.Dispose();
    }

    public void Clear(Rgba8 color)
    {
        ThrowIfNotRecording();
        if (Target!.KeepsCommands)
        {
            // A clear ignores the scale of the canvas it is played back on.
            _paint.Color = ToSkia(color);
            _canvas.DrawRect(0, 0, Target.Size.Width, Target.Size.Height, _paint);
            return;
        }

        _canvas.Clear(ToSkia(color));
    }

    public void FillRectangle(PixelRect bounds, Rgba8 color)
    {
        ThrowIfNotRecording();
        if (RenderGuard.IsDrawable(bounds))
        {
            _paint.Color = ToSkia(color);
            _canvas.DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, _paint);
            Device.DrawCallCount++;
        }
    }

    public void FillLinearGradient(PixelRect bounds, Rgba8 startColor, Rgba8 endColor, bool vertical)
    {
        ThrowIfNotRecording();
        if (!RenderGuard.IsDrawable(bounds))
        {
            return;
        }

        _gradientColors[0] = ToSkia(startColor);
        _gradientColors[1] = ToSkia(endColor);
        var start = new SKPoint(bounds.X, bounds.Y);
        var end = vertical
            ? new SKPoint(bounds.X, bounds.Y + bounds.Height)
            : new SKPoint(bounds.X + bounds.Width, bounds.Y);
        using var shader = SKShader.CreateLinearGradient(start, end, _gradientColors, SKShaderTileMode.Clamp);
        _paint.Shader = shader;
        _canvas.DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, _paint);
        _paint.Shader = null;
        Device.DrawCallCount++;
    }

    public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color)
    {
        ThrowIfNotRecording();
        if (RenderGuard.IsDrawable(a, b, c))
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(a.X, a.Y);
            builder.LineTo(b.X, b.Y);
            builder.LineTo(c.X, c.Y);
            builder.Close();
            using var path = builder.Detach();

            _paint.Color = ToSkia(color);
            _canvas.DrawPath(path, _paint);
            Device.DrawCallCount++;
        }
    }

    public void FillMesh(ReadOnlySpan<MeshVertex> vertices, PixelRect? clip)
    {
        ThrowIfNotRecording();
        var count = vertices.Length - (vertices.Length % 3);
        if (count == 0)
        {
            return;
        }

        if (Target!.KeepsCommands)
        {
            FillMeshAsPaths(vertices[..count], clip);
            return;
        }

        var positions = new SKPoint[count];
        var colors = new SKColor[count];
        for (var i = 0; i < count; i++)
        {
            positions[i] = new SKPoint(vertices[i].Position.X, vertices[i].Position.Y);
            colors[i] = ToSkia(vertices[i].Color);
        }

        using var mesh = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, colors);
        _canvas.Save();
        if (clip is { } rectangle)
        {
            _canvas.ClipRect(new SKRect(rectangle.X, rectangle.Y, rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height));
        }

        _canvas.DrawVertices(mesh, SKBlendMode.Dst, _paint);
        _canvas.Restore();
        Device.DrawCallCount++;
    }

    // One path per color: adjacent triangles of their own would show hairlines in a PDF viewer.
    private void FillMeshAsPaths(ReadOnlySpan<MeshVertex> vertices, PixelRect? clip)
    {
        _canvas.Save();
        if (clip is { } rectangle)
        {
            _canvas.ClipRect(new SKRect(rectangle.X, rectangle.Y, rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height));
        }

        using var builder = new SKPathBuilder();
        var pathColor = default(Rgba8);
        var isEmpty = true;
        for (var i = 0; i < vertices.Length; i += 3)
        {
            ref readonly var a = ref vertices[i];
            ref readonly var b = ref vertices[i + 1];
            ref readonly var c = ref vertices[i + 2];
            var color = new Rgba8((byte)((a.Color.R + b.Color.R + c.Color.R) / 3), (byte)((a.Color.G + b.Color.G + c.Color.G) / 3), (byte)((a.Color.B + b.Color.B + c.Color.B) / 3), (byte)((a.Color.A + b.Color.A + c.Color.A) / 3));
            if (!isEmpty && color != pathColor)
            {
                FlushMeshPath(builder, pathColor);
            }

            pathColor = color;
            isEmpty = false;
            builder.MoveTo(a.Position.X, a.Position.Y);
            builder.LineTo(b.Position.X, b.Position.Y);
            builder.LineTo(c.Position.X, c.Position.Y);
            builder.Close();
        }

        if (!isEmpty)
        {
            FlushMeshPath(builder, pathColor);
        }

        _canvas.Restore();
        Device.DrawCallCount++;
    }

    private void FlushMeshPath(SKPathBuilder builder, Rgba8 color)
    {
        using var path = builder.Detach();
        _paint.Color = ToSkia(color);
        _canvas.DrawPath(path, _paint);
    }

    public void DrawPolyline(IGpuBuffer points, int firstPoint, int pointCount, in PolylineStyle style)
    {
        ArgumentNullException.ThrowIfNull(points);
        ThrowIfNotRecording();
        if (points is not SkiaBuffer buffer || buffer.Device != Device)
        {
            throw new ArgumentException("The buffer belongs to another device.", nameof(points));
        }

        if ((buffer.Usage & BufferUsage.Vertex) == 0)
        {
            throw new ArgumentException("The buffer lacks vertex usage.", nameof(points));
        }

        var widthInPixels = style.WidthInPixels;
        var color = style.Color;
        RenderGuard.Polyline(firstPoint, pointCount, widthInPixels, buffer.SizeInBytes);
        _usedBuffers.Add(buffer);

        var positions = MemoryMarshal.Cast<byte, Vector2>(buffer.Data).Slice(firstPoint, pointCount);
        // Widths below one pixel get reduced coverage, like in the WebGPU line shader.
        _strokePaint.StrokeWidth = Math.Max(widthInPixels, 1);
        _strokePaint.Color = ToSkia(color with { A = (byte)MathF.Round(color.A * Math.Min(widthInPixels, 1)) });

        // Separate segments, like the instanced quads of the WebGPU backend: both blend the overlaps.
        _canvas.Save();
        if (style.Clip is { } clip)
        {
            _canvas.ClipRect(new SKRect(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height));
        }

        if (style.GlowRadius > 0)
        {
            DrawGlow(positions, style);
        }

        DrawSegments(positions, style);
        _canvas.Restore();
        Device.DrawCallCount++;
    }

    private static bool IsAscending(ReadOnlySpan<Vector2> positions)
    {
        var previous = float.NegativeInfinity;
        foreach (var position in positions)
        {
            if (position.X < PolylineStyle.Gap)
            {
                if (position.X < previous)
                {
                    return false;
                }

                previous = position.X;
            }
        }

        return true;
    }

    // A streaming line holds several points per pixel column, and each would paint the same pixels again.
    // One stroke from the lowest to the highest point of a column shows the same.
    private void DrawColumns(ReadOnlySpan<Vector2> positions, in PolylineStyle style)
    {
        var segments = new List<SKPoint>(Target!.Size.Width * 4);
        var column = int.MinValue;
        var low = 0f;
        var high = 0f;
        var hasPrevious = false;
        var last = default(SKPoint);
        foreach (var position in positions)
        {
            if (position.X >= PolylineStyle.Gap)
            {
                FlushColumn(segments, column, low, high, ref hasPrevious, ref last);
                column = int.MinValue;
                hasPrevious = false;
                continue;
            }

            var pixel = (position * style.Scale) + style.Offset;
            var current = (int)MathF.Floor(pixel.X);
            if (current != column)
            {
                FlushColumn(segments, column, low, high, ref hasPrevious, ref last);
                column = current;
                low = pixel.Y;
                high = pixel.Y;
            }
            else
            {
                low = MathF.Min(low, pixel.Y);
                high = MathF.Max(high, pixel.Y);
            }
        }

        FlushColumn(segments, column, low, high, ref hasPrevious, ref last);
        if (segments.Count > 0)
        {
            _canvas.DrawPoints(SKPointMode.Lines, segments.ToArray(), _strokePaint);
        }
    }

    private static void FlushColumn(List<SKPoint> segments, int column, float low, float high, ref bool hasPrevious, ref SKPoint last)
    {
        if (column == int.MinValue)
        {
            return;
        }

        var top = new SKPoint(column + 0.5f, low);
        var bottom = new SKPoint(column + 0.5f, high);
        if (hasPrevious)
        {
            segments.Add(last);
            segments.Add(MathF.Abs(last.Y - low) < MathF.Abs(last.Y - high) ? top : bottom);
        }

        segments.Add(top);
        segments.Add(bottom);
        last = bottom;
        hasPrevious = true;
    }

    // One call for all segments: a call for each of them makes up most of the cost of a frame.
    private void DrawSegments(ReadOnlySpan<Vector2> positions, in PolylineStyle style)
    {
        if (positions.Length > Target!.Size.Width * MinimumPointsPerColumn && IsAscending(positions))
        {
            DrawColumns(positions, style);
            return;
        }

        var count = 0;
        for (var i = 1; i < positions.Length; i++)
        {
            if (positions[i - 1].X < PolylineStyle.Gap && positions[i].X < PolylineStyle.Gap)
            {
                count += 2;
            }
        }

        if (count == 0)
        {
            return;
        }

        var segments = new SKPoint[count];
        var next = 0;
        for (var i = 1; i < positions.Length; i++)
        {
            if (positions[i - 1].X < PolylineStyle.Gap && positions[i].X < PolylineStyle.Gap)
            {
                var start = (positions[i - 1] * style.Scale) + style.Offset;
                var end = (positions[i] * style.Scale) + style.Offset;
                segments[next++] = new SKPoint(start.X, start.Y);
                segments[next++] = new SKPoint(end.X, end.Y);
            }
        }

        _canvas.DrawPoints(SKPointMode.Lines, segments, _strokePaint);
    }

    // Simplified: a few wider strokes instead of a smooth falloff, with Lighten so that they do not add up.
    private void DrawGlow(ReadOnlySpan<Vector2> positions, in PolylineStyle style)
    {
        var width = _strokePaint.StrokeWidth;
        var color = _strokePaint.Color;
        _strokePaint.BlendMode = SKBlendMode.Lighten;
        for (var layer = GlowLayers; layer >= 1; layer--)
        {
            var reach = layer / (float)GlowLayers;
            var alpha = style.Color.A / 255f * style.GlowIntensity * MathF.Exp(-2 * reach * reach);
            _strokePaint.StrokeWidth = width + (2 * style.GlowRadius * reach);
            _strokePaint.Color = new SKColor((byte)(style.Color.R * alpha), (byte)(style.Color.G * alpha), (byte)(style.Color.B * alpha), 255);
            DrawSegments(positions, style);
        }

        _strokePaint.BlendMode = SKBlendMode.SrcOver;
        _strokePaint.StrokeWidth = width;
        _strokePaint.Color = color;
    }

    public void DrawMarkers(IGpuBuffer markers, int firstMarker, int markerCount, in MarkerStyle style)
    {
        ArgumentNullException.ThrowIfNull(markers);
        ThrowIfNotRecording();
        if (markers is not SkiaBuffer buffer || buffer.Device != Device)
        {
            throw new ArgumentException("The buffer belongs to another device.", nameof(markers));
        }

        if ((buffer.Usage & BufferUsage.Vertex) == 0)
        {
            throw new ArgumentException("The buffer lacks vertex usage.", nameof(markers));
        }

        RenderGuard.Markers(firstMarker, markerCount, style, buffer.SizeInBytes);
        if (markerCount == 0)
        {
            return;
        }

        _usedBuffers.Add(buffer);
        if (markerCount >= MarkerSplatter.MinimumCount && MarkerSplatter.Supports(style))
        {
            var size = Target!.Size;
            var pixels = new Rgba8[size.PixelCount];
            MarkerSplatter.Splat(buffer.Data, firstMarker, markerCount, style, pixels, size);
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var data = SKData.CreateCopy(MemoryMarshal.AsBytes(pixels.AsSpan()));
            var picture = SKImage.FromPixels(info, data, info.RowBytes);
            _glyphImages.Add(picture);
            _canvas.DrawImage(picture, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
            Device.DrawCallCount++;
            return;
        }

        var instances = MemoryMarshal.Cast<byte, MarkerInstance>(buffer.Data).Slice(firstMarker, markerCount);
        _canvas.Save();
        if (style.Clip is { } clip)
        {
            _canvas.ClipRect(new SKRect(clip.X, clip.Y, clip.X + clip.Width, clip.Y + clip.Height));
        }

        for (var i = 0; i < instances.Length; i++)
        {
            ref readonly var marker = ref instances[i];
            var center = (marker.Position * style.Scale) + style.Offset;
            var size = marker.Size * style.SizeScale;
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(size) || size <= 0)
            {
                continue;
            }

            if (style.BorderWidthInPixels > 0)
            {
                _markerPaint.Color = ToSkia(style.BorderColor);
                DrawMarkerShape(center, size, style.Shape);
            }

            var innerSize = size - (2 * style.BorderWidthInPixels);
            if (innerSize > 0)
            {
                _markerPaint.Color = ToSkia(marker.Color);
                DrawMarkerShape(center, innerSize, style.Shape);
            }
        }

        _canvas.Restore();
        Device.DrawCallCount++;
    }

    private void DrawMarkerShape(Vector2 center, float size, MarkerShape shape)
    {
        var half = size * 0.5f;
        if (shape == MarkerShape.Circle)
        {
            _canvas.DrawCircle(center.X, center.Y, half, _markerPaint);
            return;
        }

        if (shape == MarkerShape.Square)
        {
            _canvas.DrawRect(center.X - half, center.Y - half, size, size, _markerPaint);
            return;
        }

        var path = shape switch
        {
            MarkerShape.Triangle => _trianglePath,
            MarkerShape.Diamond => _diamondPath,
            MarkerShape.Cross => _crossPath,
            _ => _plusPath,
        };
        _canvas.Save();
        _canvas.Translate(center.X, center.Y);
        _canvas.Scale(half, half);
        _canvas.DrawPath(path, _markerPaint);
        _canvas.Restore();
    }

    private static SKPath CreateMarkerPath(MarkerShape shape)
    {
        ReadOnlySpan<Vector2> points = shape switch
        {
            MarkerShape.Triangle => [new(0, -1.1547005f), new(1, 0.57735027f), new(-1, 0.57735027f)],
            MarkerShape.Diamond => [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)],
            _ =>
            [
                new(-0.34f, -1), new(0.34f, -1), new(0.34f, -0.34f),
                new(1, -0.34f), new(1, 0.34f), new(0.34f, 0.34f),
                new(0.34f, 1), new(-0.34f, 1), new(-0.34f, 0.34f),
                new(-1, 0.34f), new(-1, -0.34f), new(-0.34f, -0.34f),
            ],
        };
        using var builder = new SKPathBuilder();
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            if (shape == MarkerShape.Cross)
            {
                point = new Vector2((point.X - point.Y) * 0.70710678f, (point.X + point.Y) * 0.70710678f);
            }

            if (i == 0)
            {
                builder.MoveTo(point.X, point.Y);
            }
            else
            {
                builder.LineTo(point.X, point.Y);
            }
        }

        builder.Close();
        return builder.Detach();
    }

    public void DrawGlyphs(IGpuTexture atlas, ReadOnlySpan<GlyphInstance> glyphs)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ThrowIfNotRecording();
        if (atlas is not SkiaTexture texture || texture.Device != Device)
        {
            throw new ArgumentException("The atlas belongs to another device.", nameof(atlas));
        }

        if (texture.Format != TextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("The atlas must be RGBA.", nameof(atlas));
        }

        if (glyphs.IsEmpty)
        {
            return;
        }

        if (GlyphRasterizer.Rasterize(texture, glyphs, Target!.Size) is { } image)
        {
            _glyphImages.Add(image.Image);
            _canvas.DrawImage(image.Image, image.Left, image.Top, new SKSamplingOptions(SKFilterMode.Nearest), null);
        }

        Device.DrawCallCount++;
    }

    public void DrawTextPlaceholder(PixelRect bounds, Rgba8 color) => FillRectangle(bounds, color);

    public void DrawImage(IGpuTexture image, PixelRect bounds)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfNotRecording();
        if (image is not SkiaTexture texture || texture.Device != Device)
        {
            throw new ArgumentException("The image belongs to another device.", nameof(image));
        }

        if (texture.Format != TextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("The image must be RGBA.", nameof(image));
        }

        if (!RenderGuard.IsDrawable(bounds))
        {
            return;
        }

        var info = new SKImageInfo(texture.Size.Width, texture.Size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var data = SKData.CreateCopy(texture.Data);
        var picture = SKImage.FromPixels(info, data, info.RowBytes);
        _glyphImages.Add(picture);
        _canvas.DrawImage(
            picture,
            new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height),
            new SKSamplingOptions(SKFilterMode.Nearest));
        Device.DrawCallCount++;
    }

    public void DrawRaster(IGpuTexture values, IGpuTexture colormap, PixelRect bounds, in RasterStyle style)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(colormap);
        ThrowIfNotRecording();
        if (values is not SkiaTexture valueTexture || valueTexture.Device != Device || colormap is not SkiaTexture table || table.Device != Device)
        {
            throw new ArgumentException("The texture belongs to another device.", nameof(values));
        }

        RenderGuard.RasterFormats(values, colormap);
        if (!RenderGuard.IsDrawable(bounds))
        {
            return;
        }

        // The pixels whose centers are inside, like the rasterization of a GPU.
        var left = (int)MathF.Ceiling(bounds.X - 0.5f);
        var top = (int)MathF.Ceiling(bounds.Y - 0.5f);
        var right = (int)MathF.Ceiling(bounds.X + bounds.Width - 0.5f);
        var bottom = (int)MathF.Ceiling(bounds.Y + bounds.Height - 0.5f);
        if (right <= left || bottom <= top)
        {
            return;
        }

        var width = right - left;
        var height = bottom - top;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var data = SKData.Create(info.BytesSize);
        var pixels = data.Span;
        RasterPainter.Paint(MemoryMarshal.Cast<byte, float>(valueTexture.Data), valueTexture.Size, MemoryMarshal.Cast<byte, Rgba8>(table.Data).Slice(0, table.Size.Width), MemoryMarshal.Cast<byte, Rgba8>(pixels), new PixelSize(width, height), left, top, bounds, style);
        var picture = SKImage.FromPixels(info, data, info.RowBytes);
        _glyphImages.Add(picture);
        _canvas.DrawImage(picture, left, top, new SKSamplingOptions(SKFilterMode.Nearest), null);
        Device.DrawCallCount++;
    }

    public void Submit()
    {
        ThrowIfNotRecording();
        Target!.ThrowIfDisposed();
        foreach (var buffer in _usedBuffers)
        {
            buffer.ThrowIfDisposed();
        }

        var picture = _recorder.EndRecording();
        if (Target.KeepsCommands)
        {
            Target.Keep(picture);
        }
        else
        {
            Device.Play(Target, picture);
        }

        _submitted = true;
        Device.OnFrameClosed(this);
    }

    protected override void OnDisposed()
    {
        foreach (var image in _glyphImages)
        {
            image.Dispose();
        }

        _glyphImages.Clear();
        _usedBuffers.Clear();
        if (!_submitted)
        {
            _recorder.EndRecording().Dispose();
        }

        if (Device.IsDisposed)
        {
            Release();
        }
        else
        {
            Device.OnFrameClosed(this);
            Device.ReturnFrame(this);
        }
    }

    private static SKColor ToSkia(Rgba8 color) => new(color.R, color.G, color.B, color.A);

    private void ThrowIfNotRecording()
    {
        ThrowIfDisposed();
        if (_submitted)
        {
            throw new InvalidOperationException("The frame was already submitted.");
        }
    }
}
