using Tachyon.Charts.Export;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>
/// Videos of everything a window shows, in two ways: a free recording of whatever the user does, started and stopped
/// like a video recorder, and a planned turn of the camera. Both buttons of the showroom run this code on every page.
/// The area is the panel that holds the charts; WPF and Avalonia call it the same way:
/// <code>
/// var recording = ChartAreaRecorder.Start(panel, "session.mp4");   // all charts in the panel, as shown
/// // … the user zooms, pans, turns, drags planes and sets markers …
/// await recording.StopAsync();
///
/// await ChartAreaRecorder.ExportVideoAsync(panel, "turn.mp4", new VideoOptions { Duration = TimeSpan.FromSeconds(6) }, step);
/// </code>
/// </summary>
public static class VideoRecordingPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Video recording";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Red button: start a recording, operate the chart, press again to stop | Video: a turn of six seconds | Drag: turn | Wheel: zoom";

    /// <summary>Builds the chart: a lit hill to turn and zoom while recording.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Hill(c, new Material3D());

    /// <summary>Starts to record all charts of an area, in its size at 30 frames per second.</summary>
    /// <param name="recordArea">Starts the recording of the area into a file: <c>path => ChartAreaRecorder.Start(panel, path)</c>.</param>
    /// <param name="path">Receives the file the video goes into.</param>
    /// <returns>The recording.</returns>
    public static IChartRecording StartRecording(Func<string, IChartRecording> recordArea, out string path)
    {
        ArgumentNullException.ThrowIfNull(recordArea);
        path = NewFile();
        return recordArea(path);
    }

    /// <summary>Stops the recording and completes the file.</summary>
    /// <param name="recording">The recording.</param>
    /// <returns>When the file is written.</returns>
    public static Task StopRecordingAsync(IChartRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return recording.StopAsync();
    }

    /// <summary>
    /// Records six seconds of an area at fixed times. A 3D chart turns once around; without a camera the video shows
    /// what keeps arriving.
    /// </summary>
    /// <param name="exportArea">Writes the planned video of the area: <c>(path, options, step) => ChartAreaRecorder.ExportVideoAsync(panel, path, options, step)</c>.</param>
    /// <param name="camera">The camera of a 3D chart, or <see langword="null"/>.</param>
    /// <returns>The file.</returns>
    public static async Task<string> RecordTurnAsync(Func<string, VideoOptions, Action<int, TimeSpan>?, Task> exportArea, Camera3D? camera)
    {
        ArgumentNullException.ThrowIfNull(exportArea);
        var path = NewFile();
        var options = new VideoOptions { Duration = TimeSpan.FromSeconds(6) };
        var degrees = 360.0 / (options.Duration.TotalSeconds * options.FrameRate);
        Action<int, TimeSpan>? turn = camera is null ? null : (frame, _) =>
        {
            if (frame > 0)
            {
                camera.Orbit(degrees, 0);
            }
        };
        await exportArea(path, options, turn);
        return path;
    }

    // Videos/Tachyon Export/chart-<time>.mp4
    private static string NewFile()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Tachyon Export");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"chart-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
    }
}
