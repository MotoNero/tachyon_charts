using System.Numerics;

#if AVALONIA
namespace Tachyon.Charts.Avalonia;
#else
namespace Tachyon.Charts.Wpf;
#endif

/// <summary>
/// Counts quick clicks. Native windows do not tell the framework enough: window messages only know the second click, and a chart behind a child window loses the count of Avalonia.
/// </summary>
/// <param name="maximumInterval">The longest time between two clicks.</param>
/// <param name="maximumDistance">The largest distance between two clicks in pixels.</param>
internal sealed class ClickCounter(TimeSpan maximumInterval, float maximumDistance)
{
    private TimeSpan _lastTime;
    private Vector2 _lastPosition;
    private int _count;

    /// <summary>Counts a click.</summary>
    /// <param name="position">Where it happened.</param>
    /// <param name="time">When it happened.</param>
    /// <returns>The number of clicks in a row.</returns>
    public int Click(Vector2 position, TimeSpan time)
    {
        var isSequence = _count > 0
            && time - _lastTime <= maximumInterval
            && Vector2.Distance(position, _lastPosition) <= maximumDistance;
        _count = isSequence ? _count + 1 : 1;
        _lastTime = time;
        _lastPosition = position;
        return _count;
    }
}
