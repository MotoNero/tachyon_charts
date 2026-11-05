using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.WebGPU;
using Tachyon.Charts.ThreeD.Scene;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>A point of a line with its color.</summary>
/// <param name="Position">The place in the box.</param>
/// <param name="Color">The color.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LineVertex(Vector3 Position, Rgba8 Color);

/// <summary>A point of a surface as the GPU reads it.</summary>
/// <param name="Position">The place in the box.</param>
/// <param name="Normal">The direction away from the surface.</param>
/// <param name="Color">The color.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct MeshVertex(Vector3 Position, Vector3 Normal, Rgba8 Color);

/// <summary>What the uniforms of a surface hold. The order follows mesh.wgsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MeshUniforms
{
    public Matrix4x4 Camera;
    public Vector4 Eye;
    public Vector4 Material;
    public CutPlanes Cuts;
}

/// <summary>What the uniforms of a raster surface hold. The order follows surface.wgsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceUniforms
{
    public Matrix4x4 Camera;
    public Vector4 Eye;
    public Vector4 Material;
    public uint Columns;
    public uint Rows;
    public uint FirstRow;
    public uint Unused;
    public Vector4 Origin;
    public Vector4 Colors;
    public Vector4 Contour;
    public Vector4 ContourColor;
    public Vector4 Planes;
    public CutPlanes Cuts;
}

/// <summary>The fixed part of the uniforms of a raster surface.</summary>
/// <param name="Columns">The number of columns.</param>
/// <param name="Rows">The number of rows.</param>
/// <param name="Origin">Left and front edge in the box, then the step per column and row.</param>
/// <param name="Colors">The heights at the ends of the colormap.</param>
/// <param name="FirstRow">The slot of the ring that the first row shows.</param>
/// <param name="LineSpacing">The distance of the curves in rows, 0 for a closed surface.</param>
/// <param name="Levels">The heights of the lines in the box, none without lines.</param>
/// <param name="Contours">The look of the lines.</param>
internal readonly record struct SurfaceShape(int Columns, int Rows, Vector4 Origin, Vector4 Colors, uint FirstRow = 0, int LineSpacing = 0, float[]? Levels = null, Contours3D? Contours = null);

/// <summary>
/// Draws the scene into a texture of its own with depth and 4x MSAA. The chart then shows the texture as an
/// image, so text, overlays and the frame order of the 2D pipeline stay as they are. The pointers
/// belong to the native WebGPU API, which has no safe form.
/// </summary>
internal sealed unsafe partial class Renderer3D : IDisposable
{
    private const uint SampleCount = 4;
    private const int LineUniformSize = 64;
    private const Native.TextureFormat ColorFormat = Native.TextureFormat.Rgba8Unorm;
    private const Native.TextureFormat DepthFormat = Native.TextureFormat.Depth32float;
    private const Native.TextureFormat AccumulationFormat = Native.TextureFormat.Rgba16float;
    private const Native.TextureFormat RevealFormat = Native.TextureFormat.R8Unorm;

    private readonly WebGpuRenderDevice _device;
    private readonly Native.Buffer* _lineUniforms;
    private readonly Native.BindGroup* _lineBindGroup;
    private readonly Native.RenderPipeline* _linePipeline;
    private readonly Native.RenderPipeline* _opaquePipeline;
    private readonly Native.RenderPipeline* _transparentPipeline;
    private readonly Native.RenderPipeline* _compositePipeline;
    private readonly Native.RenderPipeline* _surfaceProjection;
    private readonly Native.BindGroupLayout* _surfaceProjectionLayout;
    private readonly Native.RenderPipeline* _surfaceLines;
    private readonly Native.RenderPipeline* _surfaceDepth;
    private readonly Native.BindGroupLayout* _surfaceLinesLayout;
    private readonly Native.BindGroupLayout* _surfaceDepthLayout;
    private readonly Native.RenderPipeline* _opaqueSurface;
    private readonly Native.RenderPipeline* _transparentSurface;
    private readonly Native.BindGroupLayout* _opaqueSurfaceLayout;
    private readonly Native.BindGroupLayout* _transparentSurfaceLayout;
    private readonly Dictionary<object, SurfaceBuffers> _surfaces = [];
    private readonly Native.BindGroupLayout* _opaqueLayout;
    private readonly Native.BindGroupLayout* _transparentLayout;
    private readonly Dictionary<Mesh3D, MeshBuffers> _meshes = [];
    private Native.Buffer* _lines;
    private ulong _lineCapacity;
    private Attachment _msaaColor;
    private Attachment _depth;
    private Attachment _msaaAccumulation;
    private Attachment _msaaReveal;
    private Attachment _accumulation;
    private Attachment _reveal;
    private Native.BindGroup* _compositeBindGroup;
    private WebGpuTexture? _picture;
    private CutPlanes _cuts;
    private bool _disposed;

    public Renderer3D(WebGpuRenderDevice device)
    {
        _device = device;
        var uniforms = new Native.BufferDescriptor { Size = LineUniformSize, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
        _lineUniforms = Api.DeviceCreateBuffer(device.Device, &uniforms);

        var lineShader = device.CompileShader("lines.wgsl", ReadShader("lines.wgsl"));
        _linePipeline = CreateLinePipeline(lineShader);
        Api.ShaderModuleRelease(lineShader);
        var surfaceShader = device.CompileShader("surface.wgsl", ReadShader("clip.wgsl") + ReadShader("light.wgsl") + ReadShader("surface.wgsl"));
        _opaqueSurface = CreateMeshPipeline(surfaceShader, isTransparent: false, hasVertices: false);
        _transparentSurface = CreateMeshPipeline(surfaceShader, isTransparent: true, hasVertices: false);
        _surfaceLines = CreateMeshPipeline(surfaceShader, isTransparent: false, hasVertices: false, Native.PrimitiveTopology.LineList);
        _surfaceDepth = CreateMeshPipeline(surfaceShader, isTransparent: false, hasVertices: false, Native.PrimitiveTopology.TriangleList, isDepthOnly: true);
        _surfaceProjection = CreateProjectionPipeline(surfaceShader);
        _surfaceProjectionLayout = Api.RenderPipelineGetBindGroupLayout(_surfaceProjection, 0);
        Api.ShaderModuleRelease(surfaceShader);
        _surfaceLinesLayout = Api.RenderPipelineGetBindGroupLayout(_surfaceLines, 0);
        _surfaceDepthLayout = Api.RenderPipelineGetBindGroupLayout(_surfaceDepth, 0);
        _opaqueSurfaceLayout = Api.RenderPipelineGetBindGroupLayout(_opaqueSurface, 0);
        _transparentSurfaceLayout = Api.RenderPipelineGetBindGroupLayout(_transparentSurface, 0);
        var meshShader = device.CompileShader("mesh.wgsl", ReadShader("clip.wgsl") + ReadShader("light.wgsl") + ReadShader("mesh.wgsl"));
        _opaquePipeline = CreateMeshPipeline(meshShader, isTransparent: false, hasVertices: true);
        _transparentPipeline = CreateMeshPipeline(meshShader, isTransparent: true, hasVertices: true);
        Api.ShaderModuleRelease(meshShader);
        var compositeShader = device.CompileShader("composite.wgsl", ReadShader("composite.wgsl"));
        _compositePipeline = CreateCompositePipeline(compositeShader);
        Api.ShaderModuleRelease(compositeShader);

        CreatePointPipeline();
        CreateVolumePipelines();
        CreateTracePipeline();
        CreateSpotPipeline();
        CreateBarPipelines();
        CreateBubblePipeline();
        var layout = Api.RenderPipelineGetBindGroupLayout(_linePipeline, 0);
        var entry = new Native.BindGroupEntry { Binding = 0, Buffer = _lineUniforms, Size = LineUniformSize };
        var descriptor = new Native.BindGroupDescriptor { Layout = layout, EntryCount = 1, Entries = &entry };
        _lineBindGroup = Api.DeviceCreateBindGroup(device.Device, &descriptor);
        Api.BindGroupLayoutRelease(layout);
        _opaqueLayout = Api.RenderPipelineGetBindGroupLayout(_opaquePipeline, 0);
        _transparentLayout = Api.RenderPipelineGetBindGroupLayout(_transparentPipeline, 0);
    }

    /// <summary>Gets the finished picture.</summary>
    public IGpuTexture? Picture => _picture;

    /// <summary>Writes what the objects of the scene hold on the graphics card: each surface, cloud and volume on a line, the rest together.</summary>
    /// <param name="text">Receives the lines.</param>
    public void WriteMemory(DiagnosticsText text)
    {
        var total = 0L;
        foreach (var (owner, buffers) in _surfaces)
        {
            text.AddBytes(owner is Waterfall3D ? "GPU waterfall" : "GPU surface", buffers.Bytes);
            total += buffers.Bytes;
        }

        foreach (var buffer in _clouds.Values)
        {
            text.AddBytes("GPU cloud", buffer.Bytes);
            total += buffer.Bytes;
        }

        foreach (var buffer in _volumes.Values)
        {
            text.AddBytes("GPU volume", buffer.Bytes);
            total += buffer.Bytes;
        }

        var rest = 0L;
        foreach (var buffers in _meshes.Values)
        {
            rest += buffers.Bytes;
        }

        foreach (var buffer in _traces.Values)
        {
            rest += buffer.Bytes;
        }

        foreach (var buffer in _spots.Values)
        {
            rest += buffer.Bytes;
        }

        foreach (var buffer in _bars.Values)
        {
            rest += buffer.Bytes;
        }

        rest += BubbleBytes();

        if (rest > 0)
        {
            text.AddBytes("GPU meshes, lines, points, bars", rest);
        }

        text.AddBytes("GPU total", total + rest);
    }

    private static Native.WebGPU Api => WebGpuRenderDevice.Api;

    /// <summary>Takes over a surface. Its data reach the GPU once.</summary>
    /// <param name="mesh">The surface.</param>
    /// <param name="vertices">The points in box coordinates.</param>
    /// <param name="indices">The triangles.</param>
    public void Upload(Mesh3D mesh, ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices)
    {
        if (_meshes.TryGetValue(mesh, out var old))
        {
            old.Dispose();
        }

        _meshes[mesh] = new MeshBuffers(this, mesh.Material, vertices, indices);
    }

    /// <summary>Takes over the heights of a raster surface. A new size creates the buffers.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="shape">The size and place.</param>
    /// <param name="heights">The heights in the box.</param>
    /// <param name="colors">The colormap.</param>
    /// <param name="material">How it takes the light.</param>
    public void Update(object surface, in SurfaceShape shape, ReadOnlySpan<float> heights, ReadOnlySpan<Vector4> colors, Material3D material)
    {
        if (!_surfaces.TryGetValue(surface, out var buffers) || !buffers.Fits(shape))
        {
            buffers?.Dispose();
            buffers = new SurfaceBuffers(this, material, shape, colors);
            _surfaces[surface] = buffers;
        }

        buffers.Write(shape, heights);
    }

    /// <summary>Gets a value indicating whether a raster surface or waterfall is on the GPU.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns>Whether it is.</returns>
    public bool HoldsSurface(object surface) => _surfaces.ContainsKey(surface);

    /// <summary>Writes one row of a ring.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="slot">The slot in the ring.</param>
    /// <param name="heights">The heights of the row.</param>
    /// <param name="firstRow">The slot that the first row of the picture shows now.</param>
    public void WriteRow(object surface, int slot, ReadOnlySpan<float> heights, uint firstRow) => _surfaces[surface].WriteRow(slot, heights, firstRow);

    /// <summary>Gets a value indicating whether a surface is on the GPU.</summary>
    /// <param name="mesh">The surface.</param>
    /// <returns>Whether it is.</returns>
    public bool Holds(Mesh3D mesh) => _meshes.ContainsKey(mesh);

    /// <summary>Draws a frame into the picture.</summary>
    /// <param name="size">The size in pixels.</param>
    /// <param name="matrix">The matrix of the camera.</param>
    /// <param name="eye">The place of the eye.</param>
    /// <param name="background">The color behind everything.</param>
    /// <param name="lines">The lines, two points each.</param>
    /// <param name="planes">The planes of the walls and the floor.</param>
    /// <param name="cuts">The planes that cut the scene.</param>
    public void Render(PixelSize size, Matrix4x4 matrix, Vector3 eye, Rgba8 background, ReadOnlySpan<LineVertex> lines, Vector4 planes, in CutPlanes cuts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cuts = cuts;
        EnsureSize(size);
        WriteLines(lines);
        Api.QueueWriteBuffer(_device.Queue, _lineUniforms, 0, &matrix, LineUniformSize);
        var hasTransparency = false;
        foreach (var mesh in _meshes.Values)
        {
            mesh.WriteUniforms(matrix, eye);
            hasTransparency |= mesh.IsTransparent;
        }

        WriteTraceUniforms(matrix, size);
        WriteCloudUniforms(matrix, size);
        WriteVolumeUniforms(matrix);
        hasTransparency |= WriteBarUniforms(matrix, eye);
        WriteBubbleUniforms(matrix, eye, size);
        foreach (var surface in _surfaces.Values)
        {
            surface.WriteUniforms(matrix, eye, planes);
            hasTransparency |= surface.IsTransparent;
        }

        var encoder = Api.DeviceCreateCommandEncoder(_device.Device, null);
        var hasMarch = HasMarch();
        EncodeOpaque(encoder, background, lines.Length, hasTransparency || hasMarch, eye.Y);
        if (hasTransparency)
        {
            EncodeTransparent(encoder, eye.Y);
            EncodeComposite(encoder);
        }

        if (hasMarch)
        {
            EncodeMarch(encoder);
        }

        var commands = Api.CommandEncoderFinish(encoder, null);
        Api.QueueSubmit(_device.Queue, 1, &commands);
        Api.CommandBufferRelease(commands);
        Api.CommandEncoderRelease(encoder);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseAttachments();
        foreach (var mesh in _meshes.Values)
        {
            mesh.Dispose();
        }

        foreach (var surface in _surfaces.Values)
        {
            surface.Dispose();
        }

        ReleaseTraces();
        ReleaseBars();
        ReleaseBubbles();
        ReleaseClouds();
        ReleaseVolumes();

        Api.BindGroupLayoutRelease(_surfaceProjectionLayout);
        Api.RenderPipelineRelease(_surfaceProjection);
        Api.BindGroupLayoutRelease(_surfaceLinesLayout);
        Api.BindGroupLayoutRelease(_surfaceDepthLayout);
        Api.RenderPipelineRelease(_surfaceLines);
        Api.RenderPipelineRelease(_surfaceDepth);
        Api.BindGroupLayoutRelease(_opaqueSurfaceLayout);
        Api.BindGroupLayoutRelease(_transparentSurfaceLayout);
        Api.RenderPipelineRelease(_opaqueSurface);
        Api.RenderPipelineRelease(_transparentSurface);

        if (_lines is not null)
        {
            Api.BufferRelease(_lines);
        }

        Api.BindGroupLayoutRelease(_opaqueLayout);
        Api.BindGroupLayoutRelease(_transparentLayout);
        Api.BindGroupRelease(_lineBindGroup);
        Api.RenderPipelineRelease(_linePipeline);
        Api.RenderPipelineRelease(_opaquePipeline);
        Api.RenderPipelineRelease(_transparentPipeline);
        Api.RenderPipelineRelease(_compositePipeline);
        Api.BufferRelease(_lineUniforms);
    }

    private static string ReadShader(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The shader '{name}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Native.DepthStencilState CreateDepthState(bool writesDepth)
    {
        var keep = new Native.StencilFaceState { Compare = Native.CompareFunction.Always, FailOp = Native.StencilOperation.Keep, DepthFailOp = Native.StencilOperation.Keep, PassOp = Native.StencilOperation.Keep };
        return new Native.DepthStencilState { Format = DepthFormat, DepthWriteEnabled = writesDepth, DepthCompare = Native.CompareFunction.LessEqual, StencilFront = keep, StencilBack = keep };
    }

    private void EncodeOpaque(Native.CommandEncoder* encoder, Rgba8 background, int lineCount, bool keepsDepth, float eyeY)
    {
        var color = new Native.RenderPassColorAttachment
        {
            View = _msaaColor.View,
            ResolveTarget = _picture!.View,
            DepthSlice = uint.MaxValue,
            LoadOp = Native.LoadOp.Clear,
            StoreOp = Native.StoreOp.Discard,
            ClearValue = new Native.Color(background.R / 255.0, background.G / 255.0, background.B / 255.0, 1),
        };
        var depth = new Native.RenderPassDepthStencilAttachment
        {
            View = _depth.View,
            DepthLoadOp = Native.LoadOp.Clear,
            DepthStoreOp = keepsDepth ? Native.StoreOp.Store : Native.StoreOp.Discard,
            DepthClearValue = 1,
        };
        var descriptor = new Native.RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color, DepthStencilAttachment = &depth };
        var pass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
        if (lineCount > 0)
        {
            Api.RenderPassEncoderSetPipeline(pass, _linePipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _lineBindGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(pass, 0, _lines, 0, (ulong)lineCount * 16);
            Api.RenderPassEncoderDraw(pass, (uint)lineCount, 1, 0, 0);
        }

        Api.RenderPassEncoderSetPipeline(pass, _opaquePipeline);
        foreach (var mesh in _meshes.Values)
        {
            if (!mesh.IsTransparent)
            {
                mesh.Draw(pass);
            }
        }

        DrawTraces(pass);
        DrawClouds(pass);
        DrawSlices(pass);
        DrawBars(pass, isTransparent: false, eyeY);
        DrawBubbles(pass);
        foreach (var surface in _surfaces.Values)
        {
            if (surface.IsLines)
            {
                Api.RenderPassEncoderSetPipeline(pass, _surfaceDepth);
                surface.DrawDepth(pass);
                Api.RenderPassEncoderSetPipeline(pass, _surfaceLines);
                surface.DrawLines(pass);
            }
            else if (!surface.IsTransparent)
            {
                Api.RenderPassEncoderSetPipeline(pass, _opaqueSurface);
                surface.Draw(pass);
            }
        }

        Api.RenderPassEncoderSetPipeline(pass, _surfaceProjection);
        foreach (var surface in _surfaces.Values)
        {
            surface.DrawProjection(pass);
        }

        Api.RenderPassEncoderEnd(pass);
        Api.RenderPassEncoderRelease(pass);
    }

    private void EncodeTransparent(Native.CommandEncoder* encoder, float eyeY)
    {
        var colors = stackalloc Native.RenderPassColorAttachment[2];
        colors[0] = new Native.RenderPassColorAttachment
        {
            View = _msaaAccumulation.View,
            ResolveTarget = _accumulation.View,
            DepthSlice = uint.MaxValue,
            LoadOp = Native.LoadOp.Clear,
            StoreOp = Native.StoreOp.Discard,
            ClearValue = new Native.Color(0, 0, 0, 0),
        };
        colors[1] = new Native.RenderPassColorAttachment
        {
            View = _msaaReveal.View,
            ResolveTarget = _reveal.View,
            DepthSlice = uint.MaxValue,
            LoadOp = Native.LoadOp.Clear,
            StoreOp = Native.StoreOp.Discard,
            ClearValue = new Native.Color(1, 1, 1, 1),
        };
        var depth = new Native.RenderPassDepthStencilAttachment { View = _depth.View, DepthLoadOp = Native.LoadOp.Load, DepthStoreOp = Native.StoreOp.Discard };
        var descriptor = new Native.RenderPassDescriptor { ColorAttachmentCount = 2, ColorAttachments = colors, DepthStencilAttachment = &depth };
        var pass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
        Api.RenderPassEncoderSetPipeline(pass, _transparentPipeline);
        foreach (var mesh in _meshes.Values)
        {
            if (mesh.IsTransparent)
            {
                mesh.Draw(pass);
            }
        }

        DrawBars(pass, isTransparent: true, eyeY);
        Api.RenderPassEncoderSetPipeline(pass, _transparentSurface);
        foreach (var surface in _surfaces.Values)
        {
            if (surface.IsTransparent && !surface.IsLines)
            {
                surface.Draw(pass);
            }
        }

        Api.RenderPassEncoderEnd(pass);
        Api.RenderPassEncoderRelease(pass);
    }

    private void EncodeComposite(Native.CommandEncoder* encoder)
    {
        var color = new Native.RenderPassColorAttachment
        {
            View = _picture!.View,
            DepthSlice = uint.MaxValue,
            LoadOp = Native.LoadOp.Load,
            StoreOp = Native.StoreOp.Store,
        };
        var descriptor = new Native.RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color };
        var pass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
        Api.RenderPassEncoderSetPipeline(pass, _compositePipeline);
        Api.RenderPassEncoderSetBindGroup(pass, 0, _compositeBindGroup, 0, null);
        Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
        Api.RenderPassEncoderEnd(pass);
        Api.RenderPassEncoderRelease(pass);
    }

    private Native.RenderPipeline* CreateLinePipeline(Native.ShaderModule* shader)
    {
        var blend = new Native.BlendState
        {
            Color = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.SrcAlpha, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
            Alpha = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
        };
        var target = new Native.ColorTargetState { Format = ColorFormat, Blend = &blend, WriteMask = Native.ColorWriteMask.All };
        var depth = CreateDepthState(writesDepth: true);
        var attributes = stackalloc Native.VertexAttribute[2];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 1 };
        var layout = new Native.VertexBufferLayout { ArrayStride = 16, StepMode = Native.VertexStepMode.Vertex, AttributeCount = 2, Attributes = attributes };
        fixed (byte* vertexEntry = "vs_main\0"u8, fragmentEntry = "fs_main\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &layout },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.LineList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                DepthStencil = &depth,
                Multisample = new Native.MultisampleState { Count = SampleCount, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            return Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }
    }

    private Native.RenderPipeline* CreateMeshPipeline(Native.ShaderModule* shader, bool isTransparent, bool hasVertices, Native.PrimitiveTopology topology = Native.PrimitiveTopology.TriangleList, bool isDepthOnly = false, Native.VertexBufferLayout* instances = null)
    {
        var add = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.One };
        var reveal = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.Zero, DstFactor = Native.BlendFactor.OneMinusSrc };
        var accumulationBlend = new Native.BlendState { Color = add, Alpha = add };
        var revealBlend = new Native.BlendState { Color = reveal, Alpha = reveal };
        var colorMask = isDepthOnly ? Native.ColorWriteMask.None : Native.ColorWriteMask.All;
        var targets = stackalloc Native.ColorTargetState[2];
        targets[0] = isTransparent
            ? new Native.ColorTargetState { Format = AccumulationFormat, Blend = &accumulationBlend, WriteMask = Native.ColorWriteMask.All }
            : new Native.ColorTargetState { Format = ColorFormat, WriteMask = colorMask };
        targets[1] = new Native.ColorTargetState { Format = RevealFormat, Blend = &revealBlend, WriteMask = Native.ColorWriteMask.All };
        var depth = CreateDepthState(writesDepth: !isTransparent);
        if (isDepthOnly)
        {
            // Behind its own lines: the hidden surface must not swallow the curves that lie on it.
            depth.DepthBias = 6000;
            depth.DepthBiasSlopeScale = 3;
        }

        var attributes = stackalloc Native.VertexAttribute[3];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 24, ShaderLocation = 2 };
        var layout = new Native.VertexBufferLayout { ArrayStride = (ulong)Unsafe.SizeOf<MeshVertex>(), StepMode = Native.VertexStepMode.Vertex, AttributeCount = 3, Attributes = attributes };
        fixed (byte* vertexEntry = "vs_main\0"u8, opaqueEntry = "fs_opaque\0"u8, transparentEntry = "fs_transparent\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = isTransparent ? transparentEntry : opaqueEntry, TargetCount = isTransparent ? 2u : 1u, Targets = targets };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry, BufferCount = hasVertices ? 1u : 0u, Buffers = instances is null ? &layout : instances },
                Primitive = new Native.PrimitiveState { Topology = topology, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                DepthStencil = &depth,
                Multisample = new Native.MultisampleState { Count = SampleCount, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            return Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }
    }

    private Native.RenderPipeline* CreateProjectionPipeline(Native.ShaderModule* shader)
    {
        var blend = new Native.BlendState
        {
            Color = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.SrcAlpha, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
            Alpha = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
        };
        var target = new Native.ColorTargetState { Format = ColorFormat, Blend = &blend, WriteMask = Native.ColorWriteMask.All };
        var depth = CreateDepthState(writesDepth: false);
        fixed (byte* vertexEntry = "vs_projected\0"u8, fragmentEntry = "fs_projected\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.TriangleList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                DepthStencil = &depth,
                Multisample = new Native.MultisampleState { Count = SampleCount, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            return Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }
    }

    private Native.RenderPipeline* CreateCompositePipeline(Native.ShaderModule* shader)
    {
        var blend = new Native.BlendState
        {
            Color = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.SrcAlpha, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
            Alpha = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
        };
        var target = new Native.ColorTargetState { Format = ColorFormat, Blend = &blend, WriteMask = Native.ColorWriteMask.All };
        fixed (byte* vertexEntry = "vs_main\0"u8, fragmentEntry = "fs_main\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.TriangleList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                Multisample = new Native.MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            return Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }
    }

    private void WriteLines(ReadOnlySpan<LineVertex> lines)
    {
        var bytes = (ulong)lines.Length * 16;
        if (bytes > _lineCapacity)
        {
            if (_lines is not null)
            {
                Api.BufferRelease(_lines);
            }

            _lineCapacity = bytes * 2;
            var descriptor = new Native.BufferDescriptor { Size = _lineCapacity, Usage = Native.BufferUsage.Vertex | Native.BufferUsage.CopyDst };
            _lines = Api.DeviceCreateBuffer(_device.Device, &descriptor);
        }

        if (bytes > 0)
        {
            fixed (LineVertex* source = lines)
            {
                Api.QueueWriteBuffer(_device.Queue, _lines, 0, source, (nuint)bytes);
            }
        }
    }

    private void EnsureSize(PixelSize size)
    {
        if (_picture is not null && _picture.Size == size)
        {
            return;
        }

        ReleaseAttachments();
        ForgetMarchGroups();
        _picture = new WebGpuTexture(_device, new TextureDesc(size, TextureFormat.Rgba8Unorm), isRenderTarget: true);
        _msaaColor = CreateAttachment(ColorFormat, size, SampleCount);
        _depth = CreateAttachment(DepthFormat, size, SampleCount, isRead: true);
        _msaaAccumulation = CreateAttachment(AccumulationFormat, size, SampleCount);
        _msaaReveal = CreateAttachment(RevealFormat, size, SampleCount);
        _accumulation = CreateAttachment(AccumulationFormat, size, 1);
        _reveal = CreateAttachment(RevealFormat, size, 1);

        var layout = Api.RenderPipelineGetBindGroupLayout(_compositePipeline, 0);
        var entries = stackalloc Native.BindGroupEntry[2];
        entries[0] = new Native.BindGroupEntry { Binding = 0, TextureView = _accumulation.View };
        entries[1] = new Native.BindGroupEntry { Binding = 1, TextureView = _reveal.View };
        var descriptor = new Native.BindGroupDescriptor { Layout = layout, EntryCount = 2, Entries = entries };
        _compositeBindGroup = Api.DeviceCreateBindGroup(_device.Device, &descriptor);
        Api.BindGroupLayoutRelease(layout);
    }

    private Attachment CreateAttachment(Native.TextureFormat format, PixelSize size, uint samples, bool isRead = false)
    {
        var usage = samples == 1 || isRead ? Native.TextureUsage.RenderAttachment | Native.TextureUsage.TextureBinding : Native.TextureUsage.RenderAttachment;
        var descriptor = new Native.TextureDescriptor
        {
            Usage = usage,
            Dimension = Native.TextureDimension.Dimension2D,
            Size = new Native.Extent3D((uint)size.Width, (uint)size.Height, 1),
            Format = format,
            MipLevelCount = 1,
            SampleCount = samples,
        };
        var texture = Api.DeviceCreateTexture(_device.Device, &descriptor);
        return new Attachment(texture, Api.TextureCreateView(texture, null));
    }

    private void ReleaseAttachments()
    {
        if (_picture is null)
        {
            return;
        }

        Api.BindGroupRelease(_compositeBindGroup);
        _msaaColor.Release();
        _depth.Release();
        _msaaAccumulation.Release();
        _msaaReveal.Release();
        _accumulation.Release();
        _reveal.Release();
        _picture.Dispose();
        _picture = null;
    }

    private readonly struct Attachment(Native.Texture* texture, Native.TextureView* view)
    {
        public Native.TextureView* View { get; } = view;

        public void Release()
        {
            Api.TextureViewRelease(View);
            Api.TextureRelease(texture);
        }
    }

    private sealed class SurfaceBuffers : IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly Material3D _material;
        private readonly Native.Buffer* _heights;
        private readonly Native.Buffer* _indices;
        private readonly Native.Buffer* _colors;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _bindGroup;
        private readonly uint _indexCount;
        private readonly Native.Buffer* _levels;
        private readonly Native.BindGroup* _projectionBindGroup;
        private readonly Native.Buffer* _lineIndices;
        private readonly Native.BindGroup* _lineBindGroup;
        private readonly Native.BindGroup* _depthBindGroup;
        private readonly uint _lineIndexCount;
        private SurfaceShape _shape;

        /// <summary>Gets the bytes it holds on the graphics card, with the lines of the grid counted like the triangles.</summary>
        public long Bytes { get; }

        public SurfaceBuffers(Renderer3D owner, Material3D material, in SurfaceShape shape, ReadOnlySpan<Vector4> colors)
        {
            _owner = owner;
            _material = material;
            _shape = shape;
            var values = (ulong)shape.Columns * (ulong)shape.Rows;
            _heights = Create(values * sizeof(float), Native.BufferUsage.Storage);
            _colors = Create((ulong)colors.Length * 16, Native.BufferUsage.Storage);
            fixed (Vector4* source = colors)
            {
                Api.QueueWriteBuffer(owner._device.Queue, _colors, 0, source, (nuint)(colors.Length * 16));
            }

            _indexCount = (uint)((shape.Columns - 1) * (shape.Rows - 1) * 6);
            _indices = Create((ulong)_indexCount * sizeof(uint), Native.BufferUsage.Index);
            Bytes = (long)(values * sizeof(float)) + (colors.Length * 16L) + (_indexCount * 8L);
            WriteIndices(shape.Columns, shape.Rows);
            var size = (ulong)Unsafe.SizeOf<SurfaceUniforms>();
            var uniforms = new Native.BufferDescriptor { Size = size, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &uniforms);
            var levels = shape.Levels ?? [];
            _levels = Create(Math.Max((ulong)levels.Length, 4) * sizeof(float), Native.BufferUsage.Storage);
            if (levels.Length > 0)
            {
                fixed (float* source = levels)
                {
                    Api.QueueWriteBuffer(owner._device.Queue, _levels, 0, source, (nuint)(levels.Length * sizeof(float)));
                }
            }

            var entries = stackalloc Native.BindGroupEntry[4];
            entries[0] = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = size };
            entries[1] = new Native.BindGroupEntry { Binding = 1, Buffer = _heights, Size = values * sizeof(float) };
            entries[2] = new Native.BindGroupEntry { Binding = 2, Buffer = _colors, Size = (ulong)colors.Length * 16 };
            entries[3] = new Native.BindGroupEntry { Binding = 3, Buffer = _levels, Size = Math.Max((ulong)levels.Length, 4) * sizeof(float) };
            var group = new Native.BindGroupDescriptor { Layout = IsTransparent ? owner._transparentSurfaceLayout : owner._opaqueSurfaceLayout, EntryCount = 4, Entries = entries };
            _bindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
            if (shape.Contours is not null)
            {
                entries[2] = entries[3] with { Binding = 3 };
                var projection = new Native.BindGroupDescriptor { Layout = owner._surfaceProjectionLayout, EntryCount = 3, Entries = entries };
                _projectionBindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &projection);
                entries[2] = new Native.BindGroupEntry { Binding = 2, Buffer = _colors, Size = (ulong)colors.Length * 16 };
            }

            if (shape.LineSpacing > 0)
            {
                group.Layout = owner._surfaceLinesLayout;
                _lineBindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
                group.Layout = owner._surfaceDepthLayout;
                _depthBindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
                _lineIndexCount = (uint)(((shape.Rows + shape.LineSpacing - 1) / shape.LineSpacing) * (shape.Columns - 1) * 2);
                _lineIndices = Create((ulong)_lineIndexCount * sizeof(uint), Native.BufferUsage.Index);
                WriteLineIndices(shape);
            }
        }

        public bool IsTransparent => _material.Opacity < 1;

        public bool IsLines => _shape.LineSpacing > 0;

        public bool Fits(in SurfaceShape shape) => shape.Columns == _shape.Columns && shape.Rows == _shape.Rows;

        public void Write(in SurfaceShape shape, ReadOnlySpan<float> heights)
        {
            _shape = shape;
            fixed (float* source = heights)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _heights, 0, source, (nuint)(heights.Length * sizeof(float)));
            }
        }

        public void WriteRow(int slot, ReadOnlySpan<float> heights, uint firstRow)
        {
            _shape = _shape with { FirstRow = firstRow };
            fixed (float* source = heights)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _heights, (ulong)slot * (ulong)_shape.Columns * sizeof(float), source, (nuint)(heights.Length * sizeof(float)));
            }
        }

        public void WriteUniforms(Matrix4x4 matrix, Vector3 eye, Vector4 planes)
        {
            var contours = _shape.Contours;
            var uniforms = new SurfaceUniforms
            {
                Camera = matrix,
                Eye = new Vector4(eye, 1),
                Material = new Vector4((float)_material.Gloss, (float)_material.Sharpness, (float)_material.Opacity, _material.IsLit ? 1 : 0),
                Columns = (uint)_shape.Columns,
                Rows = (uint)_shape.Rows,
                FirstRow = _shape.FirstRow,
                Origin = _shape.Origin,
                Colors = _shape.Colors,
                Contour = new Vector4(_shape.Levels?.Length ?? 0, (float)(contours?.Width ?? 0), Contours3D.WallDistance, 0),
                ContourColor = contours is null ? default : new Vector4(contours.Color.R, contours.Color.G, contours.Color.B, contours.Color.A) / 255f,
                Planes = planes,
                Cuts = _owner._cuts,
            };
            Api.QueueWriteBuffer(_owner._device.Queue, _uniforms, 0, &uniforms, (nuint)Unsafe.SizeOf<SurfaceUniforms>());
        }

        public void Draw(Native.RenderPassEncoder* pass)
        {
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            Api.RenderPassEncoderSetIndexBuffer(pass, _indices, Native.IndexFormat.Uint32, 0, (ulong)_indexCount * sizeof(uint));
            Api.RenderPassEncoderDrawIndexed(pass, _indexCount, 1, 0, 0, 0);
        }

        public void DrawProjection(Native.RenderPassEncoder* pass)
        {
            if (_shape.Contours is not { } contours || (!contours.OnFloor && !contours.OnWalls))
            {
                return;
            }

            var first = contours.OnFloor ? 0u : 1u;
            var count = (contours.OnFloor ? 1u : 0u) + (contours.OnWalls ? 2u : 0u);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _projectionBindGroup, 0, null);
            Api.RenderPassEncoderSetIndexBuffer(pass, _indices, Native.IndexFormat.Uint32, 0, (ulong)_indexCount * sizeof(uint));
            Api.RenderPassEncoderDrawIndexed(pass, _indexCount, count, 0, 0, first);
        }

        public void DrawDepth(Native.RenderPassEncoder* pass)
        {
            Api.RenderPassEncoderSetBindGroup(pass, 0, _depthBindGroup, 0, null);
            Api.RenderPassEncoderSetIndexBuffer(pass, _indices, Native.IndexFormat.Uint32, 0, (ulong)_indexCount * sizeof(uint));
            Api.RenderPassEncoderDrawIndexed(pass, _indexCount, 1, 0, 0, 0);
        }

        public void DrawLines(Native.RenderPassEncoder* pass)
        {
            Api.RenderPassEncoderSetBindGroup(pass, 0, _lineBindGroup, 0, null);
            Api.RenderPassEncoderSetIndexBuffer(pass, _lineIndices, Native.IndexFormat.Uint32, 0, (ulong)_lineIndexCount * sizeof(uint));
            Api.RenderPassEncoderDrawIndexed(pass, _lineIndexCount, 1, 0, 0, 0);
        }

        public void Dispose()
        {
            if (_lineIndices is not null)
            {
                Api.BindGroupRelease(_lineBindGroup);
                Api.BindGroupRelease(_depthBindGroup);
                Api.BufferRelease(_lineIndices);
            }

            if (_projectionBindGroup is not null)
            {
                Api.BindGroupRelease(_projectionBindGroup);
            }

            Api.BindGroupRelease(_bindGroup);
            Api.BufferRelease(_levels);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_indices);
            Api.BufferRelease(_colors);
            Api.BufferRelease(_heights);
        }

        private Native.Buffer* Create(ulong size, Native.BufferUsage usage)
        {
            var descriptor = new Native.BufferDescriptor { Size = Math.Max(size, 16), Usage = usage | Native.BufferUsage.CopyDst };
            return Api.DeviceCreateBuffer(_owner._device.Device, &descriptor);
        }

        private void WriteLineIndices(in SurfaceShape shape)
        {
            var indices = new uint[_lineIndexCount];
            var at = 0;
            for (var row = 0; row < shape.Rows; row += shape.LineSpacing)
            {
                for (var column = 0; column < shape.Columns - 1; column++)
                {
                    var start = (uint)((row * shape.Columns) + column);
                    indices[at++] = start;
                    indices[at++] = start + 1;
                }
            }

            fixed (uint* source = indices)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _lineIndices, 0, source, (nuint)(indices.Length * sizeof(uint)));
            }
        }

        private void WriteIndices(int columns, int rows)
        {
            var quads = new uint[(columns - 1) * 6];
            for (var row = 0; row < rows - 1; row++)
            {
                for (var column = 0; column < columns - 1; column++)
                {
                    var corner = (uint)((row * columns) + column);
                    var at = column * 6;
                    quads[at] = corner;
                    quads[at + 1] = corner + 1;
                    quads[at + 2] = corner + (uint)columns;
                    quads[at + 3] = corner + 1;
                    quads[at + 4] = corner + (uint)columns + 1;
                    quads[at + 5] = corner + (uint)columns;
                }

                fixed (uint* source = quads)
                {
                    Api.QueueWriteBuffer(_owner._device.Queue, _indices, (ulong)row * (ulong)quads.Length * sizeof(uint), source, (nuint)(quads.Length * sizeof(uint)));
                }
            }
        }
    }

    private sealed class MeshBuffers : IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly Material3D _material;
        private readonly Native.Buffer* _vertices;
        private readonly Native.Buffer* _indices;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _bindGroup;
        private readonly uint _indexCount;

        /// <summary>Gets the bytes it holds on the graphics card.</summary>
        public long Bytes { get; }

        public MeshBuffers(Renderer3D owner, Material3D material, ReadOnlySpan<MeshVertex> vertices, ReadOnlySpan<uint> indices)
        {
            _owner = owner;
            _material = material;
            _indexCount = (uint)indices.Length;
            Bytes = MemoryMarshal.AsBytes(vertices).Length + (indices.Length * 4L);
            _vertices = CreateBuffer(owner, MemoryMarshal.AsBytes(vertices), Native.BufferUsage.Vertex);
            _indices = CreateBuffer(owner, MemoryMarshal.AsBytes(indices), Native.BufferUsage.Index);
            var size = (ulong)Unsafe.SizeOf<MeshUniforms>();
            var descriptor = new Native.BufferDescriptor { Size = size, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &descriptor);
            var entry = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = size };
            var group = new Native.BindGroupDescriptor { Layout = material.Opacity < 1 ? owner._transparentLayout : owner._opaqueLayout, EntryCount = 1, Entries = &entry };
            _bindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
        }

        public bool IsTransparent => _material.Opacity < 1;

        public void WriteUniforms(Matrix4x4 matrix, Vector3 eye)
        {
            var uniforms = new MeshUniforms
            {
                Camera = matrix,
                Eye = new Vector4(eye, 1),
                Material = new Vector4((float)_material.Gloss, (float)_material.Sharpness, (float)_material.Opacity, _material.IsLit ? 1 : 0),
                Cuts = _owner._cuts,
            };
            Api.QueueWriteBuffer(_owner._device.Queue, _uniforms, 0, &uniforms, (nuint)Unsafe.SizeOf<MeshUniforms>());
        }

        public void Draw(Native.RenderPassEncoder* pass)
        {
            if (_indexCount == 0)
            {
                return;
            }

            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(pass, 0, _vertices, 0, Api.BufferGetSize(_vertices));
            Api.RenderPassEncoderSetIndexBuffer(pass, _indices, Native.IndexFormat.Uint32, 0, Api.BufferGetSize(_indices));
            Api.RenderPassEncoderDrawIndexed(pass, _indexCount, 1, 0, 0, 0);
        }

        public void Dispose()
        {
            Api.BindGroupRelease(_bindGroup);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_indices);
            Api.BufferRelease(_vertices);
        }

        private static Native.Buffer* CreateBuffer(Renderer3D owner, ReadOnlySpan<byte> data, Native.BufferUsage usage)
        {
            var descriptor = new Native.BufferDescriptor { Size = Math.Max((ulong)data.Length, 16), Usage = usage | Native.BufferUsage.CopyDst };
            var buffer = Api.DeviceCreateBuffer(owner._device.Device, &descriptor);
            fixed (byte* source = data)
            {
                if (!data.IsEmpty)
                {
                    Api.QueueWriteBuffer(owner._device.Queue, buffer, 0, source, (nuint)data.Length);
                }
            }

            return buffer;
        }
    }
}
