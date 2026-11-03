using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// Collects buffer writes of a render pass into one staging buffer. A queue write costs about 0.3 ms,
/// which adds up to most of a frame with many series. The copies run in one encoder.
/// </summary>
/// <remarks>Everything here runs on the render thread. It works with raw handles, hence unsafe.</remarks>
internal sealed unsafe class UploadBatch(WebGpuRenderDevice device)
{
    private const int CopyAlignment = 4;
    private const int FlushSize = 1 << 28;

    private readonly List<Copy> _copies = [];
    private byte[] _data = new byte[1 << 16];
    private int _used;

    /// <summary>Gets or sets a value indicating whether writes are collected.</summary>
    public bool IsActive { get; set; }

    /// <summary>Tells whether a write can wait for <see cref="Flush"/>.</summary>
    /// <param name="offset">The offset in the target.</param>
    /// <param name="byteCount">The size.</param>
    /// <returns>Whether <see cref="Add"/> takes it.</returns>
    public bool Accepts(long offset, long byteCount) =>
        IsActive && offset % CopyAlignment == 0 && byteCount % CopyAlignment == 0 && byteCount < FlushSize;

    /// <summary>Takes a write.</summary>
    /// <param name="target">The buffer.</param>
    /// <param name="offset">The offset in the buffer.</param>
    /// <param name="bytes">The data.</param>
    public void Add(Native.Buffer* target, ulong offset, ReadOnlySpan<byte> bytes)
    {
        if (_used + bytes.Length > FlushSize)
        {
            Flush();
        }

        if (_used + bytes.Length > _data.Length)
        {
            Array.Resize(ref _data, Math.Max(_data.Length * 2, _used + bytes.Length));
        }

        bytes.CopyTo(_data.AsSpan(_used));
        _copies.Add(new Copy((nint)target, offset, (ulong)_used, (ulong)bytes.Length));
        _used += bytes.Length;
    }

    /// <summary>Hands all waiting writes to the device.</summary>
    public void Flush()
    {
        if (_copies.Count == 0)
        {
            return;
        }

        var api = WebGpuRenderDevice.Api;
        var descriptor = new Native.BufferDescriptor
        {
            Size = (ulong)_used,
            Usage = Native.BufferUsage.CopySrc,
            MappedAtCreation = true,
        };
        var staging = api.DeviceCreateBuffer(device.Device, &descriptor);
        var mapped = api.BufferGetMappedRange(staging, 0, (nuint)_used);
        _data.AsSpan(0, _used).CopyTo(new Span<byte>(mapped, _used));
        api.BufferUnmap(staging);

        var encoder = api.DeviceCreateCommandEncoder(device.Device, null);
        foreach (var copy in _copies)
        {
            api.CommandEncoderCopyBufferToBuffer(encoder, staging, copy.Source, (Native.Buffer*)copy.Target, copy.Offset, copy.Size);
        }

        var commands = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(device.Queue, 1, &commands);
        api.CommandBufferRelease(commands);
        api.CommandEncoderRelease(encoder);
        api.BufferRelease(staging);
        _copies.Clear();
        _used = 0;
    }

    private readonly record struct Copy(nint Target, ulong Offset, ulong Source, ulong Size);
}
