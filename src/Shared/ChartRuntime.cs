using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Rendering.WebGPU;

#if AVALONIA
namespace Tachyon.Charts.Avalonia;
#else
namespace Tachyon.Charts.Wpf;
#endif

/// <summary>
/// The device and render thread all charts of a process share. It exists while charts do.
/// </summary>
internal sealed partial class ChartRuntime
{
    // The default timer of Windows ticks every 15.6 ms, so a wait of 1 ms can cost half of a frame at
    // 60 Hz and the render thread ends up at 32 FPS. It only applies while charts exist.
    private const uint TimerPeriodMilliseconds = 1;

    // A driver that keeps failing would otherwise take the charts down again and again.
    private const int LossesBeforeFallback = 3;

    private static readonly TimeSpan _lossWindow = TimeSpan.FromMinutes(1);
    private static readonly Lock _gate = new();
    private static readonly List<DateTime> _losses = [];
    private static readonly HashSet<RendererPreference> _excluded = [];
    private static ChartRuntime? _current;

    private readonly RendererPreference _kind;
    private int _users;
    private int _surfaces;
    private System.IO.StreamWriter? _frameLog;

    private ChartRuntime(IRenderDevice device, RendererPreference kind)
    {
        Device = device;
        _kind = kind;
        if (OperatingSystem.IsWindows())
        {
            timeBeginPeriod(TimerPeriodMilliseconds);
        }

        Scheduler = new RenderScheduler(device);
        Scheduler.DeviceLost += OnDeviceLost;
        StartFrameLog();
    }

    /// <summary>
    /// Occurs on the render thread when the device is lost. The charts stop and start again on the
    /// UI thread, which builds everything from the data on a new device.
    /// </summary>
    public event Action? DeviceLost;

    /// <summary>Gets the device, which belongs to the render thread.</summary>
    public IRenderDevice Device { get; }

    /// <summary>Gets the render thread.</summary>
    public RenderScheduler Scheduler { get; }

    /// <summary>Takes a share of the runtime, starting it if necessary.</summary>
    /// <param name="preference">The renderer of a new runtime.</param>
    /// <returns>The runtime. Call <see cref="Release"/> when done.</returns>
    public static ChartRuntime Acquire(RendererPreference preference)
    {
        using var scope = _gate.EnterScope();
        if (_current is null)
        {
            var candidates = Array.FindAll(_candidates, candidate => !_excluded.Contains(candidate.Kind));
            var blacklist = RendererSelector.ParseBlacklist(Environment.GetEnvironmentVariable("TACHYON_RENDERER_BLACKLIST"));
            var selection = RendererSelector.Select(preference, candidates, blacklist);
            _current = new ChartRuntime(selection.Device, selection.Attempts[^1].Kind);
        }

        _current._users++;
        return _current;
    }

    /// <summary>Makes the device behave as if it were lost, for tests.</summary>
    public void SimulateDeviceLoss() => Device.SimulateLoss();

    /// <summary>Gives a share back, ending the runtime with the last one.</summary>
    public void Release()
    {
        using var scope = _gate.EnterScope();
        if (--_users == 0)
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null; // NOSONAR: the instance owns the shared runtime
            }

            Scheduler.Invoke(() => SkiaTextEngine.Default.ReleaseAtlas(Device));
            Scheduler.Invoke(Device.WaitForIdle);
            Scheduler.Dispose();
            Device.Dispose();
            _frameLog?.Dispose();
            if (OperatingSystem.IsWindows())
            {
                timeEndPeriod(TimerPeriodMilliseconds);
            }
        }
    }

    // With TACHYON_FRAME_LOG set to a path, each pass adds a line: milliseconds since the start, CPU time of the pass,
    // points and uploaded bytes. This measures what a real window does, which no offscreen benchmark shows.
    private void StartFrameLog()
    {
        if (Environment.GetEnvironmentVariable("TACHYON_FRAME_LOG") is not { Length: > 0 } path)
        {
            return;
        }

        var log = _frameLog = new System.IO.StreamWriter(path) { AutoFlush = true };
        log.WriteLine("time_ms;pass_ms;points;upload_bytes");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Scheduler.PassCompleted += () =>
        {
            var pass = Scheduler.Statistics;
            log.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{clock.Elapsed.TotalMilliseconds:F1};{pass.LastPassTime.TotalMilliseconds:F2};{pass.LastPointCount};{pass.LastUploadedBytes}"));
        };
    }

    private void OnDeviceLost()
    {
        using (_gate.EnterScope())
        {
            // From now on a chart that starts gets a new runtime, while this one lives on until the last chart left.
            if (ReferenceEquals(_current, this))
            {
                _current = null; // NOSONAR: the instance owns the shared runtime
            }

            var now = DateTime.UtcNow;
            _losses.RemoveAll(time => now - time > _lossWindow);
            _losses.Add(now);
            if (_losses.Count >= LossesBeforeFallback && _kind != RendererPreference.Skia)
            {
                _excluded.Add(_kind);
                _losses.Clear();
            }
        }

        DeviceLost?.Invoke();
    }

    /// <summary>
    /// Counts a window on the screen. With several, VSync would make the render thread wait once per
    /// chart, so only a single window waits for the display.
    /// </summary>
    /// <param name="target">The target of the window.</param>
    public void AddSurface(ISurfaceRenderTarget target) => target.VSync = Interlocked.Increment(ref _surfaces) == 1;

    /// <summary>Counts a closed window.</summary>
    public void RemoveSurface() => Interlocked.Decrement(ref _surfaces);

    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint period);

    private static readonly RendererCandidate[] _candidates =
    [
        new(RendererPreference.Hardware, () => WebGpuRenderDevice.Create(WebGpuAdapterPreference.Hardware)),
        new(RendererPreference.Software, () => WebGpuRenderDevice.Create(WebGpuAdapterPreference.Software)),
        new(RendererPreference.Skia, () => new SkiaRenderDevice()),
    ];
}
