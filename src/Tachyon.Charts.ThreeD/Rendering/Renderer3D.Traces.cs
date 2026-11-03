using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.WebGPU;
using Tachyon.Charts.ThreeD.Scene;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>What the uniforms of a line hold. The order follows trace.wgsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TraceUniforms
{
    public Matrix4x4 Camera;
    public Vector2 Size;
    public float Width;
    public float Unused;
    public CutPlanes Cuts;
}

/// <content>The lines in space.</content>
internal sealed unsafe partial class Renderer3D
{
    private const int TraceStride = 16;
    private const uint TraceCorners = 6;
    private const int TraceBuffers = 4;
    private const int SpotsFrom = 8_000_000;
    private const int PixelsFrom = 10_000_000;

    private readonly Dictionary<Line3D, TraceBuffer> _traces = [];
    private readonly Dictionary<Scatter3D, TraceBuffer> _spots = [];
    private Native.RenderPipeline* _tracePipeline;
    private Native.BindGroupLayout* _traceLayout;
    private Native.RenderPipeline* _spotPipeline;
    private Native.BindGroupLayout* _spotLayout;

    /// <summary>Gets the buffer of a line and creates it the first time.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The buffer.</returns>
    public ITraceTarget GetTrace(Line3D line)
    {
        if (!_traces.TryGetValue(line, out var buffer))
        {
            buffer = new TraceBuffer(this, line.Points, line.Width, _traceLayout);
            _traces[line] = buffer;
        }

        return buffer;
    }

    /// <summary>Gets the buffer of a cloud and creates it the first time.</summary>
    /// <param name="scatter">The cloud.</param>
    /// <returns>The buffer.</returns>
    public ITraceTarget GetSpots(Scatter3D scatter)
    {
        if (!_spots.TryGetValue(scatter, out var buffer))
        {
            buffer = new TraceBuffer(this, scatter.Points, 0, _spotLayout, scatter.PointBudget);
            _spots[scatter] = buffer;
        }

        return buffer;
    }

    private void CreateSpotPipeline()
    {
        var shader = _device.CompileShader("scatter.wgsl", ReadShader("clip.wgsl") + ReadShader("scatter.wgsl"));

        // The alpha of the shader only decides on the coverage. The picture stays opaque.
        var target = new Native.ColorTargetState { Format = ColorFormat, WriteMask = Native.ColorWriteMask.Red | Native.ColorWriteMask.Green | Native.ColorWriteMask.Blue };
        var depth = CreateDepthState(writesDepth: true);
        var attributes = stackalloc Native.VertexAttribute[2];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 1 };
        var layout = new Native.VertexBufferLayout { ArrayStride = TraceStride, StepMode = Native.VertexStepMode.Instance, AttributeCount = 2, Attributes = attributes };
        fixed (byte* vertexEntry = "vs_main\0"u8, fragmentEntry = "fs_main\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &layout },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.TriangleList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                DepthStencil = &depth,
                Multisample = new Native.MultisampleState { Count = SampleCount, Mask = uint.MaxValue, AlphaToCoverageEnabled = true },
                Fragment = &fragment,
            };
            _spotPipeline = Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }

        _spotLayout = Api.RenderPipelineGetBindGroupLayout(_spotPipeline, 0);
        Api.ShaderModuleRelease(shader);
    }

    private void CreateTracePipeline()
    {
        var shader = _device.CompileShader("trace.wgsl", ReadShader("clip.wgsl") + ReadShader("trace.wgsl"));
        var target = new Native.ColorTargetState { Format = ColorFormat, WriteMask = Native.ColorWriteMask.All };
        var depth = CreateDepthState(writesDepth: true);
        var attributes = stackalloc Native.VertexAttribute[6];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 1 };
        attributes[2] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 2 };
        attributes[3] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 3 };
        attributes[4] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 4 };
        attributes[5] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 5 };

        // One buffer at four offsets: WebGPU forbids attributes beyond the stride, and a piece reads four points.
        var layouts = stackalloc Native.VertexBufferLayout[TraceBuffers];
        layouts[0] = new Native.VertexBufferLayout { ArrayStride = TraceStride, StepMode = Native.VertexStepMode.Instance, AttributeCount = 1, Attributes = attributes };
        layouts[1] = new Native.VertexBufferLayout { ArrayStride = TraceStride, StepMode = Native.VertexStepMode.Instance, AttributeCount = 2, Attributes = attributes + 1 };
        layouts[2] = new Native.VertexBufferLayout { ArrayStride = TraceStride, StepMode = Native.VertexStepMode.Instance, AttributeCount = 2, Attributes = attributes + 3 };
        layouts[3] = new Native.VertexBufferLayout { ArrayStride = TraceStride, StepMode = Native.VertexStepMode.Instance, AttributeCount = 1, Attributes = attributes + 5 };
        fixed (byte* vertexEntry = "vs_main\0"u8, fragmentEntry = "fs_main\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry, BufferCount = TraceBuffers, Buffers = layouts },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.TriangleList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                DepthStencil = &depth,
                Multisample = new Native.MultisampleState { Count = SampleCount, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            _tracePipeline = Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }

        _traceLayout = Api.RenderPipelineGetBindGroupLayout(_tracePipeline, 0);
        Api.ShaderModuleRelease(shader);
    }

    private void WriteTraceUniforms(Matrix4x4 matrix, PixelSize size)
    {
        foreach (var trace in _traces.Values)
        {
            trace.WriteUniforms(matrix, size);
        }

        foreach (var spots in _spots.Values)
        {
            spots.WriteUniforms(matrix, size);
        }
    }

    private void DrawTraces(Native.RenderPassEncoder* pass)
    {
        if (_traces.Count > 0)
        {
            Api.RenderPassEncoderSetPipeline(pass, _tracePipeline);
            foreach (var trace in _traces.Values)
            {
                trace.Draw(pass);
            }
        }

        foreach (var spots in _spots.Values)
        {
            spots.DrawSpots(pass);
        }
    }

    private void ReleaseTraces()
    {
        foreach (var trace in _traces.Values)
        {
            trace.Dispose();
        }

        foreach (var spots in _spots.Values)
        {
            spots.Dispose();
        }

        Api.BindGroupLayoutRelease(_spotLayout);
        Api.RenderPipelineRelease(_spotPipeline);
        Api.BindGroupLayoutRelease(_traceLayout);
        Api.RenderPipelineRelease(_tracePipeline);
    }

    /// <summary>The points and uniforms of a line or cloud on the GPU.</summary>
    private sealed class TraceBuffer : ITraceTarget, IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly Points3D _source;
        private readonly double _width;
        private readonly ulong _size;

        /// <summary>Gets the bytes it holds on the graphics card.</summary>
        public long Bytes => (long)_size;
        private readonly Native.Buffer* _points;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _bindGroup;
        private readonly Native.BindGroup* _pixelGroup;
        private readonly int _budget;
        private bool _usesPixels;

        public TraceBuffer(Renderer3D owner, Points3D source, double width, Native.BindGroupLayout* layout, int budget = 0)
        {
            (_owner, _source, _width, _budget) = (owner, source, width, budget);
            _size = (ulong)TraceRing.Places(source.Capacity) * TraceStride;
            var points = new Native.BufferDescriptor { Size = _size, Usage = Native.BufferUsage.Vertex | Native.BufferUsage.CopyDst };
            _points = Api.DeviceCreateBuffer(owner._device.Device, &points);
            var uniformSize = (ulong)sizeof(TraceUniforms);
            var uniforms = new Native.BufferDescriptor { Size = uniformSize, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &uniforms);
            var entry = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = uniformSize };
            var descriptor = new Native.BindGroupDescriptor { Layout = layout, EntryCount = 1, Entries = &entry };
            _bindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &descriptor);
            var pixels = new Native.BindGroupDescriptor { Layout = owner._pointLayout, EntryCount = 1, Entries = &entry };
            _pixelGroup = Api.DeviceCreateBindGroup(owner._device.Device, &pixels);
            TraceRing.Prepare(this);
        }

        public void Write(int place, ReadOnlySpan<LineVertex> points)
        {
            fixed (LineVertex* source = points)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _points, (ulong)place * TraceStride, source, (nuint)(points.Length * TraceStride));
            }
        }

        public void WriteUniforms(Matrix4x4 matrix, PixelSize size)
        {
            var uniforms = new TraceUniforms { Camera = matrix, Size = new Vector2(size.Width, size.Height), Width = (float)_width, Cuts = _owner._cuts };
            Api.QueueWriteBuffer(_owner._device.Queue, _uniforms, 0, &uniforms, (nuint)sizeof(TraceUniforms));
        }

        public void Draw(Native.RenderPassEncoder* pass)
        {
            var pieces = TraceRing.Pieces(_source.Capacity, _source.Total);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            for (uint buffer = 0; buffer < TraceBuffers; buffer++)
            {
                var offset = (ulong)buffer * TraceStride;
                Api.RenderPassEncoderSetVertexBuffer(pass, buffer, _points, offset, _size - offset);
            }

            Api.RenderPassEncoderDraw(pass, TraceCorners, (uint)pieces, 0, 0);
        }

        public void DrawSpots(Native.RenderPassEncoder* pass)
        {
            var (first, second) = TraceRing.NewestRuns(_source.Capacity, _source.Total, _budget);
            var total = first.Count + second.Count;
            _usesPixels = total >= (_usesPixels ? SpotsFrom : PixelsFrom);
            Api.RenderPassEncoderSetPipeline(pass, _usesPixels ? _owner._pointPipeline : _owner._spotPipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _usesPixels ? _pixelGroup : _bindGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(pass, 0, _points, 0, _size);
            DrawRun(pass, first);
            DrawRun(pass, second);
        }

        private void DrawRun(Native.RenderPassEncoder* pass, CloudDraw run)
        {
            if (run.Count == 0)
            {
                return;
            }

            var first = (uint)run.First + TraceRing.FirstSlot;
            if (_usesPixels)
            {
                Api.RenderPassEncoderDraw(pass, (uint)run.Count, 1, first, 0);
            }
            else
            {
                Api.RenderPassEncoderDraw(pass, TraceCorners, (uint)run.Count, 0, first);
            }
        }

        public void Dispose()
        {
            Api.BindGroupRelease(_pixelGroup);
            Api.BindGroupRelease(_bindGroup);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_points);
        }
    }
}
