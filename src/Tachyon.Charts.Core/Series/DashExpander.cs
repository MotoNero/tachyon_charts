using System.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// Cuts a line in pixels into its dashes, separated by gap points. Done on the CPU, so that all backends
/// draw the same dashes with their plain line pipeline.
/// </summary>
internal static class DashExpander
{
    private const float Margin = 16;

    /// <summary>
    /// Writes the dashes of a line.
    /// </summary>
    /// <param name="line">The line, possibly with gap points. Each gap restarts the pattern.</param>
    /// <param name="pattern">The lengths of dashes and spaces in turn, in device-independent units.</param>
    /// <param name="scale">Pixels per device-independent unit.</param>
    /// <param name="bounds">The visible area. Outside of it no dashes are made, a far point cannot cost millions.</param>
    /// <param name="output">Receives the points and grows if necessary.</param>
    /// <returns>The number of points written.</returns>
    public static int Expand(ReadOnlySpan<Vector2> line, IReadOnlyList<float> pattern, float scale, PixelRect bounds, ref Vector2[] output)
    {
        var count = 0;
        var index = 0;
        var remaining = pattern[0] * scale;
        var isDash = true;
        var isOpen = false;

        for (var i = 1; i < line.Length; i++)
        {
            var from = line[i - 1];
            var to = line[i];
            if (from.X >= PolylineStyle.Gap || to.X >= PolylineStyle.Gap)
            {
                CloseDash(ref output, ref count, ref isOpen);
                index = 0;
                remaining = pattern[0] * scale;
                isDash = true;
                continue;
            }

            if (!TryClip(ref from, ref to, bounds))
            {
                CloseDash(ref output, ref count, ref isOpen);
                continue;
            }

            Walk(from, to, pattern, scale, ref output, ref count, ref index, ref remaining, ref isDash, ref isOpen);
        }

        return count;
    }

    private static void Walk(Vector2 from, Vector2 to, IReadOnlyList<float> pattern, float scale, ref Vector2[] output, ref int count, ref int index, ref float remaining, ref bool isDash, ref bool isOpen)
    {
        var length = Vector2.Distance(from, to);
        var position = 0f;
        while (length - position > 0)
        {
            var step = Math.Min(remaining, length - position);
            var start = Vector2.Lerp(from, to, position / length);
            var end = Vector2.Lerp(from, to, (position + step) / length);
            if (isDash)
            {
                if (!isOpen)
                {
                    Add(ref output, ref count, start);
                    isOpen = true;
                }

                Add(ref output, ref count, end);
            }

            position += step;
            remaining -= step;
            if (remaining <= 0)
            {
                index = (index + 1) % pattern.Count;
                remaining = pattern[index] * scale;
                if (isDash)
                {
                    CloseDash(ref output, ref count, ref isOpen);
                }

                isDash = !isDash;
            }
        }
    }

    private static void CloseDash(ref Vector2[] output, ref int count, ref bool isOpen)
    {
        if (isOpen)
        {
            Add(ref output, ref count, new Vector2(PolylineStyle.Gap, 0));
            isOpen = false;
        }
    }

    private static void Add(ref Vector2[] output, ref int count, Vector2 point)
    {
        if (count == output.Length)
        {
            Array.Resize(ref output, Math.Max(count * 2, 256));
        }

        output[count++] = point;
    }

    // Liang-Barsky against the bounds with a margin for caps.
    private static bool TryClip(ref Vector2 from, ref Vector2 to, PixelRect bounds)
    {
        var delta = to - from;
        var enter = 0f;
        var leave = 1f;
        return Clip(-delta.X, from.X - (bounds.X - Margin), ref enter, ref leave)
            && Clip(delta.X, bounds.X + bounds.Width + Margin - from.X, ref enter, ref leave)
            && Clip(-delta.Y, from.Y - (bounds.Y - Margin), ref enter, ref leave)
            && Clip(delta.Y, bounds.Y + bounds.Height + Margin - from.Y, ref enter, ref leave)
            && Apply(ref from, ref to, delta, enter, leave);
    }

    private static bool Clip(float direction, float distance, ref float enter, ref float leave)
    {
        if (direction == 0)
        {
            return distance >= 0;
        }

        var position = distance / direction;
        if (direction < 0)
        {
            enter = Math.Max(enter, position);
        }
        else
        {
            leave = Math.Min(leave, position);
        }

        return enter <= leave;
    }

    private static bool Apply(ref Vector2 from, ref Vector2 to, Vector2 delta, float enter, float leave)
    {
        var start = from;
        from = start + (delta * enter);
        to = start + (delta * leave);
        return true;
    }
}
