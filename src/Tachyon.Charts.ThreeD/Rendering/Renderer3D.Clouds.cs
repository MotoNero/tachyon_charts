using System.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <content>The very large clouds of points.</content>
internal sealed unsafe partial class Renderer3D
{
    private readonly Dictionary<PointCloud3D, CloudBuffer> _clouds = [];
    private Native.RenderPipeline* _pointPipeline;
    private Native.BindGroupLayout* _pointLayout;

    /// <summary>Gets the buffer of a cloud. It takes its full size at once, so the memory is known before the data arrive.</summary>
    /// <param name="cloud">The cloud.</param>
    /// <returns>The buffer.</returns>
    public ITraceTarget GetCloud(PointCloud3D cloud)
    {
        if (!_clouds.TryGetValue(cloud, out var buffer))
        {
            buffer = new CloudBuffer(this, PointCloud3D.EstimateMemory(cloud.Count).Gpu);
            _clouds[cloud] = buffer;
        }

        return buffer;
    }

    /// <summary>Sets what a cloud shows.</summary>
    /// <param name="cloud">The cloud.</param>
    /// <param name="draws">The runs of points.</param>
    /// <param name="usesSpots">Whether the points are spots instead of single pixels.</param>
    public void SetDraws(PointCloud3D cloud, ReadOnlySpan<CloudDraw> draws, bool usesSpots) => _clouds[cloud].SetDraws(draws, usesSpots);

    private void CreatePointPipeline()
    {
        var shader = _device.CompileShader("scatter.wgsl", ReadShader("clip.wgsl") + ReadShader("scatter.wgsl"));
        var target = new Native.ColorTargetState { Format = ColorFormat, WriteMask = Native.ColorWriteMask.All };
        var depth = CreateDepthState(writesDepth: true);
        var attributes = stackalloc Native.VertexAttribute[2];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 12, ShaderLocation = 1 };
        var layout = new Native.VertexBufferLayout { ArrayStride = TraceStride, StepMode = Native.VertexStepMode.Vertex, AttributeCount = 2, Attributes = attributes };
        fixed (byte* vertexEntry = "vs_point\0"u8, fragmentEntry = "fs_point\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry, BufferCount = 1, Buffers = &layout },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.PointList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                DepthStencil = &depth,
                Multisample = new Native.MultisampleState { Count = SampleCount, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            _pointPipeline = Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }

        _pointLayout = Api.RenderPipelineGetBindGroupLayout(_pointPipeline, 0);
        Api.ShaderModuleRelease(shader);
    }

    private void WriteCloudUniforms(Matrix4x4 matrix, PixelSize size)
    {
        foreach (var cloud in _clouds.Values)
        {
            cloud.WriteUniforms(matrix, size);
        }
    }

    private void DrawClouds(Native.RenderPassEncoder* pass)
    {
        foreach (var cloud in _clouds.Values)
        {
            cloud.Draw(pass);
        }
    }

    private void ReleaseClouds()
    {
        foreach (var cloud in _clouds.Values)
        {
            cloud.Dispose();
        }

        Api.BindGroupLayoutRelease(_pointLayout);
        Api.RenderPipelineRelease(_pointPipeline);
    }

    /// <summary>The points and uniforms of a cloud on the GPU.</summary>
    private sealed class CloudBuffer : ITraceTarget, IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly ulong _size;

        /// <summary>Gets the bytes it holds on the graphics card.</summary>
        public long Bytes => (long)_size;
        private readonly Native.Buffer* _points;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _pointGroup;
        private readonly Native.BindGroup* _spotGroup;
        private CloudDraw[] _draws = [];
        private int _drawCount;
        private bool _usesSpots;

        public CloudBuffer(Renderer3D owner, long size)
        {
            _owner = owner;
            _size = (ulong)Math.Max(size, TraceStride);
            var points = new Native.BufferDescriptor { Size = _size, Usage = Native.BufferUsage.Vertex | Native.BufferUsage.CopyDst };
            _points = Api.DeviceCreateBuffer(owner._device.Device, &points);
            var uniformSize = (ulong)sizeof(TraceUniforms);
            var uniforms = new Native.BufferDescriptor { Size = uniformSize, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &uniforms);
            var entry = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = uniformSize };
            var pointGroup = new Native.BindGroupDescriptor { Layout = owner._pointLayout, EntryCount = 1, Entries = &entry };
            _pointGroup = Api.DeviceCreateBindGroup(owner._device.Device, &pointGroup);
            var spotGroup = new Native.BindGroupDescriptor { Layout = owner._spotLayout, EntryCount = 1, Entries = &entry };
            _spotGroup = Api.DeviceCreateBindGroup(owner._device.Device, &spotGroup);
        }

        public void Write(int place, ReadOnlySpan<LineVertex> points)
        {
            fixed (LineVertex* source = points)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _points, (ulong)place * TraceStride, source, (nuint)(points.Length * TraceStride));
            }
        }

        public void SetDraws(ReadOnlySpan<CloudDraw> draws, bool usesSpots)
        {
            if (_draws.Length < draws.Length)
            {
                _draws = new CloudDraw[draws.Length];
            }

            draws.CopyTo(_draws);
            (_drawCount, _usesSpots) = (draws.Length, usesSpots);
        }

        public void WriteUniforms(Matrix4x4 matrix, PixelSize size)
        {
            var uniforms = new TraceUniforms { Camera = matrix, Size = new Vector2(size.Width, size.Height), Cuts = _owner._cuts };
            Api.QueueWriteBuffer(_owner._device.Queue, _uniforms, 0, &uniforms, (nuint)sizeof(TraceUniforms));
        }

        public void Draw(Native.RenderPassEncoder* pass)
        {
            Api.RenderPassEncoderSetPipeline(pass, _usesSpots ? _owner._spotPipeline : _owner._pointPipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _usesSpots ? _spotGroup : _pointGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(pass, 0, _points, 0, _size);
            for (var i = 0; i < _drawCount; i++)
            {
                var (first, count) = (_draws[i].First, _draws[i].Count);
                if (_usesSpots)
                {
                    Api.RenderPassEncoderDraw(pass, TraceCorners, (uint)count, 0, (uint)first);
                }
                else
                {
                    Api.RenderPassEncoderDraw(pass, (uint)count, 1, (uint)first, 0);
                }
            }
        }

        public void Dispose()
        {
            Api.BindGroupRelease(_spotGroup);
            Api.BindGroupRelease(_pointGroup);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_points);
        }
    }
}
