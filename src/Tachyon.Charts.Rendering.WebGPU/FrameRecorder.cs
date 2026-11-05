using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// Records the commands of a frame and encodes them into one render pass. Its arrays and GPU buffers
/// survive between frames, so a frame of the same size does not allocate here.
/// </summary>
internal sealed unsafe class FrameRecorder : IDisposable
{
    private const int InitialCapacity = 1024;
    private const int PointSize = 8;

    private readonly WebGpuRenderDevice _device;
    private readonly uint _uniformStride;

    private Native.ShaderModule* _flatShader;
    private Native.ShaderModule* _polylineShader;
    private readonly Native.ShaderModule* _markerShader;
    private readonly Native.ShaderModule* _imageShader;
    private Native.RenderPipeline* _imagePipeline;
    private readonly Native.ShaderModule* _rasterShader;
    private Native.RenderPipeline* _rasterPipeline;
    private Native.RenderPipeline* _glowPipeline;
    private Native.BindGroupLayout* _valuesLayout;
    private Native.PipelineLayout* _rasterLayout;
    private Native.BindGroupLayout* _bindGroupLayout;
    private Native.PipelineLayout* _pipelineLayout;
    private readonly Dictionary<Native.TextureFormat, (nint Flat, nint Polyline, nint Marker, nint Text, nint Image, nint Raster, nint Glow)> _pipelines = [];
    private Native.RenderPipeline* _flatPipeline;
    private Native.RenderPipeline* _polylinePipeline;
    private Native.RenderPipeline* _markerPipeline;
    private Native.RenderPipeline* _textPipeline;
    private readonly Native.ShaderModule* _textShader;
    private Native.BindGroupLayout* _atlasLayout;
    private Native.PipelineLayout* _textLayout;
    private Native.Buffer* _glyphBuffer;
    private ulong _glyphBufferSize;
    private GlyphInstance[] _glyphs = new GlyphInstance[InitialCapacity];
    private int _glyphCount;

    private Native.Buffer* _vertexBuffer;
    private ulong _vertexBufferSize;
    private Native.Buffer* _uniformBuffer;
    private ulong _uniformBufferSize;
    private Native.BindGroup* _bindGroup;

    private FlatVertex[] _vertices = new FlatVertex[InitialCapacity];
    private int _vertexCount;
    private DrawOp[] _ops = new DrawOp[InitialCapacity];
    private int _opCount;
    private byte[] _uniforms = [];
    private int _transformedCount;
    private bool _hasClear;
    private float? _turn;
    private Rgba8 _clearColor;

    // Frames waiting for the common upload and submit. The arrays above hold them one after the other.
    private readonly List<PendingFrame> _pending = [];
    private bool _isBatching;
    private int _firstOp;
    private int _firstVertex;
    private int _firstGlyph;
    private int _firstSlot;

    public FrameRecorder(WebGpuRenderDevice device)
    {
        _device = device;
        _uniformStride = (uint)RenderGuard.AlignUp(Unsafe.SizeOf<DrawParams>(), device.MinUniformOffsetAlignment);

        _flatShader = device.CompileShader("flat.wgsl", WebGpuRenderDevice.LoadShaderSource("flat.wgsl"));
        _polylineShader = device.CompileShader("polyline.wgsl", WebGpuRenderDevice.LoadShaderSource("polyline.wgsl"));
        _markerShader = device.CompileShader("marker.wgsl", WebGpuRenderDevice.LoadShaderSource("marker.wgsl"));
        _imageShader = device.CompileShader("image.wgsl", WebGpuRenderDevice.LoadShaderSource("image.wgsl"));
        _textShader = device.CompileShader("text.wgsl", WebGpuRenderDevice.LoadShaderSource("text.wgsl"));
        _rasterShader = device.CompileShader("raster.wgsl", WebGpuRenderDevice.LoadShaderSource("raster.wgsl"));
        CreatePipelineLayout();
    }

    public void Reset()
    {
        // Drops the buffer references of the frame in progress. Waiting frames stay.
        Array.Clear(_ops, _firstOp, _opCount - _firstOp);
        _opCount = _firstOp;
        _vertexCount = _firstVertex;
        _glyphCount = _firstGlyph;
        _transformedCount = 0;
        _hasClear = false;
        _turn = null;
    }

    /// <summary>Turns the following commands a quarter turn; see <see cref="ICommandList.SetQuarterTurn"/>.</summary>
    /// <param name="sum">The sum that places the turned picture, or <see langword="null"/> to stop.</param>
    public void SetQuarterTurn(float? sum) => _turn = sum;

    // Points computed on the CPU turn at once; points the GPU computes turn in the shader.
    private Vector2 Turned(Vector2 point) => _turn is { } sum ? QuarterTurn.Apply(sum, point) : point;

    private PixelRect? Turned(PixelRect? clip) => _turn is { } sum && clip is { } bounds ? QuarterTurn.Apply(sum, bounds) : clip;

    /// <summary>Collects the following frames until <see cref="EndBatch"/>.</summary>
    public void BeginBatch()
    {
        _isBatching = true;
        _device.Uploads.IsActive = true;
    }

    /// <summary>Uploads and submits all collected frames at once.</summary>
    public void EndBatch()
    {
        _isBatching = false;
        _device.Uploads.IsActive = false;
        Flush();
        _device.Uploads.Flush();
    }

    public void Clear(Rgba8 color)
    {
        // A clear overwrites everything, so the commands before it have no effect.
        Reset();
        _hasClear = true;
        _clearColor = color;
    }

    public void FillRectangle(PixelRect bounds, Rgba8 color)
    {
        if (!RenderGuard.IsDrawable(bounds))
        {
            return;
        }

        var right = bounds.X + bounds.Width;
        var bottom = bounds.Y + bounds.Height;
        var topLeft = new Vector2(bounds.X, bounds.Y);
        var bottomRight = new Vector2(right, bottom);
        AddTriangle(topLeft, new Vector2(right, bounds.Y), bottomRight, color);
        AddTriangle(topLeft, bottomRight, new Vector2(bounds.X, bottom), color);
    }

    public void FillLinearGradient(PixelRect bounds, Rgba8 startColor, Rgba8 endColor, bool vertical)
    {
        if (!RenderGuard.IsDrawable(bounds))
        {
            return;
        }

        var right = bounds.X + bounds.Width;
        var bottom = bounds.Y + bounds.Height;
        var topLeftColor = startColor;
        var topRightColor = vertical ? startColor : endColor;
        var bottomLeftColor = vertical ? endColor : startColor;
        var bottomRightColor = endColor;
        AddTriangle(
            new Vector2(bounds.X, bounds.Y),
            new Vector2(right, bounds.Y),
            new Vector2(right, bottom),
            topLeftColor,
            topRightColor,
            bottomRightColor);
        AddTriangle(
            new Vector2(bounds.X, bounds.Y),
            new Vector2(right, bottom),
            new Vector2(bounds.X, bottom),
            topLeftColor,
            bottomRightColor,
            bottomLeftColor);
    }

    public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color)
    {
        if (RenderGuard.IsDrawable(a, b, c))
        {
            AddTriangle(a, b, c, color);
        }
    }

    public void FillMesh(ReadOnlySpan<MeshVertex> vertices, PixelRect? clip)
    {
        var count = vertices.Length - (vertices.Length % 3);
        if (count == 0)
        {
            return;
        }

        if (_vertexCount + count > _vertices.Length)
        {
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _vertexCount + count));
        }

        NextOp() = new DrawOp { First = _vertexCount, Count = count, IsMesh = true, Clip = Turned(clip) };
        for (var i = 0; i < count; i++)
        {
            _vertices[_vertexCount++] = new FlatVertex(Turned(vertices[i].Position), vertices[i].Color);
        }
    }

    public void DrawPolyline(WebGpuBuffer points, int firstPoint, int pointCount, in PolylineStyle style)
    {
        _transformedCount++;
        ref var op = ref NextOp();
        op = new DrawOp
        {
            Points = points,
            First = firstPoint,
            Count = pointCount,
            Width = style.WidthInPixels,
            Color = style.Color,
            Scale = style.Scale,
            Offset = style.Offset,
            Clip = Turned(style.Clip),
            Turn = _turn,
            UniformSlot = _transformedCount,
            GlowRadius = style.GlowRadius,
            GlowIntensity = style.GlowIntensity,
        };
    }

    public void DrawImage(WebGpuTexture image, PixelRect bounds)
    {
        _transformedCount++;
        NextOp() = new DrawOp
        {
            Atlas = image,
            IsImage = true,
            Scale = new Vector2(bounds.Width, bounds.Height),
            Offset = new Vector2(bounds.X, bounds.Y),
            Turn = _turn,
            UniformSlot = _transformedCount,
        };
    }

    public void DrawRaster(WebGpuTexture values, WebGpuTexture colormap, PixelRect bounds, in RasterStyle style)
    {
        _transformedCount++;
        NextOp() = new DrawOp
        {
            Atlas = values,
            Table = colormap,
            IsImage = true,
            Scale = new Vector2(bounds.Width, bounds.Height),
            Offset = new Vector2(bounds.X, bounds.Y),
            RasterStyle = style,
            Turn = _turn,
            UniformSlot = _transformedCount,
        };
    }

    public void DrawMarkers(WebGpuBuffer markers, int firstMarker, int markerCount, in MarkerStyle style)
    {
        _transformedCount++;
        NextOp() = new DrawOp
        {
            Points = markers,
            First = firstMarker,
            Count = markerCount,
            IsMarkers = true,
            MarkerStyle = style,
            Clip = Turned(style.Clip),
            Turn = _turn,
            UniformSlot = _transformedCount,
        };
    }

    public void DrawGlyphs(WebGpuTexture atlas, ReadOnlySpan<GlyphInstance> glyphs)
    {
        if (glyphs.IsEmpty)
        {
            return;
        }

        if (_glyphCount + glyphs.Length > _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(_glyphs.Length * 2, _glyphCount + glyphs.Length));
        }

        NextOp() = new DrawOp { Atlas = atlas, First = _glyphCount, Count = glyphs.Length };
        glyphs.CopyTo(_glyphs.AsSpan(_glyphCount));
        _glyphCount += glyphs.Length;
    }

    public void Submit(WebGpuTarget target)
    {
        _turn = null;
        for (var i = _firstOp; i < _opCount; i++)
        {
            _ops[i].Points?.ThrowIfDisposed();
            _ops[i].Atlas?.ThrowIfDisposed();
            _ops[i].Table?.ThrowIfDisposed();
        }

        _pending.Add(new PendingFrame(target, _firstOp, _opCount, _firstSlot, _hasClear, _clearColor));
        _firstSlot += _transformedCount + 1;
        _firstOp = _opCount;
        _firstVertex = _vertexCount;
        _firstGlyph = _glyphCount;
        _transformedCount = 0;
        _hasClear = false;
        if (!_isBatching)
        {
            Flush();
        }
    }

    private void Flush()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        // wgpu aborts the process on a submit to a lost device.
        if (_device.IsLost)
        {
            throw new DeviceLostException();
        }

        _device.Uploads.Flush();
        var api = WebGpuRenderDevice.Api;
        UploadVertices();
        UploadGlyphs();
        UploadUniforms();

        // One submit for all frames: it costs far more per call than the commands inside.
        var count = _pending.Count;
        var views = new nint[count];
        var buffers = new nint[count];
        var used = 0;
        for (var i = 0; i < count; i++)
        {
            var frame = _pending[i];

            // Acquired this late so that a discarded frame never holds a swapchain image.
            var view = frame.Target.AcquireView();
            views[i] = (nint)view;
            if (view is not null)
            {
                var encoder = api.DeviceCreateCommandEncoder(_device.Device, null);
                Encode(encoder, frame, view);
                buffers[used++] = (nint)api.CommandEncoderFinish(encoder, null);
                api.CommandEncoderRelease(encoder);
                if (frame.Target is WebGpuSurfaceTarget)
                {
                    // Several swapchain images in one submit lose the device on D3D12.
                    // Seen with wgpu-native of Silk.NET.WebGPU 2.23.0.
                    SubmitBuffers(buffers, used);
                    used = 0;
                    frame.Target.CompleteFrame(view);
                    views[i] = 0;
                }
            }
        }

        SubmitBuffers(buffers, used);

        for (var i = 0; i < count; i++)
        {
            if (views[i] != 0)
            {
                _pending[i].Target.CompleteFrame((Native.TextureView*)views[i]);
            }
        }

        _pending.Clear();
        Array.Clear(_ops, 0, _opCount);
        _opCount = 0;
        _vertexCount = 0;
        _glyphCount = 0;
        _firstOp = 0;
        _firstVertex = 0;
        _firstGlyph = 0;
        _firstSlot = 0;
    }

    private void SubmitBuffers(nint[] buffers, int count)
    {
        if (count == 0)
        {
            return;
        }

        var api = WebGpuRenderDevice.Api;
        fixed (nint* pointer = buffers)
        {
            api.QueueSubmit(_device.Queue, (nuint)count, (Native.CommandBuffer**)pointer);
        }

        for (var i = 0; i < count; i++)
        {
            api.CommandBufferRelease((Native.CommandBuffer*)buffers[i]);
        }
    }

    private void Encode(Native.CommandEncoder* encoder, in PendingFrame frame, Native.TextureView* view)
    {
        var api = WebGpuRenderDevice.Api;
        SelectPipelines(frame.Target.Format);
        var clear = frame.ClearColor;
        var attachment = new Native.RenderPassColorAttachment
        {
            View = view,
            DepthSlice = uint.MaxValue,
            LoadOp = frame.HasClear ? Native.LoadOp.Clear : Native.LoadOp.Load,
            StoreOp = Native.StoreOp.Store,
            ClearValue = new Native.Color(clear.R / 255.0, clear.G / 255.0, clear.B / 255.0, clear.A / 255.0),
        };
        var passDescriptor = new Native.RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &attachment,
        };
        var pass = api.CommandEncoderBeginRenderPass(encoder, &passDescriptor);
        EncodeOps(pass, frame);
        api.RenderPassEncoderEnd(pass);
        api.RenderPassEncoderRelease(pass);
    }

    public void Dispose()
    {
        var api = WebGpuRenderDevice.Api;
        ReleaseBindGroup();
        ReleaseBuffer(ref _vertexBuffer);
        ReleaseBuffer(ref _uniformBuffer);
        ReleaseBuffer(ref _glyphBuffer);
        foreach (var (flat, polyline, marker, text, image, raster, glow) in _pipelines.Values)
        {
            api.RenderPipelineRelease((Native.RenderPipeline*)glow);
            api.RenderPipelineRelease((Native.RenderPipeline*)raster);
            api.RenderPipelineRelease((Native.RenderPipeline*)image);
            api.RenderPipelineRelease((Native.RenderPipeline*)flat);
            api.RenderPipelineRelease((Native.RenderPipeline*)polyline);
            api.RenderPipelineRelease((Native.RenderPipeline*)marker);
            api.RenderPipelineRelease((Native.RenderPipeline*)text);
        }

        api.PipelineLayoutRelease(_rasterLayout);
        api.BindGroupLayoutRelease(_valuesLayout);
        api.ShaderModuleRelease(_rasterShader);
        api.PipelineLayoutRelease(_textLayout);
        api.BindGroupLayoutRelease(_atlasLayout);
        api.ShaderModuleRelease(_textShader);

        api.PipelineLayoutRelease(_pipelineLayout);
        api.BindGroupLayoutRelease(_bindGroupLayout);
        api.ShaderModuleRelease(_flatShader);
        api.ShaderModuleRelease(_polylineShader);
        api.ShaderModuleRelease(_markerShader);
        api.ShaderModuleRelease(_imageShader);
        _flatPipeline = null;
        _polylinePipeline = null;
        _pipelineLayout = null;
        _bindGroupLayout = null;
        _flatShader = null;
        _polylineShader = null;
    }

    private ref DrawOp NextOp()
    {
        if (_opCount == _ops.Length)
        {
            Array.Resize(ref _ops, _ops.Length * 2);
        }

        return ref _ops[_opCount++];
    }

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 color)
        => AddTriangle(a, b, c, color, color, color);

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Rgba8 colorA, Rgba8 colorB, Rgba8 colorC)
    {
        if (_vertexCount + 3 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        // Neighboring triangles share one draw call.
        if (_opCount == _firstOp || _ops[_opCount - 1].Points is not null || _ops[_opCount - 1].Atlas is not null || _ops[_opCount - 1].IsMesh)
        {
            NextOp() = new DrawOp { First = _vertexCount };
        }

        _ops[_opCount - 1].Count += 3;
        _vertices[_vertexCount++] = new FlatVertex(Turned(a), colorA);
        _vertices[_vertexCount++] = new FlatVertex(Turned(b), colorB);
        _vertices[_vertexCount++] = new FlatVertex(Turned(c), colorC);
    }

    private void UploadVertices()
    {
        if (_vertexCount == 0)
        {
            return;
        }

        var byteCount = (ulong)(_vertexCount * Unsafe.SizeOf<FlatVertex>());
        if (byteCount > _vertexBufferSize)
        {
            ReleaseBuffer(ref _vertexBuffer);
            _vertexBufferSize = Math.Max(byteCount, _vertexBufferSize * 2);
            _vertexBuffer = CreateBuffer(_vertexBufferSize, Native.BufferUsage.Vertex);
        }

        fixed (FlatVertex* pointer = _vertices)
        {
            WebGpuRenderDevice.Api.QueueWriteBuffer(_device.Queue, _vertexBuffer, 0, pointer, (nuint)byteCount);
        }
    }

    private void UploadGlyphs()
    {
        if (_glyphCount == 0)
        {
            return;
        }

        var byteCount = (ulong)(_glyphCount * Unsafe.SizeOf<GlyphInstance>());
        if (byteCount > _glyphBufferSize)
        {
            ReleaseBuffer(ref _glyphBuffer);
            _glyphBufferSize = Math.Max(byteCount, _glyphBufferSize * 2);
            _glyphBuffer = CreateBuffer(_glyphBufferSize, Native.BufferUsage.Vertex);
        }

        fixed (GlyphInstance* pointer = _glyphs)
        {
            WebGpuRenderDevice.Api.QueueWriteBuffer(_device.Queue, _glyphBuffer, 0, pointer, (nuint)byteCount);
        }
    }

    private void UploadUniforms()
    {
        var byteCount = (int)_uniformStride * _firstSlot;
        if (_uniforms.Length < byteCount)
        {
            _uniforms = new byte[byteCount];
        }

        foreach (var frame in _pending)
        {
            WriteFrameUniforms(frame);
        }

        if ((ulong)byteCount > _uniformBufferSize)
        {
            ReleaseBindGroup();
            ReleaseBuffer(ref _uniformBuffer);
            _uniformBufferSize = Math.Max((ulong)byteCount, _uniformBufferSize * 2);
            _uniformBuffer = CreateBuffer(_uniformBufferSize, Native.BufferUsage.Uniform);
            CreateBindGroup();
        }

        fixed (byte* pointer = _uniforms)
        {
            WebGpuRenderDevice.Api.QueueWriteBuffer(_device.Queue, _uniformBuffer, 0, pointer, (nuint)byteCount);
        }
    }

    private void WriteFrameUniforms(in PendingFrame frame)
    {
        // Slot 0 of a frame serves flat and text pipelines, transformed batches each have their own slot.
        var targetSize = frame.Target.Size;
        var size = new Vector2(targetSize.Width, targetSize.Height);
        var firstSlot = frame.FirstSlot;
        WriteUniforms(firstSlot, new DrawParams { TargetSize = size });
        for (var i = frame.FirstOp; i < frame.EndOp; i++)
        {
            ref readonly var op = ref _ops[i];
            WriteOpUniforms(firstSlot, size, op);
            if (op.Turn is { } sum && (op.Points is not null || op.IsImage))
            {
                MemoryMarshal.Write(_uniforms.AsSpan(((firstSlot + op.UniformSlot) * (int)_uniformStride) + 128), new Vector4(1, sum, 0, 0));
            }
        }
    }

    private void WriteOpUniforms(int firstSlot, Vector2 size, in DrawOp op)
    {
        var slot = firstSlot + op.UniformSlot;
        if (op.Table is not null)
        {
            WriteRasterUniforms(slot, size, op);
        }
        else if (op.IsImage)
        {
            WriteUniforms(slot, new DrawParams { TargetSize = size, Scale = op.Scale, Offset = op.Offset });
        }
        else if (op.Points is not null && op.IsMarkers)
        {
            WriteMarkerUniforms(slot, size, op);
        }
        else if (op.Points is not null)
        {
            var color = new Vector4(op.Color.R, op.Color.G, op.Color.B, op.Color.A) / 255f;
            WriteUniforms(slot, new DrawParams { TargetSize = size, Width = op.Width, Color = color, Scale = op.Scale, Offset = op.Offset, SizeScale = op.GlowRadius, GlowIntensity = op.GlowIntensity });
        }
    }

    private void WriteRasterUniforms(int slot, Vector2 size, in DrawOp op)
    {
        ref readonly var raster = ref op.RasterStyle;
        WriteUniforms(slot, new DrawParams
        {
            TargetSize = size,
            Scale = op.Scale,
            Offset = op.Offset,
            Width = raster.IsInterpolated ? 1 : 0,
            Color = new Vector4((float)raster.Reduction, 0, 0, 0),
            SizeScale = raster.IsLogarithmic ? 1 : 0,
            Shape = (uint)raster.RowShift,
            ValueMinimum = raster.Minimum,
            ValueMaximum = raster.Maximum,
            SectionOrigin = raster.SectionOrigin,
            SectionSize = raster.SectionSize,
            MissingColor = ToVector(raster.MissingColor),
            BelowColor = raster.BelowColor is { } below ? ToVector(below) : new Vector4(-1),
            AboveColor = raster.AboveColor is { } above ? ToVector(above) : new Vector4(-1),
        });
    }

    private void WriteMarkerUniforms(int slot, Vector2 size, in DrawOp op)
    {
        var border = op.MarkerStyle.BorderColor;
        WriteUniforms(slot, new DrawParams
        {
            TargetSize = size,
            Scale = op.MarkerStyle.Scale,
            Offset = op.MarkerStyle.Offset,
            Color = new Vector4(border.R, border.G, border.B, border.A) / 255f,
            Width = op.MarkerStyle.BorderWidthInPixels,
            SizeScale = op.MarkerStyle.SizeScale,
            Shape = (uint)op.MarkerStyle.Shape,
        });
    }

    private static Vector4 ToVector(Rgba8 color) => new Vector4(color.R, color.G, color.B, color.A) / 255f;

    private void WriteUniforms(int slot, in DrawParams values) =>
        MemoryMarshal.Write(_uniforms.AsSpan(slot * (int)_uniformStride), in values);

    private static void SetScissor(Native.RenderPassEncoder* pass, in PixelRect clip, PixelSize target)
    {
        var left = (uint)Math.Clamp((int)MathF.Floor(clip.X), 0, target.Width);
        var top = (uint)Math.Clamp((int)MathF.Floor(clip.Y), 0, target.Height);
        var right = (uint)Math.Clamp((int)MathF.Ceiling(clip.X + clip.Width), (int)left, target.Width);
        var bottom = (uint)Math.Clamp((int)MathF.Ceiling(clip.Y + clip.Height), (int)top, target.Height);
        WebGpuRenderDevice.Api.RenderPassEncoderSetScissorRect(pass, left, top, right - left, bottom - top);
    }

    private void EncodeOps(Native.RenderPassEncoder* pass, in PendingFrame frame)
    {
        var api = WebGpuRenderDevice.Api;
        var target = frame.Target.Size;
        for (var i = frame.FirstOp; i < frame.EndOp; i++)
        {
            ref readonly var op = ref _ops[i];
            var uniformOffset = (uint)(frame.FirstSlot + op.UniformSlot) * _uniformStride;
            api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 1, &uniformOffset);

            _device.DrawCallCount++;
            if (op.Table is { } table)
            {
                api.RenderPassEncoderSetPipeline(pass, _rasterPipeline);
                api.RenderPassEncoderSetBindGroup(pass, 1, op.Atlas!.GetBindGroup(_valuesLayout), 0, null);
                api.RenderPassEncoderSetBindGroup(pass, 2, table.GetBindGroup(_atlasLayout), 0, null);
                api.RenderPassEncoderDraw(pass, 6, 1, 0, 0);
            }
            else if (op.IsImage)
            {
                api.RenderPassEncoderSetPipeline(pass, _imagePipeline);
                api.RenderPassEncoderSetBindGroup(pass, 1, op.Atlas!.GetBindGroup(_atlasLayout), 0, null);
                api.RenderPassEncoderDraw(pass, 6, 1, 0, 0);
            }
            else if (op.Atlas is { } atlas)
            {
                var stride = (ulong)Unsafe.SizeOf<GlyphInstance>();
                api.RenderPassEncoderSetPipeline(pass, _textPipeline);
                api.RenderPassEncoderSetBindGroup(pass, 1, atlas.GetBindGroup(_atlasLayout), 0, null);
                api.RenderPassEncoderSetVertexBuffer(pass, 0, _glyphBuffer, (ulong)op.First * stride, (ulong)op.Count * stride);
                api.RenderPassEncoderDraw(pass, 6, (uint)op.Count, 0, 0);
            }
            else if (op.Points is { } points)
            {
                EncodePoints(pass, op, points, target);
            }
            else
            {
                EncodeFlat(pass, op, target);
            }
        }
    }

    private void EncodePoints(Native.RenderPassEncoder* pass, in DrawOp op, WebGpuBuffer points, PixelSize target)
    {
        var api = WebGpuRenderDevice.Api;
        if (op.IsMarkers)
        {
            var stride = (ulong)Unsafe.SizeOf<MarkerInstance>();
            api.RenderPassEncoderSetPipeline(pass, _markerPipeline);
            api.RenderPassEncoderSetVertexBuffer(pass, 0, points.Handle, (ulong)op.First * stride, (ulong)op.Count * stride);
        }
        else
        {
            var start = (ulong)op.First * PointSize;
            var bytes = (ulong)(op.Count - 1) * PointSize;

            // Two views of one buffer: WebGPU forbids an attribute beyond the array stride.
            api.RenderPassEncoderSetVertexBuffer(pass, 0, points.Handle, start, bytes);
            api.RenderPassEncoderSetVertexBuffer(pass, 1, points.Handle, start + PointSize, bytes);
            if (op.GlowRadius > 0)
            {
                if (op.Clip is { } glowClip)
                {
                    SetScissor(pass, glowClip, target);
                }

                api.RenderPassEncoderSetPipeline(pass, _glowPipeline);
                api.RenderPassEncoderDraw(pass, 6, (uint)op.Count - 1, 0, 0);
            }

            api.RenderPassEncoderSetPipeline(pass, _polylinePipeline);
        }

        if (op.Clip is { } clip)
        {
            SetScissor(pass, clip, target);
        }

        api.RenderPassEncoderDraw(pass, 6, op.IsMarkers ? (uint)op.Count : (uint)op.Count - 1, 0, 0);
        if (op.Clip is not null)
        {
            api.RenderPassEncoderSetScissorRect(pass, 0, 0, (uint)target.Width, (uint)target.Height);
        }
    }

    private void EncodeFlat(Native.RenderPassEncoder* pass, in DrawOp op, PixelSize target)
    {
        var api = WebGpuRenderDevice.Api;
        api.RenderPassEncoderSetPipeline(pass, _flatPipeline);
        api.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, _vertexBufferSize);
        if (op.Clip is { } clip)
        {
            SetScissor(pass, clip, target);
        }

        api.RenderPassEncoderDraw(pass, (uint)op.Count, 1, (uint)op.First, 0);
        if (op.Clip is not null)
        {
            api.RenderPassEncoderSetScissorRect(pass, 0, 0, (uint)target.Width, (uint)target.Height);
        }
    }

    private Native.Buffer* CreateBuffer(ulong size, Native.BufferUsage usage)
    {
        var descriptor = new Native.BufferDescriptor { Usage = usage | Native.BufferUsage.CopyDst, Size = size };
        return WebGpuRenderDevice.Api.DeviceCreateBuffer(_device.Device, &descriptor);
    }

    private static void ReleaseBuffer(ref Native.Buffer* buffer)
    {
        if (buffer is not null)
        {
            WebGpuRenderDevice.Api.BufferRelease(buffer);
            buffer = null;
        }
    }

    private void ReleaseBindGroup()
    {
        if (_bindGroup is not null)
        {
            WebGpuRenderDevice.Api.BindGroupRelease(_bindGroup);
            _bindGroup = null;
        }
    }

    private void CreateBindGroup()
    {
        var entry = new Native.BindGroupEntry
        {
            Binding = 0,
            Buffer = _uniformBuffer,
            Size = (ulong)Unsafe.SizeOf<DrawParams>(),
        };
        var descriptor = new Native.BindGroupDescriptor
        {
            Layout = _bindGroupLayout,
            EntryCount = 1,
            Entries = &entry,
        };
        _bindGroup = WebGpuRenderDevice.Api.DeviceCreateBindGroup(_device.Device, &descriptor);
    }

    private void CreatePipelineLayout()
    {
        var api = WebGpuRenderDevice.Api;
        var entry = new Native.BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = Native.ShaderStage.Vertex | Native.ShaderStage.Fragment,
            Buffer = new Native.BufferBindingLayout
            {
                Type = Native.BufferBindingType.Uniform,
                HasDynamicOffset = true,
                MinBindingSize = (ulong)Unsafe.SizeOf<DrawParams>(),
            },
        };
        var groupDescriptor = new Native.BindGroupLayoutDescriptor { EntryCount = 1, Entries = &entry };
        _bindGroupLayout = api.DeviceCreateBindGroupLayout(_device.Device, &groupDescriptor);

        var groupLayout = _bindGroupLayout;
        var layoutDescriptor = new Native.PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &groupLayout };
        _pipelineLayout = api.DeviceCreatePipelineLayout(_device.Device, &layoutDescriptor);

        var atlasEntries = stackalloc Native.BindGroupLayoutEntry[]
        {
            new()
            {
                Binding = 0,
                Visibility = Native.ShaderStage.Fragment,
                Texture = new Native.TextureBindingLayout
                {
                    SampleType = Native.TextureSampleType.Float,
                    ViewDimension = Native.TextureViewDimension.Dimension2D,
                },
            },
        };
        var atlasDescriptor = new Native.BindGroupLayoutDescriptor { EntryCount = 1, Entries = atlasEntries };
        _atlasLayout = api.DeviceCreateBindGroupLayout(_device.Device, &atlasDescriptor);

        var groupLayouts = stackalloc Native.BindGroupLayout*[] { _bindGroupLayout, _atlasLayout };
        var textDescriptor = new Native.PipelineLayoutDescriptor { BindGroupLayoutCount = 2, BindGroupLayouts = groupLayouts };
        _textLayout = api.DeviceCreatePipelineLayout(_device.Device, &textDescriptor);

        // A value texture of R32Float cannot be filtered, so it has a layout of its own.
        var valuesEntry = atlasEntries[0];
        valuesEntry.Texture.SampleType = Native.TextureSampleType.UnfilterableFloat;
        var valuesDescriptor = new Native.BindGroupLayoutDescriptor { EntryCount = 1, Entries = &valuesEntry };
        _valuesLayout = api.DeviceCreateBindGroupLayout(_device.Device, &valuesDescriptor);

        var rasterLayouts = stackalloc Native.BindGroupLayout*[] { _bindGroupLayout, _valuesLayout, _atlasLayout };
        var rasterDescriptor = new Native.PipelineLayoutDescriptor { BindGroupLayoutCount = 3, BindGroupLayouts = rasterLayouts };
        _rasterLayout = api.DeviceCreatePipelineLayout(_device.Device, &rasterDescriptor);
    }

    private void SelectPipelines(Native.TextureFormat format)
    {
        if (!_pipelines.TryGetValue(format, out var pair))
        {
            pair = CreatePipelines(format);
            _pipelines.Add(format, pair);
        }

        _flatPipeline = (Native.RenderPipeline*)pair.Flat;
        _polylinePipeline = (Native.RenderPipeline*)pair.Polyline;
        _markerPipeline = (Native.RenderPipeline*)pair.Marker;
        _textPipeline = (Native.RenderPipeline*)pair.Text;
        _imagePipeline = (Native.RenderPipeline*)pair.Image;
        _rasterPipeline = (Native.RenderPipeline*)pair.Raster;
        _glowPipeline = (Native.RenderPipeline*)pair.Glow;
    }

    private (nint Flat, nint Polyline, nint Marker, nint Text, nint Image, nint Raster, nint Glow) CreatePipelines(Native.TextureFormat format)
    {
        var flatAttributes = stackalloc Native.VertexAttribute[]
        {
            new() { Format = Native.VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 },
            new() { Format = Native.VertexFormat.Unorm8x4, Offset = PointSize, ShaderLocation = 1 },
        };
        var flatLayout = new Native.VertexBufferLayout
        {
            ArrayStride = (ulong)Unsafe.SizeOf<FlatVertex>(),
            StepMode = Native.VertexStepMode.Vertex,
            AttributeCount = 2,
            Attributes = flatAttributes,
        };
        var flat = CreatePipeline(_flatShader, _pipelineLayout, &flatLayout, 1, format);

        var startAttribute = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x2, ShaderLocation = 0 };
        var endAttribute = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x2, ShaderLocation = 1 };
        var polylineLayouts = stackalloc Native.VertexBufferLayout[]
        {
            new() { ArrayStride = PointSize, StepMode = Native.VertexStepMode.Instance, AttributeCount = 1, Attributes = &startAttribute },
            new() { ArrayStride = PointSize, StepMode = Native.VertexStepMode.Instance, AttributeCount = 1, Attributes = &endAttribute },
        };
        var polyline = CreatePipeline(_polylineShader, _pipelineLayout, polylineLayouts, 2, format);
        var glow = CreatePipeline(_polylineShader, _pipelineLayout, polylineLayouts, 2, format, isGlow: true);

        var markerAttributes = stackalloc Native.VertexAttribute[]
        {
            new() { Format = Native.VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 },
            new() { Format = Native.VertexFormat.Float32, Offset = 8, ShaderLocation = 1 },
            new() { Format = Native.VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 2 },
        };
        var markerLayout = new Native.VertexBufferLayout
        {
            ArrayStride = (ulong)Unsafe.SizeOf<MarkerInstance>(),
            StepMode = Native.VertexStepMode.Instance,
            AttributeCount = 3,
            Attributes = markerAttributes,
        };
        var marker = CreatePipeline(_markerShader, _pipelineLayout, &markerLayout, 1, format);

        // The fields of GlyphInstance in order.
        var glyphAttributes = stackalloc Native.VertexAttribute[]
        {
            new() { Format = Native.VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 },
            new() { Format = Native.VertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 },
            new() { Format = Native.VertexFormat.Float32x2, Offset = 16, ShaderLocation = 2 },
            new() { Format = Native.VertexFormat.Float32x2, Offset = 24, ShaderLocation = 3 },
            new() { Format = Native.VertexFormat.Unorm8x4, Offset = 32, ShaderLocation = 4 },
            new() { Format = Native.VertexFormat.Float32, Offset = 36, ShaderLocation = 5 },
            new() { Format = Native.VertexFormat.Float32, Offset = 40, ShaderLocation = 6 },
            new() { Format = Native.VertexFormat.Float32x2, Offset = 44, ShaderLocation = 7 },
        };
        var glyphLayout = new Native.VertexBufferLayout
        {
            ArrayStride = (ulong)Unsafe.SizeOf<GlyphInstance>(),
            StepMode = Native.VertexStepMode.Instance,
            AttributeCount = 8,
            Attributes = glyphAttributes,
        };
        var text = CreatePipeline(_textShader, _textLayout, &glyphLayout, 1, format);

        var image = CreatePipeline(_imageShader, _textLayout, null, 0, format);

        var raster = CreatePipeline(_rasterShader, _rasterLayout, null, 0, format);

        return ((nint)flat, (nint)polyline, (nint)marker, (nint)text, (nint)image, (nint)raster, (nint)glow);
    }

    private Native.RenderPipeline* CreatePipeline(
        Native.ShaderModule* shader,
        Native.PipelineLayout* pipelineLayout,
        Native.VertexBufferLayout* layouts,
        nuint layoutCount,
        Native.TextureFormat format,
        bool isGlow = false)
    {
        var maximum = new Native.BlendComponent { Operation = Native.BlendOperation.Max, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.One };
        var blend = isGlow ? new Native.BlendState { Color = maximum, Alpha = maximum } : new Native.BlendState
        {
            Color = new Native.BlendComponent
            {
                Operation = Native.BlendOperation.Add,
                SrcFactor = Native.BlendFactor.SrcAlpha,
                DstFactor = Native.BlendFactor.OneMinusSrcAlpha,
            },
            Alpha = new Native.BlendComponent
            {
                Operation = Native.BlendOperation.Add,
                SrcFactor = Native.BlendFactor.One,
                DstFactor = Native.BlendFactor.OneMinusSrcAlpha,
            },
        };
        var colorTarget = new Native.ColorTargetState
        {
            Format = format,
            Blend = &blend,
            WriteMask = Native.ColorWriteMask.All,
        };

        fixed (byte* vertexEntry = "vs_main"u8, fragmentEntry = isGlow ? "fs_glow"u8 : "fs_main"u8)
        {
            var fragment = new Native.FragmentState
            {
                Module = shader,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Layout = pipelineLayout,
                Vertex = new Native.VertexState
                {
                    Module = shader,
                    EntryPoint = vertexEntry,
                    BufferCount = layoutCount,
                    Buffers = layouts,
                },
                Primitive = new Native.PrimitiveState
                {
                    Topology = Native.PrimitiveTopology.TriangleList,
                    FrontFace = Native.FrontFace.Ccw,
                    CullMode = Native.CullMode.None,
                },
                Multisample = new Native.MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment,
            };

            return WebGpuRenderDevice.Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FlatVertex(Vector2 Position, Rgba8 Color);

    // Prefix matches every WGSL Params struct; later fields are used by transformed primitives.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct DrawParams
    {
        [FieldOffset(0)]
        public Vector2 TargetSize;

        [FieldOffset(8)]
        public float Width;

        [FieldOffset(16)]
        public Vector4 Color;

        [FieldOffset(32)]
        public Vector2 Scale;

        [FieldOffset(40)]
        public Vector2 Offset;

        [FieldOffset(48)]
        public float SizeScale;

        [FieldOffset(52)]
        public uint Shape;

        [FieldOffset(52)]
        public float GlowIntensity;

        [FieldOffset(56)]
        public float ValueMinimum;

        [FieldOffset(60)]
        public float ValueMaximum;

        [FieldOffset(64)]
        public Vector2 SectionOrigin;

        [FieldOffset(72)]
        public Vector2 SectionSize;

        [FieldOffset(80)]
        public Vector4 MissingColor;

        [FieldOffset(96)]
        public Vector4 BelowColor;

        [FieldOffset(112)]
        public Vector4 AboveColor;

        // x is 1 for a quarter turn, y its sum; see QuarterTurn.
        [FieldOffset(128)]
        public Vector4 Turn;
    }

    private readonly record struct PendingFrame(WebGpuTarget Target, int FirstOp, int EndOp, int FirstSlot, bool HasClear, Rgba8 ClearColor);

    private struct DrawOp
    {
        public WebGpuBuffer? Points;
        public WebGpuTexture? Atlas;
        public WebGpuTexture? Table;
        public int First;
        public int Count;
        public float Width;
        public Rgba8 Color;
        public Vector2 Scale;
        public Vector2 Offset;
        public PixelRect? Clip;
        public int UniformSlot;
        public bool IsMarkers;
        public bool IsImage;
        public bool IsMesh;
        public float GlowRadius;
        public float GlowIntensity;
        public MarkerStyle MarkerStyle;
        public RasterStyle RasterStyle;
        public float? Turn;
    }
}
