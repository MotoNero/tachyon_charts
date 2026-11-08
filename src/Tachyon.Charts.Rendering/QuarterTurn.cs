using System.Numerics;

namespace Tachyon.Charts.Rendering;

/// <summary>The quarter turn clockwise of <see cref="ICommandList.SetQuarterTurn"/>: (x, y) lands at (sum − y, x).</summary>
internal static class QuarterTurn
{
    /// <summary>Turns a point.</summary>
    /// <param name="sum">The sum that places the turned picture.</param>
    /// <param name="point">The point before the turn.</param>
    /// <returns>The point after the turn.</returns>
    public static Vector2 Apply(float sum, Vector2 point) => new(sum - point.Y, point.X);

    /// <summary>Turns a rectangle, which stays a rectangle.</summary>
    /// <param name="sum">The sum that places the turned picture.</param>
    /// <param name="bounds">The rectangle before the turn.</param>
    /// <returns>The rectangle after the turn.</returns>
    public static PixelRect Apply(float sum, PixelRect bounds)
    {
        var (turnedWidth, turnedHeight) = (bounds.Height, bounds.Width);
        return new(sum - (bounds.Y + bounds.Height), bounds.X, turnedWidth, turnedHeight);
    }

    /// <summary>Turns a point back.</summary>
    /// <param name="sum">The sum that places the turned picture.</param>
    /// <param name="point">The point after the turn.</param>
    /// <returns>The point before the turn.</returns>
    public static Vector2 Undo(float sum, Vector2 point) => new(point.Y, sum - point.X);
}
