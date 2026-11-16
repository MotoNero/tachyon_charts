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
        var bus = new BusWriter(count);
        WriteTransfers(bus, count);
        WriteEvents(bus, count);
        return bus.Finish();
    }

    // The clock on its own; chip select low, write pulses and a byte on the data lines in bursts.
    private static void WriteTransfers(BusWriter bus, long count)
    {
        var half = Math.Max(1, count / 200_000);
        var period = Math.Max(1, count / 50);
        var burst = Math.Max(1, count / 500);
        var random = new Random(42); // NOSONAR: makes sample data, no security
        for (var at = half; at < count; at += half)
        {
            var rising = (at / half) % 2 == 1;
            bus.Set(0, at, rising);
            var inBurst = (at + (period / 2)) % period < burst;
            bus.Set(1, at, !inBurst);
            if (inBurst && rising)
            {
                WriteByte(bus, at, half, random);
            }
        }
    }

    private static void WriteByte(BusWriter bus, long at, long half, Random random)
    {
        if ((at / half / 2) % 8 == 0)
        {
            bus.Set(2, at, random.Next(2) == 0);
        }

        var value = random.Next(256);
        for (var bit = 0; bit < 8; bit++)
        {
            bus.Set(3 + bit, at, ((value >> bit) & 1) != 0);
        }
    }

    // Interrupts and an error of one point each: an edge a chart must never lose.
    private static void WriteEvents(BusWriter bus, long count)
    {
        foreach (var share in (ReadOnlySpan<double>)[0.13, 0.29, 0.41, 0.58, 0.66, 0.79, 0.93])
        {
            var at = (long)(count * share);
            bus.Set(11, at, true);
            bus.Set(11, at + 1, false);
        }

        bus.Set(12, (long)(count * 0.871), true);
        bus.Set(12, (long)(count * 0.871) + 1, false);
        bus.Set(13, count / 100, true);
        for (var at = count / 10; at < count; at += Math.Max(1, count / 10))
        {
            bus.Set(14, at, !bus.Level(14));
        }

        for (var at = count / 1000; at < count; at += Math.Max(1, count / 1000))
        {
            bus.Set(15, at, true);
            bus.Set(15, at + 10, false);
        }
    }

    // The sixteen channels as they are written: each change closes the run of the level before. Chip select and write
    // rest high.
    private sealed class BusWriter
    {
        private readonly long _count;
        private readonly DigitalTransitions[] _channels = new DigitalTransitions[16];
        private readonly bool[] _levels = new bool[16];
        private readonly long[] _since = new long[16];

        public BusWriter(long count)
        {
            _count = count;
            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new DigitalTransitions(0, 1e-9);
            }

            (_levels[1], _levels[2]) = (true, true);
        }

        public bool Level(int channel) => _levels[channel];

        public void Set(int channel, long at, bool level)
        {
            if (level != _levels[channel] && at > _since[channel] && at < _count)
            {
                _channels[channel].Append(_levels[channel], at - _since[channel]);
                (_since[channel], _levels[channel]) = (at, level);
            }
        }

        public DigitalTransitions[] Finish()
        {
            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i].Append(_levels[i], _count - _since[i]);
            }

            return _channels;
        }
    }
}
