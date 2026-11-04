using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// When frames are produced.
/// </summary>
public enum RenderMode
{
    /// <summary>
    /// Only after a change of data, axes, size or an explicit request.
    /// </summary>
    OnDemand,

    /// <summary>
    /// Every frame interval, for animations.
    /// </summary>
    Continuous,
}

/// <summary>
/// What the last frames of a scheduler cost.
/// </summary>
/// <param name="FrameCount">The frames rendered so far, counting each chart.</param>
/// <param name="LastPassTime">
/// The CPU time of the last pass over all charts that rendered, up to handing the frames to the device.
/// The GPU may still be working, so this is no measure of how long an image takes.
/// </param>
/// <param name="LastDrawCalls">The draw calls of that pass.</param>
/// <param name="LastPointCount">The points drawn in that pass.</param>
/// <param name="LastUploadedBytes">The bytes written to buffers in that pass.</param>
public readonly record struct FrameStatistics(long FrameCount, TimeSpan LastPassTime, long LastDrawCalls, long LastPointCount, long LastUploadedBytes);

/// <summary>
/// The render thread of a device: all charts on the device render here, one after the other.
/// </summary>
/// <remarks>
/// Data sources do not notify, the scheduler compares their versions once per frame interval. That keeps
/// producers free of any cost and merges all changes between two frames. Members are safe from any thread.
/// The caller keeps owning the device, the charts and the targets; they must outlive the scheduler or be
/// detached first. Create and resize targets through <see cref="Invoke"/>.
/// </remarks>
public sealed class RenderScheduler : IDisposable
{
    private static readonly long _minimumIdleWait = Stopwatch.Frequency / 1000;

    private readonly IRenderDevice _device;
    private readonly Thread _thread;
    private readonly Lock _gate = new();
    private readonly Queue<Action> _work = new();
    private readonly List<ManualResetEventSlim> _flushRequests = [];
    private readonly List<ManualResetEventSlim> _flushing = [];
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly List<Attachment> _charts = [];
    private bool _isLossReported;

    private volatile bool _stopping;
    private bool _disposed;
    private int _mode;
    private long _intervalTicks = TimeSpan.FromSeconds(1.0 / 60).Ticks;
    private FrameStatistics _statistics;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderScheduler"/> class and starts the render thread.
    /// </summary>
    /// <param name="device">The device, used only on the render thread from now on.</param>
    public RenderScheduler(IRenderDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _thread = new Thread(Run) { IsBackground = true, Name = "Tachyon render thread" };
        _thread.Start();
    }

    /// <summary>
    /// Occurs on the render thread when a frame or a posted action fails. The thread keeps running, and
    /// the chart stays without a new frame until its next change.
    /// </summary>
    public event Action<Exception>? Failed;

    /// <summary>
    /// Occurs once on the render thread when the device is lost. Nothing renders from then on: the
    /// owner replaces this scheduler and its device.
    /// </summary>
    public event Action? DeviceLost;

    /// <summary>
    /// Occurs on the render thread after a pass that rendered at least one frame. Handlers read the state
    /// of charts here and pass it to the UI thread as a copy.
    /// </summary>
    public event Action? PassCompleted;

    /// <summary>
    /// Gets or sets when frames are produced. The default is <see cref="RenderMode.OnDemand"/>.
    /// </summary>
    public RenderMode Mode
    {
        get => (RenderMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    /// <summary>
    /// Gets or sets the shortest time between two frames, which is also how often versions are compared.
    /// The default is a sixtieth of a second. Zero leaves the pace to a surface with VSync.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan FrameInterval
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _intervalTicks));
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            Interlocked.Exchange(ref _intervalTicks, value.Ticks);
        }
    }

    /// <summary>
    /// Gets the diagnostic counters.
    /// </summary>
    public FrameStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return _statistics;
            }
        }
    }

    /// <summary>
    /// Renders a chart on a target from now on, starting with a frame.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="target">A render target of the device.</param>
    /// <exception cref="ArgumentException">The chart is already attached.</exception>
    public void Attach(ChartSurface chart, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(target);
        Invoke(() =>
        {
            if (_charts.Exists(item => item.Chart == chart))
            {
                throw new ArgumentException("The chart is already attached.", nameof(chart)); // NOSONAR: chart is the parameter of the enclosing method
            }

            chart.Invalidate();
            _charts.Add(new Attachment(chart, target));
        });
    }

    /// <summary>
    /// Stops rendering a chart. When this returns, the render thread no longer touches it.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <returns>Whether the chart was attached.</returns>
    public bool Detach(ChartSurface chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var removed = false;
        Invoke(() => removed = _charts.RemoveAll(item => item.Chart == chart) > 0);
        return removed;
    }

    /// <summary>
    /// Queues a change for the render thread, for example of an axis. Changes run before the next frame.
    /// </summary>
    /// <param name="action">The change. A failure is reported through <see cref="Failed"/>.</param>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _work.Enqueue(action);
        }

        _wake.Set();
    }

    /// <summary>Gets the device. It belongs to the render thread.</summary>
    public IRenderDevice Device => _device;

    /// <summary>
    /// Runs an action on the render thread and waits for it. Not for hot paths.
    /// </summary>
    /// <param name="action">The action. A failure is thrown here.</param>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        ExceptionDispatchInfo? failure = null;
        Post(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        failure?.Throw();
    }

    /// <summary>
    /// Waits until everything changed so far is rendered, ignoring the frame interval.
    /// </summary>
    /// <exception cref="InvalidOperationException">The caller is the render thread.</exception>
    public void Flush()
    {
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            throw new InvalidOperationException("The render thread cannot wait for itself.");
        }

        using var done = new ManualResetEventSlim(false);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _flushRequests.Add(done);
        }

        _wake.Set();
        done.Wait();
    }

    /// <summary>
    /// Stops the render thread after the queued work.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stopping = true;
        _wake.Set();
        if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
        {
            _thread.Join();
            _wake.Dispose();
        }
    }

    private void Run()
    {
        var nextPass = 0L;
        while (!_stopping)
        {
            _wake.Reset();
            RunWork();

            var isFlushing = TakeFlushRequests();
            var now = Stopwatch.GetTimestamp();
            if (isFlushing || now >= nextPass)
            {
                var passStart = now;
                var rendered = RenderPass();
                ReleaseFlushRequests();

                // From the planned time, not the real start: waking a millisecond late would add up to 57 frames per
                // second instead of 60. A surface that waited for the display is part of the interval. Far behind
                // the plan, the next pass starts anew rather than catching up in a burst.
                now = Stopwatch.GetTimestamp();
                var interval = (long)(FrameInterval.TotalSeconds * Stopwatch.Frequency);
                var planned = nextPass + interval;
                if (!rendered)
                {
                    nextPass = now + Math.Max(interval, _minimumIdleWait);
                }
                else
                {
                    nextPass = planned > passStart ? planned : passStart + interval;
                }
            }

            var wait = nextPass - now;
            if (wait > 0)
            {
                _wake.Wait(1 + (int)(wait * 1000 / Stopwatch.Frequency));
            }
        }

        // Waiters must not hang when the scheduler stops.
        RunWork();
        TakeFlushRequests();
        ReleaseFlushRequests();
    }

    private void RunWork()
    {
        while (true)
        {
            Action? action;
            lock (_gate)
            {
                if (!_work.TryDequeue(out action))
                {
                    return;
                }
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                Failed?.Invoke(exception);
            }
        }
    }

    private bool TakeFlushRequests()
    {
        lock (_gate)
        {
            _flushing.AddRange(_flushRequests);
            _flushRequests.Clear();
        }

        return _flushing.Count > 0;
    }

    private void ReleaseFlushRequests()
    {
        foreach (var request in _flushing)
        {
            request.Set();
        }

        _flushing.Clear();
    }

    private void ReportLoss()
    {
        if (_isLossReported)
        {
            return;
        }

        _isLossReported = true;
        try
        {
            DeviceLost?.Invoke();
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private bool RenderPass()
    {
        if (_device.IsLost)
        {
            ReportLoss();
            return false;
        }

        var isContinuous = Mode == RenderMode.Continuous;
        var start = Stopwatch.GetTimestamp();
        var drawCalls = _device.DrawCallCount;
        var uploads = _device.UploadedByteCount;
        var frames = 0;
        var points = 0L;

        _device.BeginBatch();
        foreach (var (chart, target) in _charts)
        {
            if (isContinuous || chart.HasChanges(target))
            {
                try
                {
                    chart.Render(_device, target);
                    frames++;
                    points += chart.DrawnPointCount;
                }
                catch (DeviceLostException)
                {
                    ReportLoss();
                    return false;
                }
                catch (Exception exception) when (_device.IsLost)
                {
                    // Whatever a dying device reports, it means the same.
                    _ = exception;
                    ReportLoss();
                    return false;
                }
                catch (Exception exception)
                {
                    Failed?.Invoke(exception);
                }
            }
        }

        try
        {
            _device.EndBatch();
        }
        catch (DeviceLostException)
        {
            ReportLoss();
            return false;
        }
        catch (Exception exception) when (_device.IsLost)
        {
            _ = exception;
            ReportLoss();
            return false;
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }

        if (frames == 0)
        {
            return false;
        }

        var time = Stopwatch.GetElapsedTime(start);
        lock (_gate)
        {
            _statistics = new FrameStatistics(_statistics.FrameCount + frames, time, _device.DrawCallCount - drawCalls, points, _device.UploadedByteCount - uploads);
        }

        try
        {
            PassCompleted?.Invoke();
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }

        return true;
    }

    private readonly record struct Attachment(ChartSurface Chart, IRenderTarget Target);
}
