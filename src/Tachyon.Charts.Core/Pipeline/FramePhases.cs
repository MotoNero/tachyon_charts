namespace Tachyon.Charts.Core.Pipeline;

/// <summary>The time of the steps of a frame in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
/// <param name="Arrange">Snapshots, axes and layout.</param>
/// <param name="Prepare">Preparing the series and text.</param>
/// <param name="Draw">Recording the commands.</param>
/// <param name="Submit">Handing the frame to the device, on a screen including the presentation.</param>
public readonly record struct FramePhases(long Arrange, long Prepare, long Draw, long Submit)
{
    /// <summary>Adds two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The sum.</returns>
    public static FramePhases operator +(FramePhases left, FramePhases right) =>
        new(left.Arrange + right.Arrange, left.Prepare + right.Prepare, left.Draw + right.Draw, left.Submit + right.Submit);

    /// <summary>Subtracts a value.</summary>
    /// <param name="left">The value.</param>
    /// <param name="right">The value to subtract.</param>
    /// <returns>The difference.</returns>
    public static FramePhases operator -(FramePhases left, FramePhases right) =>
        new(left.Arrange - right.Arrange, left.Prepare - right.Prepare, left.Draw - right.Draw, left.Submit - right.Submit);
}
