namespace Tachyon.Charts.Export;

/// <summary>A recording that runs, of one chart or of all charts in an area of a window.</summary>
public interface IChartRecording
{
    /// <summary>Gets how long the recording runs.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>Ends the recording and completes the file.</summary>
    /// <returns>When the file is written.</returns>
    Task StopAsync();
}
