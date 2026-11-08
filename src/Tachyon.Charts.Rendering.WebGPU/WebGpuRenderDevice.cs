using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU.Extensions.WGPU;
using Native = Silk.NET.WebGPU;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// A render device on wgpu-native. Silk.NET.WebGPU 2.23.0 provides the bindings and the native binaries.
/// </summary>
/// <remarks>
/// Surface targets exist for Win32 windows only. Metal, Xlib and Wayland arrive with the Avalonia spike.
/// </remarks>
public sealed unsafe partial class WebGpuRenderDevice : IRenderDevice
{
    internal const string AdapterVariable = "TACHYON_WEBGPU_ADAPTER";

    // Backends in order of preference. OpenGL is excluded, only D3D12, Metal and Vulkan are targets.
    private static readonly Native.BackendType[] _backendRanking =
    [
        Native.BackendType.Metal,
        Native.BackendType.D3D12,
        Native.BackendType.Vulkan,
    ];

    private readonly GCHandle _selfHandle;
    private Native.Instance* _instance;
    private Native.Adapter* _adapter;
    private Native.Device* _device;
    private Native.Queue* _queue;
    private FrameRecorder? _recorder;
    private WebGpuCompute? _compute;
    private WebGpuCommandList? _openFrame;
    private WebGpuCommandList? _freeFrame;
    private string? _uncapturedError;
    private bool _disposed;
    private readonly LossReport _lossReport;
    private GCHandle _lossHandle; // NOSONAR: keeps the native callback alive

    private WebGpuRenderDevice(Native.Instance* instance, Native.Adapter* adapter, Native.Device* device, string adapterName, GCHandle lossHandle)
    {
        _lossHandle = lossHandle;
        _lossReport = (LossReport)lossHandle.Target!;
        Uploads = new UploadBatch(this);
        _instance = instance;
        _adapter = adapter;
        _device = device;
        _queue = Api.DeviceGetQueue(device);

        // wgpu-native aborts the process on an uncaptured error unless a callback is set.
        _selfHandle = GCHandle.Alloc(this);
        Api.DeviceSetUncapturedErrorCallback(
            device,
            new Native.PfnErrorCallback(&OnUncapturedError),
            (void*)GCHandle.ToIntPtr(_selfHandle));

        Native.SupportedLimits limits = default;
        Api.DeviceGetLimits(device, &limits);
        MinUniformOffsetAlignment = limits.Limits.MinUniformBufferOffsetAlignment;
        HasFilterableFloat32 = Api.DeviceHasFeature(device, Float32Filterable);
        MaxStorageBinding = limits.Limits.MaxStorageBufferBindingSize;
        MaxTextureDimension3D = limits.Limits.MaxTextureDimension3D;
        Native.AdapterProperties properties = default;
        Api.AdapterGetProperties(adapter, &properties);
        GraphicsMemory = properties.AdapterType == Native.AdapterType.Cpu ? 0 : GraphicsMemoryProbe.Find(properties.VendorID, properties.DeviceID);
        Capabilities = new DeviceCapabilities
        {
            BackendName = $"WebGPU ({adapterName})",
            SupportsCompute = true,
            MaxBufferSize = (long)Math.Min(limits.Limits.MaxBufferSize, long.MaxValue),
            MaxTextureDimension = (int)Math.Min(limits.Limits.MaxTextureDimension2D, int.MaxValue),
            MaxSampleCount = 4,
        };

        _recorder = new FrameRecorder(this);
    }

    /// <inheritdoc />
    public DeviceCapabilities Capabilities { get; }

    /// <inheritdoc />
    public bool IsLost => _lossReport.IsLost;

    /// <inheritdoc />
    public void SimulateLoss() => _lossReport.IsLost = true;

    /// <inheritdoc />
    public IComputeOperations Compute
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _compute ??= new WebGpuCompute(this);
        }
    }

    /// <inheritdoc />
    public long DrawCallCount { get; internal set; }

    /// <inheritdoc />
    public long UploadedByteCount { get; internal set; }

    internal static Native.WebGPU Api { get; } = LoadApi();

    internal static Wgpu Extension { get; } = new(Api.Context);

    internal Native.Instance* Instance => _instance;

    internal Native.Adapter* Adapter => _adapter;

    internal Native.Device* Device => _device;

    internal Native.Queue* Queue => _queue;

    /// <summary>Gets a value indicating whether 32-bit floats can be filtered, which keeps 16-bit volumes exact.</summary>
    internal bool HasFilterableFloat32 { get; }

    internal UploadBatch Uploads { get; }

    internal uint MinUniformOffsetAlignment { get; }

    internal ulong MaxStorageBinding { get; }

    /// <summary>Gets the largest side of a 3D texture.</summary>
    internal uint MaxTextureDimension3D { get; }

    /// <summary>Gets the memory of the graphics card in bytes, or 0 where the system does not tell.</summary>
    internal long GraphicsMemory { get; }

    /// <summary>
    /// Creates a texture, or returns <see langword="null"/> when the graphics card has no memory or no room for it.
    /// Validation errors still surface as usual.
    /// </summary>
    /// <param name="descriptor">The texture.</param>
    /// <returns>The texture, or <see langword="null"/>.</returns>
    internal Native.Texture* TryCreateTexture(Native.TextureDescriptor* descriptor)
    {
        var capture = new ErrorCapture();
        var captureHandle = GCHandle.Alloc(capture);
        try
        {
            Api.DevicePushErrorScope(_device, Native.ErrorFilter.OutOfMemory);
            var texture = Api.DeviceCreateTexture(_device, descriptor);
            Api.DevicePopErrorScope(_device, new Native.PfnErrorCallback(&OnErrorScopePopped), (void*)GCHandle.ToIntPtr(captureHandle));
            if (capture.Message is null)
            {
                return texture;
            }

            if (texture is not null)
            {
                Api.TextureRelease(texture);
            }

            return null;
        }
        finally
        {
            captureHandle.Free();
        }
    }

    internal FrameRecorder Recorder => _recorder!;

    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Creates a device on the adapter selected by the <c>TACHYON_WEBGPU_ADAPTER</c> environment
    /// variable, <c>hardware</c> (the default) or <c>software</c>.
    /// </summary>
    /// <returns>The new device.</returns>
    /// <exception cref="InvalidOperationException">The variable is invalid, or no suitable adapter exists.</exception>
    public static WebGpuRenderDevice Create() =>
        Create(ParseAdapterPreference(Environment.GetEnvironmentVariable(AdapterVariable)));

    /// <summary>
    /// Creates a device.
    /// </summary>
    /// <param name="preference">The kind of adapter to use.</param>
    /// <returns>The new device.</returns>
    /// <exception cref="InvalidOperationException">No suitable adapter exists.</exception>
    public static WebGpuRenderDevice Create(WebGpuAdapterPreference preference)
    {
        // This wgpu build creates a surface for the first backend of the instance only, and that is
        // Vulkan if a Vulkan loader exists. Restricting the instance keeps surfaces usable on D3D12.
        var extras = new InstanceExtras
        {
            Chain = new Native.ChainedStruct { SType = (Native.SType)NativeSType.STypeInstanceExtras },
            Backends = OperatingSystem.IsWindows() ? InstanceBackend.DX12 : InstanceBackend.Primary,
        };
        var instanceDescriptor = new Native.InstanceDescriptor { NextInChain = &extras.Chain };
        var instance = Api.CreateInstance(&instanceDescriptor);
        if (instance is null)
        {
            throw new InvalidOperationException("WebGPU instance creation failed.");
        }

        Native.Adapter* adapter = null;
        try
        {
            adapter = SelectAdapter(instance, preference, out var adapterName);
            var lossHandle = GCHandle.Alloc(new LossReport());
            var device = RequestDevice(adapter, lossHandle);
            return new WebGpuRenderDevice(instance, adapter, device, adapterName, lossHandle);
        }
        catch
        {
            if (adapter is not null)
            {
                Api.AdapterRelease(adapter);
            }

            Api.InstanceRelease(instance);
            throw;
        }
    }

    /// <inheritdoc />
    public IGpuBuffer CreateBuffer(BufferDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(desc.SizeInBytes, Capabilities.MaxBufferSize, nameof(desc));

        return new WebGpuBuffer(this, desc);
    }

    /// <inheritdoc />
    public IGpuTexture CreateTexture(TextureDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ObjectDisposedException.ThrowIf(_disposed, this);
        RenderGuard.PositiveSize(desc.Size, Capabilities.MaxTextureDimension);

        return new WebGpuTexture(this, desc);
    }

    /// <inheritdoc />
    public IOffscreenRenderTarget CreateOffscreenTarget(PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RenderGuard.PositiveSize(size, Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);

        return new WebGpuOffscreenTarget(this, size, dpiScale);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The window system is not Win32.</exception>
    public ISurfaceRenderTarget CreateSurfaceTarget(NativeSurfaceHandle handle, PixelSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (handle.Window == 0)
        {
            throw new ArgumentException("The surface handle is zero.", nameof(handle));
        }

        if (handle.Kind != NativeSurfaceKind.Win32Window)
        {
            throw new NotSupportedException($"Surfaces of kind {handle.Kind} are not implemented yet.");
        }

        RenderGuard.PositiveSize(size, Capabilities.MaxTextureDimension);
        RenderGuard.DpiScale(dpiScale);

        return new WebGpuSurfaceTarget(this, handle, size, dpiScale);
    }

    /// <inheritdoc />
    public ICommandList BeginFrame(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsLost)
        {
            throw new DeviceLostException();
        }

        if (target is not WebGpuTarget webGpuTarget || webGpuTarget.Device != this)
        {
            throw new ArgumentException("The target belongs to another device.", nameof(target));
        }

        webGpuTarget.ThrowIfDisposed();
        if (_openFrame is not null)
        {
            throw new InvalidOperationException("Another frame is still open.");
        }

        Recorder.Reset();
        var frame = _freeFrame ?? new WebGpuCommandList(this);
        _freeFrame = null;
        frame.Begin(webGpuTarget);
        _openFrame = frame;
        return frame;
    }

    /// <inheritdoc />
    public void BeginBatch() => Recorder.BeginBatch();

    /// <inheritdoc />
    public void EndBatch()
    {
        Recorder.EndBatch();
        ThrowIfDeviceError();
    }

    /// <inheritdoc />
    public void WaitForIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsLost)
        {
            return;
        }

        Extension.DevicePoll(_device, true, null);
        ThrowIfDeviceError();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Resources keep the native device alive through wgpu reference counting, so their later
        // disposal stays safe.
        _compute?.Dispose();
        _compute = null;
        _recorder?.Dispose();
        _recorder = null;
        Api.QueueRelease(_queue);
        Api.DeviceRelease(_device);
        Api.AdapterRelease(_adapter);
        Api.InstanceRelease(_instance);
        _queue = null;
        _device = null;
        _adapter = null;
        _instance = null;
        _selfHandle.Free();
    }

    internal static WebGpuAdapterPreference ParseAdapterPreference(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" or "hardware" => WebGpuAdapterPreference.Hardware,
        "software" => WebGpuAdapterPreference.Software,
        _ => throw new InvalidOperationException($"{AdapterVariable} must be 'hardware' or 'software', not '{value}'."),
    };

    internal static string LoadShaderSource(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName)
            ?? throw new InvalidOperationException($"Embedded shader '{fileName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Finds the error position in a wgpu diagnostic. The header names the start of the enclosing span,
    /// for validation errors that is the whole function. The first caret line marks the actual expression.
    /// </summary>
    /// <returns>The 1-based position, 0 where the diagnostic does not provide it.</returns>
    internal static (int Line, int Column) ParseShaderLocation(string diagnostic)
    {
        var lines = diagnostic.Split('\n');
        var headerIndex = Array.FindIndex(lines, line => ShaderHeaderRegex().IsMatch(line));
        if (headerIndex < 0)
        {
            return (0, 0);
        }

        var header = ShaderHeaderRegex().Match(lines[headerIndex]);
        var headerLine = int.Parse(header.Groups[1].ValueSpan);
        var headerColumn = int.Parse(header.Groups[2].ValueSpan);

        var sourceLine = 0;
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var numbered = ShaderSourceLineRegex().Match(lines[i]);
            if (numbered.Success)
            {
                sourceLine = int.Parse(numbered.Groups[1].ValueSpan);
            }
            else if (sourceLine > 0 && lines[i].Contains('^'))
            {
                return sourceLine == headerLine ? (headerLine, headerColumn) : (sourceLine, 0);
            }
        }

        return (headerLine, headerColumn);
    }

    internal bool IsOpenFrameOn(WebGpuTarget target) => _openFrame?.Target == target;

    internal void OnFrameClosed(WebGpuCommandList frame)
    {
        if (_openFrame == frame)
        {
            _openFrame = null;
        }
    }

    internal void ReturnFrame(WebGpuCommandList frame) => _freeFrame = frame;

    /// <summary>
    /// Compiles WGSL only to report compiler errors.
    /// </summary>
    internal void ValidateShader(string fileName, string source) => Api.ShaderModuleRelease(CompileShader(fileName, source));

    /// <summary>
    /// Compiles WGSL, reporting compiler errors with file and line.
    /// </summary>
    internal Native.ShaderModule* CompileShader(string fileName, string source)
    {
        var sourceBytes = new byte[Encoding.UTF8.GetByteCount(source) + 1];
        Encoding.UTF8.GetBytes(source, sourceBytes);

        var labelPointer = Marshal.StringToCoTaskMemUTF8(fileName);
        var capture = new ErrorCapture();
        var captureHandle = GCHandle.Alloc(capture);
        try
        {
            Native.ShaderModule* module;
            fixed (byte* code = sourceBytes)
            {
                var wgsl = new Native.ShaderModuleWGSLDescriptor
                {
                    Chain = new Native.ChainedStruct { SType = Native.SType.ShaderModuleWgslDescriptor },
                    Code = code,
                };
                var descriptor = new Native.ShaderModuleDescriptor
                {
                    NextInChain = &wgsl.Chain,
                    Label = (byte*)labelPointer,
                };

                Api.DevicePushErrorScope(_device, Native.ErrorFilter.Validation);
                module = Api.DeviceCreateShaderModule(_device, &descriptor);
                Api.DevicePopErrorScope(
                    _device,
                    new Native.PfnErrorCallback(&OnErrorScopePopped),
                    (void*)GCHandle.ToIntPtr(captureHandle));
            }

            if (capture.Message is null)
            {
                return module;
            }

            if (module is not null)
            {
                Api.ShaderModuleRelease(module);
            }

            var (line, column) = ParseShaderLocation(capture.Message);
            throw new ShaderCompilationException(fileName, line, column, capture.Message);
        }
        finally
        {
            captureHandle.Free();
            Marshal.FreeCoTaskMem(labelPointer);
        }
    }

    /// <summary>
    /// Turns the first error reported outside an error scope into an exception.
    /// </summary>
    internal void ThrowIfDeviceError()
    {
        Extension.DevicePoll(_device, false, null);
        if (IsLost)
        {
            _uncapturedError = null;
            throw new DeviceLostException();
        }

        if (_uncapturedError is { } message)
        {
            _uncapturedError = null;
            throw new InvalidOperationException($"WebGPU error: {message}");
        }
    }

    private static Native.WebGPU LoadApi()
    {
        // Not WebGPU.GetApi(): its path resolver misses runtimes/linux-x64 on Linux distributions,
        // while the .NET host resolves the native assets from the deps file on every platform.
        var library = NativeLibrary.Load("wgpu_native", typeof(WebGpuRenderDevice).Assembly, null);
        return new Native.WebGPU(new LamdaNativeContext(
            name => NativeLibrary.TryGetExport(library, name, out var address) ? address : 0));
    }

    private static Native.Adapter* SelectAdapter(
        Native.Instance* instance,
        WebGpuAdapterPreference preference,
        out string adapterName)
    {
        var count = (int)Extension.InstanceEnumerateAdapters(instance, null, null);
        var adapters = new Native.Adapter*[count];
        Native.Adapter* best = null;
        var bestRank = int.MaxValue;
        adapterName = string.Empty;

        fixed (Native.Adapter** adapterPointers = adapters)
        {
            Extension.InstanceEnumerateAdapters(instance, null, adapterPointers);
        }

        foreach (var adapter in adapters)
        {
            Native.AdapterProperties properties = default;
            Api.AdapterGetProperties(adapter, &properties);

            var rank = RankAdapter(properties.AdapterType, properties.BackendType, preference);
            if (rank < bestRank)
            {
                if (best is not null)
                {
                    Api.AdapterRelease(best);
                }

                best = adapter;
                bestRank = rank;
                adapterName = $"{properties.BackendType}, {Marshal.PtrToStringUTF8((nint)properties.Name)}";
            }
            else
            {
                Api.AdapterRelease(adapter);
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException($"No {preference} WebGPU adapter found among {count} adapters.");
        }

        return best;
    }

    internal static int RankAdapter(Native.AdapterType type, Native.BackendType backend, WebGpuAdapterPreference preference)
    {
        var backendRank = Array.IndexOf(_backendRanking, backend);
        if (backendRank < 0)
        {
            return int.MaxValue;
        }

        var typeRank = (preference, type) switch
        {
            (WebGpuAdapterPreference.Hardware, Native.AdapterType.DiscreteGpu) => 0,
            (WebGpuAdapterPreference.Hardware, Native.AdapterType.IntegratedGpu) => 1,
            (WebGpuAdapterPreference.Software, Native.AdapterType.Cpu) => 0,
            _ => -1,
        };

        return typeRank < 0 ? int.MaxValue : (typeRank * _backendRanking.Length) + backendRank;
    }

    // The value from webgpu.h. Silk.NET 2.23.0 does not name it, wgpu-native answers for it all the same.
    private const Native.FeatureName Float32Filterable = (Native.FeatureName)11;

    private static Native.Device* RequestDevice(Native.Adapter* adapter, GCHandle lossHandle)
    {
        // The adapter limits instead of the WebGPU defaults: large series need large buffers.
        Native.SupportedLimits supported = default;
        Api.AdapterGetLimits(adapter, &supported);
        var required = new Native.RequiredLimits { Limits = supported.Limits };

        // Optional: exact volumes need filtered 32-bit floats. Without it they fall back to 16-bit floats.
        var filterable = Float32Filterable;
        var hasFilterable = Api.AdapterHasFeature(adapter, filterable);

        // Silk.NET 2.23.0 sets this callback only here, with the device that does not exist yet.
        var descriptor = new Native.DeviceDescriptor
        {
            RequiredLimits = &required,
            RequiredFeatureCount = hasFilterable ? 1u : 0u,
            RequiredFeatures = &filterable,
            DeviceLostCallback = new Native.PfnDeviceLostCallback(&OnDeviceLost),
            DeviceLostUserdata = (void*)GCHandle.ToIntPtr(lossHandle),
        };

        var capture = new ErrorCapture();
        var captureHandle = GCHandle.Alloc(capture);
        try
        {
            // wgpu-native invokes this callback before returning.
            Api.AdapterRequestDevice(
                adapter,
                &descriptor,
                new Native.PfnRequestDeviceCallback(&OnDeviceReceived),
                (void*)GCHandle.ToIntPtr(captureHandle));

            return capture.Device is not null
                ? capture.Device
                : throw new InvalidOperationException($"WebGPU device request failed: {capture.Message}");
        }
        finally
        {
            captureHandle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDeviceReceived(Native.RequestDeviceStatus status, Native.Device* device, byte* message, void* userData)
    {
        var capture = (ErrorCapture)GCHandle.FromIntPtr((nint)userData).Target!;
        capture.Device = status == Native.RequestDeviceStatus.Success ? device : null;
        capture.Message = Marshal.PtrToStringUTF8((nint)message);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnErrorScopePopped(Native.ErrorType type, byte* message, void* userData)
    {
        if (type != Native.ErrorType.NoError)
        {
            var capture = (ErrorCapture)GCHandle.FromIntPtr((nint)userData).Target!;
            capture.Message = Marshal.PtrToStringUTF8((nint)message) ?? type.ToString();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUncapturedError(Native.ErrorType type, byte* message, void* userData)
    {
        var device = (WebGpuRenderDevice)GCHandle.FromIntPtr((nint)userData).Target!;
        device._uncapturedError ??= Marshal.PtrToStringUTF8((nint)message) ?? type.ToString();
    }

    // A device that is released on purpose reports its end too, which is no loss.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDeviceLost(Native.DeviceLostReason reason, byte* message, void* userData)
    {
        if (reason != Native.DeviceLostReason.Destroyed)
        {
            ((LossReport)GCHandle.FromIntPtr((nint)userData).Target!).IsLost = true;
        }
    }

    // The header of a wgpu diagnostic, for example "┌─ flat.wgsl:3:9".
    [GeneratedRegex(@"┌─ .*:(\d+):(\d+)")]
    private static partial Regex ShaderHeaderRegex();

    // A quoted source line of a wgpu diagnostic, for example "3 │     return a;".
    [GeneratedRegex(@"^\s*(\d+) │")]
    private static partial Regex ShaderSourceLineRegex();

    private sealed class LossReport
    {
        private volatile bool _isLost;

        public bool IsLost
        {
            get => _isLost;
            set => _isLost = value;
        }
    }

    private sealed class ErrorCapture
    {
        public string? Message { get; set; }

        public Native.Device* Device { get; set; }
    }
}
