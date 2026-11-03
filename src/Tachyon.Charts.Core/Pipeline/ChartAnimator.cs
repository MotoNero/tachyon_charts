using System.Diagnostics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>The course of an animation.</summary>
public enum Easing
{
    /// <summary>At the same speed all the time.</summary>
    Linear,

    /// <summary>Fast at first, slowing down at the end.</summary>
    CubicOut,

    /// <summary>Slow at both ends.</summary>
    CubicInOut,
}

/// <summary>Tells the time of animations. Tests use a virtual clock.</summary>
public interface IAnimationClock
{
    /// <summary>Gets the time since an arbitrary start.</summary>
    TimeSpan Now { get; }
}

/// <summary>
/// Time-based animations of a chart. The time decides the progress, so a slow frame does not slow an
/// animation down. A new animation of the same target replaces the running one and starts from where
/// that one is. All members run on the render thread.
/// </summary>
/// <param name="clock">The clock, or <see langword="null"/> for the system clock.</param>
public sealed class ChartAnimator(IAnimationClock? clock = null)
{
    private readonly IAnimationClock _clock = clock ?? new SystemClock();
    private readonly List<Running> _animations = [];

    /// <summary>Gets a value indicating whether an animation runs. Safe from any thread.</summary>
    public bool IsActive => Volatile.Read(ref _count) > 0;

    private int _count;

    /// <summary>Applies an easing.</summary>
    /// <param name="easing">The course.</param>
    /// <param name="progress">The progress in time, from 0 to 1.</param>
    /// <returns>The eased progress.</returns>
    public static double Apply(Easing easing, double progress) => easing switch
    {
        Easing.CubicOut => 1 - Math.Pow(1 - progress, 3),
        Easing.CubicInOut => progress < 0.5 ? 4 * progress * progress * progress : 1 - (Math.Pow(2 - (2 * progress), 3) / 2),
        _ => progress,
    };

    /// <summary>Starts an animation, replacing a running one of the same target.</summary>
    /// <param name="target">What is animated.</param>
    /// <param name="duration">The duration. Without one, the end applies at once.</param>
    /// <param name="easing">The course.</param>
    /// <param name="apply">Receives the eased progress from 0 to 1, and exactly 1 at the end.</param>
    public void Animate(object target, TimeSpan duration, Easing easing, Action<double> apply)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(apply);
        _animations.RemoveAll(animation => ReferenceEquals(animation.Target, target));
        if (duration <= TimeSpan.Zero)
        {
            apply(1);
        }
        else
        {
            _animations.Add(new Running(target, _clock.Now, duration, easing, apply));
        }

        Volatile.Write(ref _count, _animations.Count);
    }

    /// <summary>Moves an axis to a range. The range between follows the scale of the axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="target">The range to reach.</param>
    /// <param name="duration">The duration.</param>
    /// <param name="easing">The course.</param>
    public void AnimateRange(AxisBase axis, DoubleRange target, TimeSpan duration, Easing easing = Easing.CubicOut)
    {
        ArgumentNullException.ThrowIfNull(axis);
        var start = axis.VisibleRange;
        var scaling = axis.Scaling;
        var startMin = scaling?.Forward(start.Min) ?? start.Min;
        var startMax = scaling?.Forward(start.Max) ?? start.Max;
        var endMin = scaling?.Forward(target.Min) ?? target.Min;
        var endMax = scaling?.Forward(target.Max) ?? target.Max;
        Animate(axis, duration, easing, progress =>
        {
            var min = startMin + ((endMin - startMin) * progress);
            var max = startMax + ((endMax - startMax) * progress);
            axis.ShowRange(scaling?.Inverse(min) ?? min, scaling?.Inverse(max) ?? max);
        });
    }

    /// <summary>Moves all running animations to the current time.</summary>
    public void Tick()
    {
        if (_animations.Count == 0)
        {
            return;
        }

        var now = _clock.Now;
        for (var i = _animations.Count - 1; i >= 0; i--)
        {
            var animation = _animations[i];
            var progress = Math.Clamp((now - animation.Start) / animation.Duration, 0, 1);
            animation.Apply(progress >= 1 ? 1 : Apply(animation.Easing, progress));
            if (progress >= 1)
            {
                _animations.RemoveAt(i);
            }
        }

        Volatile.Write(ref _count, _animations.Count);
    }

    private sealed record Running(object Target, TimeSpan Start, TimeSpan Duration, Easing Easing, Action<double> Apply); // NOSONAR: a private record

    private sealed class SystemClock : IAnimationClock
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public TimeSpan Now => Stopwatch.GetElapsedTime(_start);
    }
}
