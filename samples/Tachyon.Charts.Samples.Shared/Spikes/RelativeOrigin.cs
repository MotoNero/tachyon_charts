namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>
/// Maps absolute timestamps to floats relative to a nearby origin. The subtraction happens
/// in integers, so that no precision is lost before the conversion to float.
/// </summary>
/// <param name="OriginTicks">The origin, close to the visible range.</param>
/// <param name="TicksPerPixel">The horizontal scale.</param>
public readonly record struct RelativeOrigin(long OriginTicks, double TicksPerPixel)
{
    /// <summary>
    /// Converts a timestamp to a pixel position relative to the origin.
    /// </summary>
    public float ToPixel(long ticks) => (float)((ticks - OriginTicks) / TicksPerPixel);
}
