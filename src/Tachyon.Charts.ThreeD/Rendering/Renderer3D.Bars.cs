using System.Numerics;
using System.Runtime.CompilerServices;
using Tachyon.Charts.ThreeD.Scene;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <content>The columns.</content>
internal sealed unsafe partial class Renderer3D
{
    private const uint BarCorners = 36;

    private readonly Dictionary<Bars3D, BarBuffer> _bars = [];
    private Native.RenderPipeline* _opaqueBars;
    private Native.RenderPipeline* _transparentBars;
    private Native.BindGroupLayout* _opaqueBarLayout;
    private Native.BindGroupLayout* _transparentBarLayout;

    /// <summary>Takes over columns. A new number creates the buffer.</summary>
    /// <param name="bars">The object they belong to.</param>
    /// <param name="instances">The columns.</param>
    /// <param name="place">The cells in the box, which give the order of the rows.</param>
    public void Update(Bars3D bars, ReadOnlySpan<BarInstance> instances, in GridPlace place)
    {
        if (!_bars.TryGetValue(bars, out var buffer) || buffer.Count != instances.Length)
        {
            buffer?.Dispose();
            buffer = new BarBuffer(this, bars.Material, instances.Length);
            _bars[bars] = buffer;
        }

        buffer.Place = place;
        buffer.Write(instances);
    }

    private void CreateBarPipelines()
    {
        var shader = _device.CompileShader("bars.wgsl", ReadShader("clip.wgsl") + ReadShader("light.wgsl") + ReadShader("bars.wgsl"));
        var attributes = stackalloc Native.VertexAttribute[3];
        attributes[0] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x4, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new Native.VertexAttribute { Format = Native.VertexFormat.Float32x2, Offset = 16, ShaderLocation = 1 };
        attributes[2] = new Native.VertexAttribute { Format = Native.VertexFormat.Unorm8x4, Offset = 24, ShaderLocation = 2 };
        var layout = new Native.VertexBufferLayout { ArrayStride = (ulong)Unsafe.SizeOf<BarInstance>(), StepMode = Native.VertexStepMode.Instance, AttributeCount = 3, Attributes = attributes };
        _opaqueBars = CreateMeshPipeline(shader, isTransparent: false, hasVertices: true, instances: &layout);
        _transparentBars = CreateMeshPipeline(shader, isTransparent: true, hasVertices: true, instances: &layout);
        _opaqueBarLayout = Api.RenderPipelineGetBindGroupLayout(_opaqueBars, 0);
        _transparentBarLayout = Api.RenderPipelineGetBindGroupLayout(_transparentBars, 0);
        Api.ShaderModuleRelease(shader);
    }

    private bool WriteBarUniforms(Matrix4x4 matrix, Vector3 eye)
    {
        var hasTransparency = false;
        foreach (var bars in _bars.Values)
        {
            bars.WriteUniforms(matrix, eye);
            hasTransparency |= bars.IsTransparent;
        }

        return hasTransparency;
    }

    private void DrawBars(Native.RenderPassEncoder* pass, bool isTransparent, float eyeY)
    {
        Api.RenderPassEncoderSetPipeline(pass, isTransparent ? _transparentBars : _opaqueBars);
        foreach (var bars in _bars.Values)
        {
            if (bars.IsTransparent == isTransparent)
            {
                bars.Draw(pass, eyeY);
            }
        }
    }

    private void ReleaseBars()
    {
        foreach (var bars in _bars.Values)
        {
            bars.Dispose();
        }

        Api.BindGroupLayoutRelease(_opaqueBarLayout);
        Api.BindGroupLayoutRelease(_transparentBarLayout);
        Api.RenderPipelineRelease(_opaqueBars);
        Api.RenderPipelineRelease(_transparentBars);
    }

    /// <summary>The columns of one object on the GPU.</summary>
    private sealed class BarBuffer : IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly Material3D _material;
        private readonly ulong _size;

        /// <summary>Gets the bytes it holds on the graphics card.</summary>
        public long Bytes => (long)_size;
        private readonly Native.Buffer* _instances;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _bindGroup;

        public BarBuffer(Renderer3D owner, Material3D material, int count)
        {
            (_owner, _material, Count) = (owner, material, count);
            _size = (ulong)Math.Max(count, 1) * (ulong)Unsafe.SizeOf<BarInstance>();
            var instances = new Native.BufferDescriptor { Size = _size, Usage = Native.BufferUsage.Vertex | Native.BufferUsage.CopyDst };
            _instances = Api.DeviceCreateBuffer(owner._device.Device, &instances);
            var uniformSize = (ulong)Unsafe.SizeOf<MeshUniforms>();
            var uniforms = new Native.BufferDescriptor { Size = uniformSize, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &uniforms);
            var entry = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = uniformSize };
            var group = new Native.BindGroupDescriptor { Layout = IsTransparent ? owner._transparentBarLayout : owner._opaqueBarLayout, EntryCount = 1, Entries = &entry };
            _bindGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
        }

        public int Count { get; }

        public GridPlace Place { get; set; }

        public bool IsTransparent => _material.Opacity < 1;

        public void Write(ReadOnlySpan<BarInstance> instances)
        {
            fixed (BarInstance* source = instances)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _instances, 0, source, (nuint)(instances.Length * Unsafe.SizeOf<BarInstance>()));
            }
        }

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

        // Row by row away from the eye: the depth test then rejects what lies behind before any light is computed.
        public void Draw(Native.RenderPassEncoder* pass, float eyeY)
        {
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(pass, 0, _instances, 0, _size);
            var (columns, rows) = (Place.Columns - 1, Place.Rows - 1);
            var nearest = Math.Clamp((int)((eyeY - Place.Front) / Place.StepY), 0, rows - 1);
            for (var row = nearest; row < rows; row++)
            {
                Api.RenderPassEncoderDraw(pass, BarCorners, (uint)columns, 0, (uint)(row * columns));
            }

            for (var row = nearest - 1; row >= 0; row--)
            {
                Api.RenderPassEncoderDraw(pass, BarCorners, (uint)columns, 0, (uint)(row * columns));
            }
        }

        public void Dispose()
        {
            Api.BindGroupRelease(_bindGroup);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_instances);
        }
    }
}
