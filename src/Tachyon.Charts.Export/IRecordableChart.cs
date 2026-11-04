namespace Tachyon.Charts.Export;

/// <summary>A chart control that records videos, in WPF as in Avalonia, so that code for videos works with both.</summary>
public interface IRecordableChart
{
    /// <summary>Gets a value indicating whether a recording runs.</summary>
    bool IsRecording { get; }

    /// <summary>Gets how long the running recording takes so far, or zero.</summary>
    TimeSpan RecordingTime { get; }

    /// <summary>Records a planned video, frame by frame at fixed times.</summary>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The size, length and encoding, or <see langword="null"/> for the defaults.</param>
    /// <param name="step">Called before each frame with its number and time, to move a camera or to add data.</param>
    /// <returns>When the file is written.</returns>
    Task ExportVideoAsync(string path, VideoOptions? options = null, Action<int, TimeSpan>? step = null);

    /// <summary>Starts to record what the chart shows while the user works with it.</summary>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The size, frame rate and encoding, or <see langword="null"/> for the size of the chart and 30 frames per second.</param>
    void StartRecording(string path, VideoOptions? options = null);

    /// <summary>Stops the recording and completes the file.</summary>
    /// <returns>When the file is written.</returns>
    Task StopRecordingAsync();
}
