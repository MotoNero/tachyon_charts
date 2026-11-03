using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// Runs compute shaders outside of frames. The results are needed at once, so the calls wait for the GPU.
/// </summary>
internal sealed unsafe class WebGpuCompute : IComputeOperations, IDisposable
{
    private const int WorkgroupSize = 16;
    private const uint ParamsSize = 16;
    private const uint DensityParamsSize = 32;
    private const int PointSize = 8;
    private const int PointsPerGroupRow = 1024 * 256;
    private const uint BindingAlignment = 256;

    private readonly WebGpuRenderDevice _device;
    private Native.ShaderModule* _contourShader;
    private Native.BindGroupLayout* _contourLayout;
    private Native.PipelineLayout* _contourPipelineLayout;
    private Native.ComputePipeline* _contourPipeline;
    private readonly Native.ShaderModule* _densityShader;
    private readonly Native.BindGroupLayout* _binLayout;
    private readonly Native.PipelineLayout* _binPipelineLayout;
    private Native.ComputePipeline* _binPipeline;
    private readonly Native.BindGroupLayout* _resolveLayout;
    private readonly Native.PipelineLayout* _resolvePipelineLayout;
    private Native.ComputePipeline* _resolvePipeline;
    private Native.Buffer* _bins;
    private ulong _binsSize;

    public WebGpuCompute(WebGpuRenderDevice device)
    {
        _device = device;
        var api = WebGpuRenderDevice.Api;
        _contourShader = device.CompileShader("contour.wgsl", WebGpuRenderDevice.LoadShaderSource("contour.wgsl"));

        var entries = stackalloc Native.BindGroupLayoutEntry[]
        {
            UniformEntry(0),
            new()
            {
                Binding = 1,
                Visibility = Native.ShaderStage.Compute,
                Texture = new Native.TextureBindingLayout
                {
                    SampleType = Native.TextureSampleType.UnfilterableFloat,
                    ViewDimension = Native.TextureViewDimension.Dimension2D,
                },
            },
            StorageEntry(2, Native.BufferBindingType.ReadOnlyStorage),
            StorageEntry(3, Native.BufferBindingType.Storage),
            StorageEntry(4, Native.BufferBindingType.Storage),
        };
        var groupDescriptor = new Native.BindGroupLayoutDescriptor { EntryCount = 5, Entries = entries };
        _contourLayout = api.DeviceCreateBindGroupLayout(device.Device, &groupDescriptor);

        var layout = _contourLayout;
        var layoutDescriptor = new Native.PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
        _contourPipelineLayout = api.DeviceCreatePipelineLayout(device.Device, &layoutDescriptor);
        _contourPipeline = CreatePipeline(_contourShader, _contourPipelineLayout, "main"u8);

        _densityShader = device.CompileShader("density.wgsl", WebGpuRenderDevice.LoadShaderSource("density.wgsl"));
        var binEntries = stackalloc Native.BindGroupLayoutEntry[]
        {
            UniformEntry(0),
            StorageEntry(1, Native.BufferBindingType.ReadOnlyStorage),
            StorageEntry(2, Native.BufferBindingType.Storage),
        };
        var binGroup = new Native.BindGroupLayoutDescriptor { EntryCount = 3, Entries = binEntries };
        _binLayout = api.DeviceCreateBindGroupLayout(device.Device, &binGroup);
        layout = _binLayout; // NOSONAR: read through a pointer in layoutDescriptor
        _binPipelineLayout = api.DeviceCreatePipelineLayout(device.Device, &layoutDescriptor);
        _binPipeline = CreatePipeline(_densityShader, _binPipelineLayout, "bin_points"u8);

        var resolveEntries = stackalloc Native.BindGroupLayoutEntry[]
        {
            UniformEntry(0),
            StorageEntry(2, Native.BufferBindingType.Storage),
            new()
            {
                Binding = 3,
                Visibility = Native.ShaderStage.Compute,
                StorageTexture = new Native.StorageTextureBindingLayout
                {
                    Access = Native.StorageTextureAccess.WriteOnly,
                    Format = Native.TextureFormat.R32float,
                    ViewDimension = Native.TextureViewDimension.Dimension2D,
                },
            },
            StorageEntry(4, Native.BufferBindingType.Storage),
        };
        var resolveGroup = new Native.BindGroupLayoutDescriptor { EntryCount = 4, Entries = resolveEntries };
        _resolveLayout = api.DeviceCreateBindGroupLayout(device.Device, &resolveGroup);
        layout = _resolveLayout; // NOSONAR: read through a pointer in layoutDescriptor
        _resolvePipelineLayout = api.DeviceCreatePipelineLayout(device.Device, &layoutDescriptor);
        _resolvePipeline = CreatePipeline(_densityShader, _resolvePipelineLayout, "resolve"u8);
    }

    public long BinPoints(IGpuBuffer points, int count, Vector2 scale, Vector2 offset, IGpuTexture target)
    {
        _device.Uploads.Flush();
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(target);
        if (points is not WebGpuBuffer source || source.Device != _device || (source.Usage & BufferUsage.Storage) == 0)
        {
            throw new ArgumentException("The points need a storage buffer of this device.", nameof(points));
        }

        if (target is not WebGpuTexture texture || texture.Device != _device || texture.Format != TextureFormat.R32Float)
        {
            throw new ArgumentException("The target must be an R32Float texture of this device.", nameof(target));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)count * PointSize, source.SizeInBytes, nameof(count));

        var api = WebGpuRenderDevice.Api;
        var size = texture.Size;
        var binsSize = (ulong)size.PixelCount * sizeof(uint);
        if (_bins is null || _binsSize != binsSize)
        {
            if (_bins is not null)
            {
                api.BufferRelease(_bins);
            }

            _bins = CreateBuffer(binsSize, Native.BufferUsage.Storage | Native.BufferUsage.CopyDst);
            _binsSize = binsSize;
        }

        var encoder = api.DeviceCreateCommandEncoder(_device.Device, null);
        api.CommandEncoderClearBuffer(encoder, _bins, 0, binsSize);
        Submit(encoder, null);

        // One binding reaches only so far, so a large cloud runs in parts.
        var pointsPerPart = (long)Math.Max((_device.MaxStorageBinding / BindingAlignment) * BindingAlignment / PointSize, 1);
        for (var first = 0L; first < count; first += pointsPerPart)
        {
            BinPart(source, first, (int)Math.Min(pointsPerPart, count - first), scale, offset, size);
        }

        var maximum = Resolve(texture, scale, offset, size);
        _device.ThrowIfDeviceError();
        return maximum;
    }

    private void BinPart(WebGpuBuffer source, long first, int count, Vector2 scale, Vector2 offset, PixelSize size)
    {
        var api = WebGpuRenderDevice.Api;
        var parameters = CreateDensityParams(scale, offset, size, (uint)count);
        Native.BindGroup* group = null;
        try
        {
            var entries = stackalloc Native.BindGroupEntry[]
            {
                new() { Binding = 0, Buffer = parameters, Size = DensityParamsSize },
                new() { Binding = 1, Buffer = source.Handle, Offset = (ulong)first * PointSize, Size = (ulong)count * PointSize },
                new() { Binding = 2, Buffer = _bins, Size = _binsSize },
            };
            var descriptor = new Native.BindGroupDescriptor { Layout = _binLayout, EntryCount = 3, Entries = entries };
            group = api.DeviceCreateBindGroup(_device.Device, &descriptor);

            var encoder = api.DeviceCreateCommandEncoder(_device.Device, null);
            var pass = api.CommandEncoderBeginComputePass(encoder, null);
            api.ComputePassEncoderSetPipeline(pass, _binPipeline);
            api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
            var rows = (uint)((count + PointsPerGroupRow - 1) / PointsPerGroupRow);
            api.ComputePassEncoderDispatchWorkgroups(pass, 1024, rows, 1);
            api.ComputePassEncoderEnd(pass);
            Submit(encoder, pass);
        }
        finally
        {
            if (group is not null)
            {
                api.BindGroupRelease(group);
            }

            api.BufferRelease(parameters);
        }
    }

    private long Resolve(WebGpuTexture texture, Vector2 scale, Vector2 offset, PixelSize size)
    {
        var api = WebGpuRenderDevice.Api;
        var parameters = CreateDensityParams(scale, offset, size, 0);
        var maximum = CreateBuffer(sizeof(uint), Native.BufferUsage.Storage | Native.BufferUsage.CopySrc | Native.BufferUsage.CopyDst);
        var staging = CreateBuffer(sizeof(uint), Native.BufferUsage.MapRead | Native.BufferUsage.CopyDst);
        Native.BindGroup* group = null;
        try
        {
            var entries = stackalloc Native.BindGroupEntry[]
            {
                new() { Binding = 0, Buffer = parameters, Size = DensityParamsSize },
                new() { Binding = 2, Buffer = _bins, Size = _binsSize },
                new() { Binding = 3, TextureView = texture.View },
                new() { Binding = 4, Buffer = maximum, Size = sizeof(uint) },
            };
            var descriptor = new Native.BindGroupDescriptor { Layout = _resolveLayout, EntryCount = 4, Entries = entries };
            group = api.DeviceCreateBindGroup(_device.Device, &descriptor);

            var encoder = api.DeviceCreateCommandEncoder(_device.Device, null);
            var pass = api.CommandEncoderBeginComputePass(encoder, null);
            api.ComputePassEncoderSetPipeline(pass, _resolvePipeline);
            api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
            api.ComputePassEncoderDispatchWorkgroups(pass, (uint)((size.Width + WorkgroupSize - 1) / WorkgroupSize), (uint)((size.Height + WorkgroupSize - 1) / WorkgroupSize), 1);
            api.ComputePassEncoderEnd(pass);
            api.CommandEncoderCopyBufferToBuffer(encoder, maximum, 0, staging, 0, sizeof(uint));
            Submit(encoder, pass);

            uint result = 0;
            ReadMapped(staging, new Span<byte>(&result, sizeof(uint)));
            return result;
        }
        finally
        {
            if (group is not null)
            {
                api.BindGroupRelease(group);
            }

            api.BufferRelease(parameters);
            api.BufferRelease(maximum);
            api.BufferRelease(staging);
        }
    }

    private Native.Buffer* CreateDensityParams(Vector2 scale, Vector2 offset, PixelSize size, uint count)
    {
        var buffer = CreateBuffer(DensityParamsSize, Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst);
        var values = stackalloc uint[8];
        ((Vector2*)values)[0] = scale;
        ((Vector2*)values)[1] = offset;
        values[4] = (uint)size.Width;
        values[5] = (uint)size.Height;
        values[6] = count;
        WebGpuRenderDevice.Api.QueueWriteBuffer(_device.Queue, buffer, 0, values, DensityParamsSize);
        return buffer;
    }

    public long GenerateContours(IGpuTexture values, ReadOnlySpan<float> levels, IGpuBuffer segments)
    {
        _device.Uploads.Flush();
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(segments);
        if (values is not WebGpuTexture texture || texture.Device != _device || texture.Format != TextureFormat.R32Float)
        {
            throw new ArgumentException("The values must be an R32Float texture of this device.", nameof(values));
        }

        if (segments is not WebGpuBuffer output || output.Device != _device || (output.Usage & BufferUsage.Storage) == 0)
        {
            throw new ArgumentException("The segments need a storage buffer of this device.", nameof(segments));
        }

        if (levels.IsEmpty)
        {
            return 0;
        }

        var api = WebGpuRenderDevice.Api;
        var capacity = (uint)Math.Min(output.SizeInBytes / IComputeOperations.ContourSegmentSize, uint.MaxValue);
        var paramsBuffer = CreateBuffer(ParamsSize, Native.BufferUsage.Uniform | Native.BufferUsage.CopyDst);
        var levelBuffer = CreateBuffer((ulong)levels.Length * sizeof(float), Native.BufferUsage.Storage | Native.BufferUsage.CopyDst);
        var counter = CreateBuffer(sizeof(uint), Native.BufferUsage.Storage | Native.BufferUsage.CopySrc | Native.BufferUsage.CopyDst);
        var staging = CreateBuffer(sizeof(uint), Native.BufferUsage.MapRead | Native.BufferUsage.CopyDst);
        Native.BindGroup* group = null;
        try
        {
            var parameters = stackalloc uint[] { (uint)levels.Length, capacity, 0, 0 };
            api.QueueWriteBuffer(_device.Queue, paramsBuffer, 0, parameters, ParamsSize);
            fixed (float* pointer = levels)
            {
                api.QueueWriteBuffer(_device.Queue, levelBuffer, 0, pointer, (nuint)(levels.Length * sizeof(float)));
            }

            var zero = 0u;
            api.QueueWriteBuffer(_device.Queue, counter, 0, &zero, sizeof(uint));

            var entries = stackalloc Native.BindGroupEntry[]
            {
                new() { Binding = 0, Buffer = paramsBuffer, Size = ParamsSize },
                new() { Binding = 1, TextureView = texture.View },
                new() { Binding = 2, Buffer = levelBuffer, Size = (ulong)levels.Length * sizeof(float) },
                new() { Binding = 3, Buffer = output.Handle, Size = (ulong)RenderGuard.AlignUp(output.SizeInBytes, RenderGuard.BufferWriteAlignment) },
                new() { Binding = 4, Buffer = counter, Size = sizeof(uint) },
            };
            var descriptor = new Native.BindGroupDescriptor { Layout = _contourLayout, EntryCount = 5, Entries = entries };
            group = api.DeviceCreateBindGroup(_device.Device, &descriptor);

            var size = texture.Size;
            var encoder = api.DeviceCreateCommandEncoder(_device.Device, null);
            var pass = api.CommandEncoderBeginComputePass(encoder, null);
            api.ComputePassEncoderSetPipeline(pass, _contourPipeline);
            api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
            api.ComputePassEncoderDispatchWorkgroups(pass, (uint)((size.Width + WorkgroupSize - 1) / WorkgroupSize), (uint)((size.Height + WorkgroupSize - 1) / WorkgroupSize), 1);
            api.ComputePassEncoderEnd(pass);
            api.CommandEncoderCopyBufferToBuffer(encoder, counter, 0, staging, 0, sizeof(uint));
            Submit(encoder, pass);

            uint count = 0;
            ReadMapped(staging, new Span<byte>(&count, sizeof(uint)));
            _device.ThrowIfDeviceError();
            return count;
        }
        finally
        {
            if (group is not null)
            {
                api.BindGroupRelease(group);
            }

            api.BufferRelease(paramsBuffer);
            api.BufferRelease(levelBuffer);
            api.BufferRelease(counter);
            api.BufferRelease(staging);
        }
    }

    public void ReadBuffer<T>(IGpuBuffer buffer, Span<T> destination)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer is not WebGpuBuffer source || source.Device != _device || (source.Usage & BufferUsage.Storage) == 0)
        {
            throw new ArgumentException("Only storage buffers of this device can be read.", nameof(buffer));
        }

        var bytes = MemoryMarshal.AsBytes(destination);
        if (bytes.IsEmpty)
        {
            return;
        }

        var size = (ulong)RenderGuard.AlignUp(bytes.Length, RenderGuard.BufferWriteAlignment);
        var api = WebGpuRenderDevice.Api;
        var staging = CreateBuffer(size, Native.BufferUsage.MapRead | Native.BufferUsage.CopyDst);
        try
        {
            var encoder = api.DeviceCreateCommandEncoder(_device.Device, null);
            api.CommandEncoderCopyBufferToBuffer(encoder, source.Handle, 0, staging, 0, size);
            Submit(encoder, null);
            ReadMapped(staging, bytes);
            _device.ThrowIfDeviceError();
        }
        finally
        {
            api.BufferRelease(staging);
        }
    }

    public void Dispose()
    {
        _device.Uploads.Flush();
        var api = WebGpuRenderDevice.Api;
        if (_bins is not null)
        {
            api.BufferRelease(_bins);
            _bins = null;
        }

        api.ComputePipelineRelease(_resolvePipeline);
        api.PipelineLayoutRelease(_resolvePipelineLayout);
        api.BindGroupLayoutRelease(_resolveLayout);
        api.ComputePipelineRelease(_binPipeline);
        api.PipelineLayoutRelease(_binPipelineLayout);
        api.BindGroupLayoutRelease(_binLayout);
        api.ShaderModuleRelease(_densityShader);
        _resolvePipeline = null;
        _binPipeline = null;
        api.ComputePipelineRelease(_contourPipeline);
        api.PipelineLayoutRelease(_contourPipelineLayout);
        api.BindGroupLayoutRelease(_contourLayout);
        api.ShaderModuleRelease(_contourShader);
        _contourPipeline = null;
        _contourPipelineLayout = null;
        _contourLayout = null;
        _contourShader = null;
    }

    private static Native.BindGroupLayoutEntry UniformEntry(uint binding) => new()
    {
        Binding = binding,
        Visibility = Native.ShaderStage.Compute,
        Buffer = new Native.BufferBindingLayout { Type = Native.BufferBindingType.Uniform },
    };

    private static Native.BindGroupLayoutEntry StorageEntry(uint binding, Native.BufferBindingType type) => new()
    {
        Binding = binding,
        Visibility = Native.ShaderStage.Compute,
        Buffer = new Native.BufferBindingLayout { Type = type },
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnMapped(Native.BufferMapAsyncStatus status, void* userData) =>
        *(Native.BufferMapAsyncStatus*)userData = status;

    private Native.ComputePipeline* CreatePipeline(Native.ShaderModule* shader, Native.PipelineLayout* layout, ReadOnlySpan<byte> entryPoint)
    {
        Span<byte> name = stackalloc byte[entryPoint.Length + 1];
        entryPoint.CopyTo(name);
        fixed (byte* entry = name)
        {
            var descriptor = new Native.ComputePipelineDescriptor
            {
                Layout = layout,
                Compute = new Native.ProgrammableStageDescriptor { Module = shader, EntryPoint = entry },
            };
            return WebGpuRenderDevice.Api.DeviceCreateComputePipeline(_device.Device, &descriptor);
        }
    }

    private Native.Buffer* CreateBuffer(ulong size, Native.BufferUsage usage)
    {
        var descriptor = new Native.BufferDescriptor { Usage = usage, Size = size };
        return WebGpuRenderDevice.Api.DeviceCreateBuffer(_device.Device, &descriptor);
    }

    private void Submit(Native.CommandEncoder* encoder, Native.ComputePassEncoder* pass)
    {
        var api = WebGpuRenderDevice.Api;
        var commands = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(_device.Queue, 1, &commands);
        api.CommandBufferRelease(commands);
        if (pass is not null)
        {
            api.ComputePassEncoderRelease(pass);
        }

        api.CommandEncoderRelease(encoder);
    }

    private void ReadMapped(Native.Buffer* staging, Span<byte> destination)
    {
        var api = WebGpuRenderDevice.Api;
        var size = (nuint)RenderGuard.AlignUp(destination.Length, RenderGuard.BufferWriteAlignment);
        var status = Native.BufferMapAsyncStatus.Unknown;
        api.BufferMapAsync(staging, Native.MapMode.Read, 0, size, new Native.PfnBufferMapCallback(&OnMapped), &status);
        WebGpuRenderDevice.Extension.DevicePoll(_device.Device, true, null);
        _device.ThrowIfDeviceError();
        if (status != Native.BufferMapAsyncStatus.Success)
        {
            throw new InvalidOperationException($"Mapping the readback buffer failed: {status}.");
        }

        var mapped = api.BufferGetConstMappedRange(staging, 0, size);
        new ReadOnlySpan<byte>(mapped, destination.Length).CopyTo(destination);
        api.BufferUnmap(staging);
    }
}
