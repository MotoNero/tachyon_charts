using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Export;
using Tachyon.Charts.Rendering;

#if AVALONIA
namespace Tachyon.Charts.Avalonia;
#else
namespace Tachyon.Charts.Wpf;
#endif

/// <summary>
/// Records the charts of an area together: a timer asks the render thread they share for a picture of all of them at
/// each frame. One picture at a time; a slow frame leaves out ticks, and the recorder fills the time.
/// </summary>
internal sealed class AreaRecording : IChartRecording
{
    private readonly ChartRecorder _recorder;
    private readonly RenderScheduler _scheduler;
    private readonly Timer _timer;
    private readonly Action? _stopped;
    private IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> _charts;
    private int _isCapturing;

    private AreaRecording(ChartRecorder recorder, RenderScheduler scheduler, IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> charts, int frameRate, Action? stopped)
    {
        (_recorder, _scheduler, _charts, _stopped) = (recorder, scheduler, charts, stopped);
        _timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _isCapturing, 1) == 0)
            {
                _scheduler.Post(() =>
                {
                    try
                    {
                        _recorder.Capture(Collect(Volatile.Read(ref _charts), _scheduler), _scheduler.Device);
                    }
                    finally
                    {
                        Volatile.Write(ref _isCapturing, 0);
                    }
                });
            }
        }, null, 0, Math.Max(1000 / frameRate / 2, 1));
    }

    /// <inheritdoc />
    public TimeSpan Elapsed => _recorder.Elapsed;

    /// <summary>Starts a recording.</summary>
    /// <param name="charts">The charts and their places in pixels of the area.</param>
    /// <param name="size">The size of the area in pixels.</param>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The frame rate and encoding, or <see langword="null"/>; the size comes from the area.</param>
    /// <param name="stopped">Called when the recording stops, for example to end the search for charts.</param>
    /// <returns>The recording.</returns>
    public static AreaRecording Start(IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> charts, PixelSize size, string path, VideoOptions? options, Action? stopped = null)
    {
        var scheduler = SchedulerOf(charts);
        var recorder = ChartRecorder.Start(path, size, options ?? new VideoOptions());
        return new AreaRecording(recorder, scheduler, charts, (options ?? new VideoOptions()).FrameRate, stopped);
    }

    /// <summary>Writes a planned video of the charts, frame by frame at fixed times.</summary>
    /// <param name="charts">The charts and their places in pixels of the area.</param>
    /// <param name="size">The size of the area in pixels.</param>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The length and encoding, or <see langword="null"/>; the size comes from the area.</param>
    /// <param name="step">Called before each frame.</param>
    /// <returns>When the file is written.</returns>
    public static Task ExportVideoAsync(IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> charts, PixelSize size, string path, VideoOptions? options, Action<int, TimeSpan>? step)
    {
        var scheduler = SchedulerOf(charts);
        return Task.Run(() => scheduler.Invoke(() => ChartVideo.Write(Collect(charts, scheduler), scheduler.Device, path, size, options ?? new VideoOptions(), step)));
    }

    /// <summary>Replaces the charts and their places, as the area has them now: a chart that has its size only now, or a window that grew.</summary>
    /// <param name="charts">The charts and their places.</param>
    public void Update(IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> charts) => Volatile.Write(ref _charts, charts);

    /// <inheritdoc />
    public async Task StopAsync()
    {
        _stopped?.Invoke();
        await _timer.DisposeAsync().ConfigureAwait(false);
        await _recorder.StopAsync().ConfigureAwait(false);
        await Task.Run(() => _scheduler.Invoke(_recorder.Dispose)).ConfigureAwait(false);
    }

    // The render thread of the first chart that runs.
    private static RenderScheduler SchedulerOf(IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> charts)
    {
        foreach (var (controller, _) in charts)
        {
            if (controller() is { } running)
            {
                return running.Scheduler;
            }
        }

        throw new InvalidOperationException("There is no chart to record.");
    }

    // Asked at each frame: a chart that starts during the recording, as a 3D chart that is still loading, comes in as
    // soon as it runs. Charts on another render thread stay out.
    private static RecordedChart[] Collect(IReadOnlyList<(Func<ChartController?> Controller, PixelRect Bounds)> charts, RenderScheduler scheduler)
    {
        var found = new List<RecordedChart>(charts.Count);
        foreach (var (controller, bounds) in charts)
        {
            if (controller() is { Surface: { } surface } running && ReferenceEquals(running.Scheduler, scheduler))
            {
                found.Add(new RecordedChart(surface, bounds));
            }
        }

        return [.. found];
    }
}
