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
    private int _isCapturing;

    private AreaRecording(ChartRecorder recorder, RenderScheduler scheduler, IReadOnlyList<(ChartController Controller, PixelRect Bounds)> charts, int frameRate)
    {
        (_recorder, _scheduler) = (recorder, scheduler);
        _timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _isCapturing, 1) == 0)
            {
                _scheduler.Post(() =>
                {
                    try
                    {
                        _recorder.Capture(Collect(charts), _scheduler.Device);
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
    /// <returns>The recording.</returns>
    public static AreaRecording Start(IReadOnlyList<(ChartController Controller, PixelRect Bounds)> charts, PixelSize size, string path, VideoOptions? options)
    {
        var scheduler = SchedulerOf(charts);
        var recorder = ChartRecorder.Start(path, size, options ?? new VideoOptions());
        return new AreaRecording(recorder, scheduler, charts, (options ?? new VideoOptions()).FrameRate);
    }

    /// <summary>Writes a planned video of the charts, frame by frame at fixed times.</summary>
    /// <param name="charts">The charts and their places in pixels of the area.</param>
    /// <param name="size">The size of the area in pixels.</param>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The length and encoding, or <see langword="null"/>; the size comes from the area.</param>
    /// <param name="step">Called before each frame.</param>
    /// <returns>When the file is written.</returns>
    public static Task ExportVideoAsync(IReadOnlyList<(ChartController Controller, PixelRect Bounds)> charts, PixelSize size, string path, VideoOptions? options, Action<int, TimeSpan>? step)
    {
        var scheduler = SchedulerOf(charts);
        return Task.Run(() => scheduler.Invoke(() => ChartVideo.Write(Collect(charts), scheduler.Device, path, size, options ?? new VideoOptions(), step)));
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        await _timer.DisposeAsync().ConfigureAwait(false);
        await _recorder.StopAsync().ConfigureAwait(false);
        await Task.Run(() => _scheduler.Invoke(_recorder.Dispose)).ConfigureAwait(false);
    }

    // The charts that run on the render thread of the first one; a chart that is not shown yet is left out.
    private static RenderScheduler SchedulerOf(IReadOnlyList<(ChartController Controller, PixelRect Bounds)> charts) =>
        charts.Count > 0 ? charts[0].Controller.Scheduler : throw new InvalidOperationException("There is no chart to record.");

    private static RecordedChart[] Collect(IReadOnlyList<(ChartController Controller, PixelRect Bounds)> charts)
    {
        var found = new List<RecordedChart>(charts.Count);
        foreach (var (controller, bounds) in charts)
        {
            if (controller.Surface is { } surface && ReferenceEquals(controller.Scheduler, charts[0].Controller.Scheduler))
            {
                found.Add(new RecordedChart(surface, bounds));
            }
        }

        return [.. found];
    }
}
