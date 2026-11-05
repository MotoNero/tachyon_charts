using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>A bubble for the GPU: its middle in the box, and its radius in the box or, negative, its diameter in pixels.</summary>
/// <param name="Center">The middle.</param>
/// <param name="Size">The radius, or the negative diameter in pixels.</param>
/// <param name="Color">The color.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct BubbleInstance(Vector3 Center, float Size, Rgba8 Color);

/// <summary>What the uniforms of bubbles hold. The order follows bubbles.wgsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BubbleUniforms
{
    public Matrix4x4 Camera;
    public Vector4 Eye;
    public Vector4 Material;
    public CutPlanes Cuts;
    public Vector4 Size;
}

/// <content>The bubbles.</content>
internal sealed unsafe partial class Renderer3D
{
    private const uint BubbleCorners = 6;

    private readonly Dictionary<Bubbles3D, BubbleBuffer> _bubbles = [];
    private Native.RenderPipeline* _bubblePipeline;
    private Native.BindGroupLayout* _bubbleLayout;

    /// <summary>Takes over the bubbles of an object.</summary>
    /// <param name="bubbles">The object.</param>
    /// <param name="instances">The bubbles in the box.</param>
    public void Upload(Bubbles3D bubbles, ReadOnlySpan<BubbleInstance> instances)
    {
        if (!_bubbles.TryGetValue(bubbles, out var buffer) || buffer.Capacity < instances.Length)
        {
            buffer?.Dispose();
            buffer = new BubbleBuffer(this, bubbles.Material, instances.Length);
            _bubbles[bubbles] = buffer;
        }

        buffer.Write(instances);
    }

    /// <summary>Gets a value indicating whether the bubbles of an object are on the GPU.</summary>
    /// <param name="bubbles">The object.</param>
    /// <returns>Whether they are.</returns>
    public bool Holds(Bubbles3D bubbles) => _bubbles.ContainsKey(bubbles);

    private void CreateBubblePipeline()
    {
        var shader = _device.CompileShader("bubbles.wgsl", ReadShader("clip.wgsl") + ReadShader("light.wgsl") + ReadShader("bubbles.wgsl"));

        // The alpha of the shader only decides on the coverage of the edge. The picture stays opaque.
        var target = new Native.ColorTargetState { Format = ColorFormat, WriteMask = Native.ColorWriteMask.Red | Native.ColorWriteMask.Green | Native.ColorWriteMask.Blue };
        var depth = CreateDepthState(writesDepth: true);
        var attributes = stackalloc Native.VertexAttribute[3];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 16, ShaderLocation = 2 };
        var layout = new Native.VertexBufferLayout { ArrayStride = (ulong)Unsafe.SizeOf<BubbleInstance>(), StepMode = Native.VertexStepMode.Instance, AttributeCount = 3, Attributes = attributes };
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
            _bubblePipeline = Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }

        _bubbleLayout = Api.RenderPipelineGetBindGroupLayout(_bubblePipeline, 0);
        Api.ShaderModuleRelease(shader);
    }

    private void WriteBubbleUniforms(Matrix4x4 matrix, Vector3 eye, PixelSize size)
    {
        foreach (var bubbles in _bubbles.Values)
        {
            bubbles.WriteUniforms(matrix, eye, size);
        }
    }

    private void DrawBubbles(Native.RenderPassEncoder* pass)
    {
        if (_bubbles.Count == 0)
        {
            return;
        }

        Api.RenderPassEncoderSetPipeline(pass, _bubblePipeline);
        foreach (var bubbles in _bubbles.Values)
        {
            bubbles.Draw(pass);
        }
    }

    private long BubbleBytes()
    {
        var bytes = 0L;
        foreach (var bubbles in _bubbles.Values)
        {
            bytes += bubbles.Bytes;
        }

        return bytes;
    }

    private void ReleaseBubbles()
    {
        foreach (var bubbles in _bubbles.Values)
        {
            bubbles.Dispose();
        }

        Api.BindGroupLayoutRelease(_bubbleLayout);
        Api.RenderPipelineRelease(_bubblePipeline);
    }

    /// <summary>The bubbles of one object on the GPU.</summary>
    private sealed class BubbleBuffer : IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly Material3D _material;
        private readonly Native.Buffer* _instances;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _bindGroup;
        private int _count;

        public BubbleBuffer(Renderer3D owner, Material3D material, int capacity)
        {
            (_owner, _material, Capacity) = (owner, material, capacity);
            Bytes = (long)Math.Max(capacity, 1) * Unsafe.SizeOf<BubbleInstance>();
            var instances = new Native.BufferDescriptor { Size = (ulong)Bytes, Usage = Native.BufferUsage.Vertex | Native.BufferUsage.CopyDst };
            _instances = Api.DeviceCreateBuffer(owner._device.Device, &instances);
            var uniformSize = (ulong)sizeof(BubbleUniforms);
            var uniforms = new Native.BufferDescriptor { Size = uniformSize, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &uniforms);
            var entry = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = uniformSize };
            var group = new Native.BindGroupDescriptor { Layout = owner._bubbleLayout, EntryCount = 1, Entries = &entry };
            _bindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
        }

        public int Capacity { get; }

        /// <summary>Gets the bytes it holds on the graphics card.</summary>
        public long Bytes { get; }

        public void Write(ReadOnlySpan<BubbleInstance> instances)
        {
            _count = instances.Length;
            if (_count == 0)
            {
                return;
            }

            fixed (BubbleInstance* source = instances)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _instances, 0, source, (nuint)(instances.Length * Unsafe.SizeOf<BubbleInstance>()));
            }
        }

        public void WriteUniforms(Matrix4x4 matrix, Vector3 eye, PixelSize size)
        {
            var uniforms = new BubbleUniforms
            {
                Camera = matrix,
                Eye = new Vector4(eye, 1),
                Material = new Vector4((float)_material.Gloss, (float)_material.Sharpness, 1, _material.IsLit ? 1 : 0),
                Cuts = _owner._cuts,
                Size = new Vector4(size.Width, size.Height, 0, 0),
            };
            Api.QueueWriteBuffer(_owner._device.Queue, _uniforms, 0, &uniforms, (nuint)sizeof(BubbleUniforms));
        }

        public void Draw(Native.RenderPassEncoder* pass)
        {
            if (_count == 0)
            {
                return;
            }

            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(pass, 0, _instances, 0, (ulong)Bytes);
            Api.RenderPassEncoderDraw(pass, BubbleCorners, (uint)_count, 0, 0);
        }

        public void Dispose()
        {
            Api.BindGroupRelease(_bindGroup);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_instances);
        }
    }
}
