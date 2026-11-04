using System.Collections.Concurrent;
using System.Diagnostics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Export.Video;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export;

/// <summary>
/// Records what a chart shows while the user works with it, like a video recorder: start, operate the chart, stop.
/// Pictures are taken on the render thread and encoded on a thread of their own. The time comes from the clock: when a
/// picture comes late, the last one is shown longer, when pictures come faster than the frame rate, the extra ones are
/// left out. The video therefore plays evenly and lasts as long as the recording.
/// </summary>
public sealed class ChartRecorder : IChartRecording, IDisposable
{
    private const int Buffers = 4;

    private readonly PixelSize _size;
    private readonly int _frameRate;
    private readonly Func<TimeSpan> _clock;
    private readonly BlockingCollection<(Rgba8[] Pixels, long Repeats)> _queue = new(Buffers - 1);
    private readonly ConcurrentBag<Rgba8[]> _free = [];
    private readonly Thread _thread;
    private readonly Lock _gate = new();
    private IVideoEncoder? _encoder;
    private readonly FrameComposer _composer = new();
    private Exception? _failure;
    private long _queued;
    private bool _isStopped;

    private ChartRecorder(PixelSize size, int frameRate, Func<TimeSpan> clock, Func<IVideoEncoder> open)
    {
        (_size, _frameRate, _clock) = (size, frameRate, clock);
        for (var i = 0; i < Buffers; i++)
        {
            _free.Add(new Rgba8[size.PixelCount]);
        }

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Encode(open, ready)) { IsBackground = true, Name = "Video recorder" };
        _thread.Start();
        ready.Wait();
        if (_failure is { } failure)
        {
            _thread.Join();
            throw failure;
        }
    }

    /// <summary>Gets how long the recording runs.</summary>
    public TimeSpan Elapsed => _clock();

    /// <summary>Gets the frames handed to the encoder so far, repeated ones included.</summary>
    public long Frames => Interlocked.Read(ref _queued);

    /// <summary>Gets the size of the video.</summary>
    public PixelSize Size => _size;

    /// <summary>Starts a recording into an MP4 file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="size">The size of the video; odd values are rounded down.</param>
    /// <param name="options">The frame rate, bit rate and encoder. The length comes from the recording.</param>
    /// <returns>The recorder.</returns>
    /// <exception cref="FileNotFoundException">FFmpeg is needed and was not found.</exception>
    /// <exception cref="InvalidOperationException">The encoder failed to start.</exception>
    public static ChartRecorder Start(string path, PixelSize size, VideoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.FrameRate);
        var even = ChartVideo.SizeOf(options with { Width = size.Width, Height = size.Height });
        var clock = Stopwatch.StartNew();
        return new ChartRecorder(even, options.FrameRate, () => clock.Elapsed, () => ChartVideo.Open(path, even, options));
    }

    /// <summary>Takes a picture of a chart that fills the video, if the next frame is due. Call it on the render thread.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="device">The device the chart renders with.</param>
    public void Capture(ChartSurface chart, IRenderDevice device) =>
        Capture([new RecordedChart(chart, new PixelRect(0, 0, _size.Width, _size.Height))], device);

    /// <summary>
    /// Takes a picture of several charts at their places, as a window shows them, if the next frame is due. Call it on
    /// the render thread the charts share.
    /// </summary>
    /// <param name="charts">The charts and their places in the video.</param>
    /// <param name="device">The device the charts render with.</param>
    public void Capture(IReadOnlyList<RecordedChart> charts, IRenderDevice device)
    {
        ArgumentNullException.ThrowIfNull(charts);
        ArgumentNullException.ThrowIfNull(device);
        var repeats = Due() - Interlocked.Read(ref _queued);
        if (repeats <= 0 || _isStopped || !_free.TryTake(out var pixels))
        {
            return;
        }

        // The layout of a chart can depend on the size of the picture, so a second pass settles it.
        _composer.Compose(charts, device, pixels, _size, passes: 2);
        lock (_gate)
        {
            if (_isStopped || !_queue.TryAdd((pixels, repeats)))
            {
                _free.Add(pixels);
                return;
            }

            Interlocked.Add(ref _queued, repeats);
        }
    }

    /// <summary>Ends the recording: the last picture fills the time up to now, then the file is completed.</summary>
    /// <returns>When the file is written.</returns>
    /// <exception cref="InvalidOperationException">The encoder failed.</exception>
    public Task StopAsync()
    {
        lock (_gate)
        {
            if (!_isStopped)
            {
                _isStopped = true;
                var rest = Due() - _queued;
                if (rest > 0)
                {
                    _queue.Add((Array.Empty<Rgba8>(), rest));
                    _queued += rest;
                }

                _queue.CompleteAdding();
            }
        }

        return Task.Run(() =>
        {
            _thread.Join();
            if (_failure is { } failure)
            {
                throw new InvalidOperationException("The video could not be written: " + failure.Message, failure);
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (!_isStopped)
            {
                _isStopped = true;
                _queue.CompleteAdding();
            }
        }

        _thread.Join();
        _composer.Dispose();
        _queue.Dispose();
    }

    /// <summary>Creates a recorder with its own clock and encoder, for tests.</summary>
    internal static ChartRecorder Create(PixelSize size, int frameRate, Func<TimeSpan> clock, IVideoEncoder encoder) =>
        new(size, frameRate, clock, () => encoder);

    // The frames that belong to the time so far: frame n shows the moment n divided by the frame rate.
    private long Due() => (long)Math.Floor(_clock().TotalSeconds * _frameRate) + 1;

    // On the thread of the encoder. An empty picture repeats the last one, at the end of the recording.
    private void Encode(Func<IVideoEncoder> open, ManualResetEventSlim ready)
    {
        try
        {
            _encoder = open();
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or PlatformNotSupportedException or IOException)
        {
            _failure = exception;
            ready.Set();
            return;
        }

        ready.Set();
        Rgba8[]? last = null;
        try
        {
            foreach (var (pixels, repeats) in _queue.GetConsumingEnumerable())
            {
                var picture = pixels.Length > 0 ? pixels : last ?? new Rgba8[_size.PixelCount];
                for (var i = 0; i < repeats; i++)
                {
                    _encoder.Write(picture);
                }

                if (pixels.Length > 0)
                {
                    if (last is not null)
                    {
                        _free.Add(last);
                    }

                    last = pixels;
                }
            }

            _encoder.Finish();
        }
        catch (InvalidOperationException exception)
        {
            _failure = exception;
        }
        finally
        {
            _encoder.Dispose();
        }
    }
}
