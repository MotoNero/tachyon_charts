using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.ThreeD.Scene;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>Receives the slices of a volume.</summary>
internal interface IVolumeTarget
{
    /// <summary>Gets a value indicating whether 32 bits per value reach the GPU exactly.</summary>
    bool IsExact { get; }

    /// <summary>Writes a slice of shares.</summary>
    /// <param name="z">The slice.</param>
    /// <param name="shares">The values.</param>
    void Write(int z, ReadOnlySpan<float> shares);

    /// <summary>Writes a slice of bytes.</summary>
    /// <param name="z">The slice.</param>
    /// <param name="bytes">The values.</param>
    void Write(int z, ReadOnlySpan<byte> bytes);
}

/// <summary>What the uniforms of a volume hold. The order follows volume.wgsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VolumeUniforms
{
    public Matrix4x4 Camera;
    public CutPlanes Cuts;
    public Vector4 Low;
    public Vector4 High;
    public Vector4 Slices;
    public Vector4 March;
    public Vector4 Look;
    public Vector4 Eye;
    public Vector4 Material;
    public Vector4 NearA;
    public Vector4 NearB;
    public Vector4 NearC;
    public Vector4 FarA;
    public Vector4 FarB;
    public Vector4 FarC;
}

/// <summary>How a frame looks through a volume.</summary>
/// <param name="March">The length of a step, the reach for the slope, and the power for the opacity.</param>
/// <param name="Near">The corners of the big triangle at the near end of the depth.</param>
/// <param name="Far">The corners at the far end.</param>
/// <param name="Look">How the rays look, see volume.wgsl.</param>
/// <param name="Eye">The place of the eye.</param>
/// <param name="Material">How a surface takes the light.</param>
internal readonly record struct VolumeView(Vector4 March, (Vector4 A, Vector4 B, Vector4 C) Near, (Vector4 A, Vector4 B, Vector4 C) Far, Vector4 Look = default, Vector4 Eye = default, Vector4 Material = default);

/// <content>The volumes.</content>
internal sealed unsafe partial class Renderer3D
{
    private const uint SliceCorners = 6;
    private const uint SliceCount = 3;
    private const uint TransferBytes = TransferFunction3D.TableSize * 16;

    private readonly Dictionary<Volume3D, VolumeBuffer> _volumes = [];
    private Native.RenderPipeline* _slicePipeline;
    private Native.BindGroupLayout* _sliceLayout;
    private Native.Sampler* _volumeSampler;
    private Native.RenderPipeline* _marchPipeline;
    private Native.BindGroupLayout* _marchLayout;

    /// <summary>Gets the texture of a volume. It takes its full size at once.</summary>
    /// <param name="volume">The volume.</param>
    /// <param name="colors">The colormap.</param>
    /// <returns>The texture.</returns>
    public IVolumeTarget GetVolume(Volume3D volume, ReadOnlySpan<Vector4> colors)
    {
        if (!_volumes.TryGetValue(volume, out var buffer))
        {
            buffer = new VolumeBuffer(this, volume.Data, colors);
            _volumes[volume] = buffer;
        }

        return buffer;
    }

    /// <summary>Sets the place of a volume.</summary>
    /// <param name="volume">The volume.</param>
    /// <param name="low">The lower corner in the box.</param>
    /// <param name="high">The upper corner in the box.</param>
    /// <param name="slices">The places of the slices and the threshold.</param>
    /// <param name="showsSlices">Whether the slices show.</param>
    public void SetPlace(Volume3D volume, Vector3 low, Vector3 high, Vector4 slices, bool showsSlices)
    {
        _volumes[volume].SetPlace(low, high, slices);
        _volumes[volume].ShowsSlices = showsSlices;
    }

    /// <summary>Sets how a frame looks through a volume.</summary>
    /// <param name="volume">The volume.</param>
    /// <param name="table">The transfer function.</param>
    /// <param name="view">The view.</param>
    public void SetMarch(Volume3D volume, ReadOnlySpan<Vector4> table, in VolumeView view) => _volumes[volume].SetMarch(table, view);

    private void CreateVolumePipelines()
    {
        var shader = _device.CompileShader("volume.wgsl", ReadShader("clip.wgsl") + ReadShader("light.wgsl") + ReadShader("volume.wgsl"));
        var target = new Native.ColorTargetState { Format = ColorFormat, WriteMask = Native.ColorWriteMask.All };
        var depth = CreateDepthState(writesDepth: true);
        fixed (byte* vertexEntry = "vs_slice\0"u8, fragmentEntry = "fs_slice\0"u8)
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
            _slicePipeline = Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }

        _sliceLayout = Api.RenderPipelineGetBindGroupLayout(_slicePipeline, 0);
        var blend = new Native.BlendState
        {
            Color = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
            Alpha = new Native.BlendComponent { Operation = Native.BlendOperation.Add, SrcFactor = Native.BlendFactor.One, DstFactor = Native.BlendFactor.OneMinusSrcAlpha },
        };
        var marchTarget = new Native.ColorTargetState { Format = ColorFormat, Blend = &blend, WriteMask = Native.ColorWriteMask.All };
        fixed (byte* vertexEntry = "vs_march\0"u8, fragmentEntry = "fs_march\0"u8)
        {
            var fragment = new Native.FragmentState { Module = shader, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &marchTarget };
            var descriptor = new Native.RenderPipelineDescriptor
            {
                Vertex = new Native.VertexState { Module = shader, EntryPoint = vertexEntry },
                Primitive = new Native.PrimitiveState { Topology = Native.PrimitiveTopology.TriangleList, FrontFace = Native.FrontFace.Ccw, CullMode = Native.CullMode.None },
                Multisample = new Native.MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            _marchPipeline = Api.DeviceCreateRenderPipeline(_device.Device, &descriptor);
        }

        _marchLayout = Api.RenderPipelineGetBindGroupLayout(_marchPipeline, 0);
        Api.ShaderModuleRelease(shader);
        var sampler = new Native.SamplerDescriptor
        {
            AddressModeU = Native.AddressMode.ClampToEdge,
            AddressModeV = Native.AddressMode.ClampToEdge,
            AddressModeW = Native.AddressMode.ClampToEdge,
            MagFilter = Native.FilterMode.Linear,
            MinFilter = Native.FilterMode.Linear,
            MaxAnisotropy = 1,
        };
        _volumeSampler = Api.DeviceCreateSampler(_device.Device, &sampler);
    }

    private void WriteVolumeUniforms(Matrix4x4 matrix)
    {
        foreach (var volume in _volumes.Values)
        {
            volume.WriteUniforms(matrix);
        }
    }

    private void DrawSlices(Native.RenderPassEncoder* pass)
    {
        if (_volumes.Count == 0)
        {
            return;
        }

        Api.RenderPassEncoderSetPipeline(pass, _slicePipeline);
        foreach (var volume in _volumes.Values)
        {
            volume.DrawSlices(pass);
        }
    }

    private bool HasMarch()
    {
        foreach (var volume in _volumes.Values)
        {
            if (volume.Marches)
            {
                return true;
            }
        }

        return false;
    }

    private void EncodeMarch(Native.CommandEncoder* encoder)
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
        Api.RenderPassEncoderSetPipeline(pass, _marchPipeline);
        foreach (var volume in _volumes.Values)
        {
            volume.DrawMarch(pass, _depth.View);
        }

        Api.RenderPassEncoderEnd(pass);
        Api.RenderPassEncoderRelease(pass);
    }

    private void ForgetMarchGroups()
    {
        foreach (var volume in _volumes.Values)
        {
            volume.ForgetMarchGroup();
        }
    }

    private void ReleaseVolumes()
    {
        foreach (var volume in _volumes.Values)
        {
            volume.Dispose();
        }

        Api.SamplerRelease(_volumeSampler);
        Api.BindGroupLayoutRelease(_marchLayout);
        Api.RenderPipelineRelease(_marchPipeline);
        Api.BindGroupLayoutRelease(_sliceLayout);
        Api.RenderPipelineRelease(_slicePipeline);
    }

    /// <summary>The texture, colors and uniforms of a volume on the GPU.</summary>
    private sealed class VolumeBuffer : IVolumeTarget, IDisposable
    {
        private readonly Renderer3D _owner;
        private readonly VolumeData _data;
        private readonly Native.TextureFormat _format;
        private readonly Native.Texture* _texture;
        private readonly Native.TextureView* _view;
        private readonly Native.Buffer* _colors;
        private readonly Native.Buffer* _uniforms;
        private readonly Native.BindGroup* _sliceGroup;
        private readonly Native.Buffer* _transfer;
        private Native.BindGroup* _marchGroup;
        private ulong _colorBytes;
        private Half[] _halves = [];
        private VolumeUniforms _values;

        /// <summary>Gets the bytes it holds on the graphics card.</summary>
        public long Bytes { get; }

        public VolumeBuffer(Renderer3D owner, VolumeData data, ReadOnlySpan<Vector4> colors)
        {
            (_owner, _data) = (owner, data);
            IsExact = owner._device.HasFilterableFloat32;
            _format = PickFormat(data, IsExact);
            var voxelBytes = _format == Native.TextureFormat.R8Unorm ? 1L : _format == Native.TextureFormat.R16float ? 2L : 4L; // NOSONAR: three cases of one choice
            Bytes = ((long)data.Width * data.Height * data.Depth * voxelBytes) + (colors.Length * 16L) + TransferBytes;
            var texture = new Native.TextureDescriptor
            {
                Usage = Native.TextureUsage.TextureBinding | Native.TextureUsage.CopyDst,
                Dimension = Native.TextureDimension.Dimension3D,
                Size = new Native.Extent3D((uint)data.Width, (uint)data.Height, (uint)data.Depth),
                Format = _format,
                MipLevelCount = 1,
                SampleCount = 1,
            };
            _texture = Api.DeviceCreateTexture(owner._device.Device, &texture);
            _view = Api.TextureCreateView(_texture, null);
            var colorBytes = MemoryMarshal.AsBytes(colors);
            _colorBytes = (ulong)colorBytes.Length;
            var colorBuffer = new Native.BufferDescriptor { Size = (ulong)colorBytes.Length, Usage = Native.BufferUsage.Storage | Native.BufferUsage.CopyDst };
            _colors = Api.DeviceCreateBuffer(owner._device.Device, &colorBuffer);
            fixed (byte* source = colorBytes)
            {
                Api.QueueWriteBuffer(owner._device.Queue, _colors, 0, source, (nuint)colorBytes.Length);
            }

            var table = new Native.BufferDescriptor { Size = TransferBytes, Usage = Native.BufferUsage.Storage | Native.BufferUsage.CopyDst };
            _transfer = Api.DeviceCreateBuffer(owner._device.Device, &table);
            var size = (ulong)sizeof(VolumeUniforms);
            var uniforms = new Native.BufferDescriptor { Size = size, Usage = Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst };
            _uniforms = Api.DeviceCreateBuffer(owner._device.Device, &uniforms);
            var entries = stackalloc Native.BindGroupEntry[4];
            entries[0] = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = size };
            entries[1] = new Native.BindGroupEntry { Binding = 1, TextureView = _view };
            entries[2] = new Native.BindGroupEntry { Binding = 2, Sampler = owner._volumeSampler };
            entries[3] = new Native.BindGroupEntry { Binding = 3, Buffer = _colors, Size = (ulong)(colors.Length * sizeof(Vector4)) };
            var group = new Native.BindGroupDescriptor { Layout = owner._sliceLayout, EntryCount = 4, Entries = entries };
            _sliceGroup = Api.DeviceCreateBindGroup(owner._device.Device, &group);
        }

        public bool IsExact { get; }

        public bool ShowsSlices { get; set; } = true;

        public bool Marches { get; private set; }

        public void Write(int z, ReadOnlySpan<float> shares)
        {
            if (_format == Native.TextureFormat.R32float)
            {
                fixed (float* source = shares)
                {
                    WriteSlice(z, source, (nuint)(shares.Length * sizeof(float)), sizeof(float));
                }

                return;
            }

            if (_halves.Length < shares.Length)
            {
                _halves = new Half[shares.Length];
            }

            for (var i = 0; i < shares.Length; i++)
            {
                _halves[i] = (Half)shares[i];
            }

            fixed (Half* source = _halves)
            {
                WriteSlice(z, source, (nuint)(shares.Length * sizeof(Half)), sizeof(Half));
            }
        }

        public void Write(int z, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* source = bytes)
            {
                WriteSlice(z, source, (nuint)bytes.Length, 1);
            }
        }

        public void SetPlace(Vector3 low, Vector3 high, Vector4 slices) =>
            (_values.Low, _values.High, _values.Slices) = (new Vector4(low, 0), new Vector4(high, 0), slices);

        public void SetMarch(ReadOnlySpan<Vector4> table, in VolumeView view)
        {
            Marches = true;
            (_values.March, _values.Look, _values.Eye, _values.Material) = (view.March, view.Look, view.Eye, view.Material);
            (_values.NearA, _values.NearB, _values.NearC) = view.Near;
            (_values.FarA, _values.FarB, _values.FarC) = view.Far;
            fixed (Vector4* source = table)
            {
                Api.QueueWriteBuffer(_owner._device.Queue, _transfer, 0, source, TransferBytes);
            }
        }

        public void ForgetMarchGroup()
        {
            if (_marchGroup is not null)
            {
                Api.BindGroupRelease(_marchGroup);
                _marchGroup = null;
            }
        }

        public void DrawMarch(Native.RenderPassEncoder* pass, Native.TextureView* depth)
        {
            if (!Marches)
            {
                return;
            }

            if (_marchGroup is null)
            {
                var entries = stackalloc Native.BindGroupEntry[6];
                entries[0] = new Native.BindGroupEntry { Binding = 0, Buffer = _uniforms, Size = (ulong)sizeof(VolumeUniforms) };
                entries[1] = new Native.BindGroupEntry { Binding = 1, TextureView = _view };
                entries[2] = new Native.BindGroupEntry { Binding = 2, Sampler = _owner._volumeSampler };
                entries[3] = new Native.BindGroupEntry { Binding = 3, Buffer = _colors, Size = _colorBytes };
                entries[4] = new Native.BindGroupEntry { Binding = 4, Buffer = _transfer, Size = TransferBytes };
                entries[5] = new Native.BindGroupEntry { Binding = 5, TextureView = depth };
                var group = new Native.BindGroupDescriptor { Layout = _owner._marchLayout, EntryCount = 6, Entries = entries };
                _marchGroup = Api.DeviceCreateBindGroup(_owner._device.Device, &group);
            }

            Api.RenderPassEncoderSetBindGroup(pass, 0, _marchGroup, 0, null);
            Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
        }

        public void WriteUniforms(Matrix4x4 matrix)
        {
            (_values.Camera, _values.Cuts) = (matrix, _owner._cuts);
            var values = _values;
            Api.QueueWriteBuffer(_owner._device.Queue, _uniforms, 0, &values, (nuint)sizeof(VolumeUniforms));
        }

        public void DrawSlices(Native.RenderPassEncoder* pass)
        {
            if (!ShowsSlices)
            {
                return;
            }

            Api.RenderPassEncoderSetBindGroup(pass, 0, _sliceGroup, 0, null);
            Api.RenderPassEncoderDraw(pass, SliceCorners, SliceCount, 0, 0);
        }

        public void Dispose()
        {
            ForgetMarchGroup();
            Api.BufferRelease(_transfer);
            Api.BindGroupRelease(_sliceGroup);
            Api.BufferRelease(_uniforms);
            Api.BufferRelease(_colors);
            Api.TextureViewRelease(_view);
            Api.TextureRelease(_texture);
        }

        private static Native.TextureFormat PickFormat(VolumeData data, bool isExact)
        {
            if (data.IsBytes)
            {
                return Native.TextureFormat.R8Unorm;
            }

            // Filtering 32-bit floats is an optional feature. Without it, 16-bit floats keep about 11 bits of a value.
            return isExact ? Native.TextureFormat.R32float : Native.TextureFormat.R16float;
        }

        private void WriteSlice(int z, void* source, nuint bytes, int bytesPerValue)
        {
            var layout = new Native.TextureDataLayout { BytesPerRow = (uint)(_data.Width * bytesPerValue), RowsPerImage = (uint)_data.Height };
            var extent = new Native.Extent3D((uint)_data.Width, (uint)_data.Height, 1);
            var destination = new Native.ImageCopyTexture { Texture = _texture, Origin = new Native.Origin3D(0, 0, (uint)z), Aspect = Native.TextureAspect.All };
            Api.QueueWriteTexture(_owner._device.Queue, &destination, source, bytes, &layout, &extent);
        }
    }
}
