using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// The channels of a logic analyzer at one nanosecond per point: a clock, chip select, write, eight data lines that
/// change in bursts, interrupts and an error of a single nanosecond, and slow lines. Each channel keeps only its
/// changes, so a billion points per channel take a few megabytes. The same formula as the sample of the configurator.
/// </summary>
public static class LogicBus
{
    /// <summary>The names of the channels, from top to bottom.</summary>
    public static IReadOnlyList<string> Names { get; } = ["CLK", "CS", "WR", "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "IRQ", "ERR", "PWR", "LED", "SYNC"];

    /// <summary>Gets the color of a channel: the clock yellow, control orange, data green, faults red, the rest blue.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The color.</returns>
    public static Rgba8 ColorOf(int channel) => channel switch
    {
        0 => new Rgba8(240, 200, 80, 255),
        1 or 2 => new Rgba8(240, 150, 70, 255),
        >= 3 and <= 10 => new Rgba8(90, 200, 120, 255),
        11 or 12 => new Rgba8(240, 90, 90, 255),
        _ => new Rgba8(90, 160, 240, 255),
    };

    /// <summary>Makes the channels.</summary>
    /// <param name="count">The points per channel.</param>
    /// <returns>The channels, in the order of <see cref="Names"/>.</returns>
    public static DigitalTransitions[] Create(long count)
    {
        var channels = new DigitalTransitions[16];
        var levels = new bool[16];
        var since = new long[16];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = new DigitalTransitions(0, 1e-9);
        }

        // Chip select and write rest high.
        (levels[1], levels[2]) = (true, true);
        void Set(int channel, long at, bool level)
        {
            if (level != levels[channel] && at > since[channel] && at < count)
            {
                channels[channel].Append(levels[channel], at - since[channel]);
                (since[channel], levels[channel]) = (at, level);
            }
        }

        var half = Math.Max(1, count / 200_000);
        var period = Math.Max(1, count / 50);
        var burst = Math.Max(1, count / 500);
        var random = new Random(42); // NOSONAR: makes sample data, no security
        for (var at = half; at < count; at += half)
        {
            var rising = (at / half) % 2 == 1;
            Set(0, at, rising);
            var inBurst = (at + (period / 2)) % period < burst;
            Set(1, at, !inBurst);
            if (inBurst && rising)
            {
                if ((at / half / 2) % 8 == 0)
                {
                    Set(2, at, random.Next(2) == 0);
                }

                var value = random.Next(256);
                for (var bit = 0; bit < 8; bit++)
                {
                    Set(3 + bit, at, ((value >> bit) & 1) != 0);
                }
            }
        }

        // Interrupts and an error of one point each: an edge a chart must never lose.
        foreach (var share in (ReadOnlySpan<double>)[0.13, 0.29, 0.41, 0.58, 0.66, 0.79, 0.93])
        {
            var at = (long)(count * share);
            Set(11, at, true);
            Set(11, at + 1, false);
        }

        Set(12, (long)(count * 0.871), true);
        Set(12, (long)(count * 0.871) + 1, false);
        Set(13, count / 100, true);
        for (var at = count / 10; at < count; at += Math.Max(1, count / 10))
        {
            Set(14, at, !levels[14]);
        }

        for (var at = count / 1000; at < count; at += Math.Max(1, count / 1000))
        {
            Set(15, at, true);
            Set(15, at + 10, false);
        }

        for (var i = 0; i < channels.Length; i++)
        {
            channels[i].Append(levels[i], count - since[i]);
        }

        return channels;
    }
}
