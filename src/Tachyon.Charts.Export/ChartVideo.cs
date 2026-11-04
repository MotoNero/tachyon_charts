using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Export.Video;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export;

/// <summary>
/// Records a chart as an MP4 video: a camera that turns, data that arrives, a range that moves. Each frame belongs to
/// a fixed time, frame number divided by frame rate, so the video plays evenly however long a frame takes to render.
/// </summary>
public static class ChartVideo
{
    /// <summary>The text when FFmpeg is needed but not there.</summary>
    public const string MissingFFmpeg =
        "FFmpeg was not found. Videos need FFmpeg outside of Windows: install it from ffmpeg.org and put it on the PATH, or set VideoOptions.FFmpegPath.";

    /// <summary>Writes the video. The display stays as it is, apart from what <paramref name="step"/> changes.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="device">The device the chart uses.</param>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The size, length and encoding.</param>
    /// <param name="step">Called before each frame with its number and time, to move a camera or to add data.</param>
    /// <exception cref="FileNotFoundException">FFmpeg is needed and was not found.</exception>
    /// <exception cref="PlatformNotSupportedException">Media Foundation was asked for outside of Windows.</exception>
    /// <exception cref="InvalidOperationException">The encoder failed.</exception>
    public static void Write(ChartSurface chart, IRenderDevice device, string path, VideoOptions options, Action<int, TimeSpan>? step = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var size = SizeOf(options);
        using var encoder = Open(path, size, options);
        Write(chart, device, size, options, step, encoder);
    }

    /// <summary>Renders the frames into an encoder.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="device">The device.</param>
    /// <param name="size">The size of a frame.</param>
    /// <param name="options">The length, frame rate and theme.</param>
    /// <param name="step">Called before each frame.</param>
    /// <param name="encoder">Receives the frames.</param>
    internal static void Write(ChartSurface chart, IRenderDevice device, PixelSize size, VideoOptions options, Action<int, TimeSpan>? step, IVideoEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.FrameRate);
        var frames = (int)Math.Ceiling(options.Duration.TotalSeconds * options.FrameRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        var pixels = new Rgba8[size.PixelCount];
        using var target = device.CreateOffscreenTarget(size, 1);
        using (var scope = new ExportScope(chart, new ExportOptions { Theme = options.Theme }))
        {
            for (var frame = 0; frame < frames; frame++)
            {
                step?.Invoke(frame, TimeSpan.FromTicks(frame * TimeSpan.TicksPerSecond / options.FrameRate));
                scope.Render(device, target);
                device.WaitForIdle();
                target.ReadPixels(pixels);
                encoder.Write(pixels);
            }
        }

        encoder.Finish();
    }

    /// <summary>Gets the size of a frame: even, as H.264 needs it.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The size.</returns>
    internal static PixelSize SizeOf(VideoOptions options)
    {
        var (width, height) = (options.Width & ~1, options.Height & ~1);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2, nameof(options));
        return new PixelSize(width, height);
    }

    private static IVideoEncoder Open(string path, PixelSize size, VideoOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var useFoundation = options.Encoder == VideoEncoder.MediaFoundation || (options.Encoder == VideoEncoder.Auto && OperatingSystem.IsWindows());
        if (useFoundation)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Media Foundation exists on Windows only. Use VideoEncoder.FFmpeg.");
            }

            return new MediaFoundationEncoder(Path.GetFullPath(path), size, options.FrameRate, options.BitRate);
        }

        var program = FFmpegEncoder.Find(options.FFmpegPath) ?? throw new FileNotFoundException(MissingFFmpeg, options.FFmpegPath ?? "ffmpeg");
        return new FFmpegEncoder(program, Path.GetFullPath(path), size, options.FrameRate, options.BitRate);
    }
}
