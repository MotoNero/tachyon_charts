using Tachyon.Charts.Core.Instruments;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Signals and a display for the oscilloscope examples.</summary>
public static class ScopeData
{
    /// <summary>Creates the display of this scene.</summary>
    /// <returns>The display with four channels.</returns>
    public static ScopeDisplay CreateDisplay()
    {
        var display = new ScopeDisplay { TimePerDivision = 5e-4, TriggerLevel = 0.5 };
        for (var i = 0; i < 4; i++)
        {
            var channel = display.AddChannel();
            channel.VoltsPerDivision = i == 0 ? 1 : 0.5;
            channel.Position = 3 - (i * 2);
            Fill(channel, i);
        }

        return display;
    }

    /// <summary>Calculates a test signal.</summary>
    /// <param name="kind">The signal: 0 sine, 1 square, 2 ringing pulse, 3 triangle.</param>
    /// <param name="time">The time in seconds.</param>
    /// <param name="phase">The shift in radians.</param>
    /// <returns>The value in volts.</returns>
    public static double Signal(int kind, double time, double phase)
    {
        var angle = (2 * Math.PI * 1000 * time) + phase;
        return kind switch
        {
            0 => 1.5 * Math.Sin(angle),
            1 => Math.Sin(angle) >= 0 ? 1 : -1,
            2 => 2 * Math.Exp(-Math.Abs(((time + 0.01) * 1000 % 1) * 6)) * Math.Cos(angle * 12),
            _ => 2 / Math.PI * Math.Asin(Math.Sin(angle)),
        };
    }

    /// <summary>Fills a channel with a test signal.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="kind">The signal: 0 sine, 1 square, 2 ringing pulse, 3 triangle.</param>
    /// <param name="phase">The shift of the signal in radians.</param>
    public static void Fill(ScopeChannel channel, int kind, double phase = 0)
    {
        ArgumentNullException.ThrowIfNull(channel);
        const int Count = 5000;
        const double Duration = 0.01;
        var time = new double[Count];
        var volts = new float[Count];
        for (var i = 0; i < Count; i++)
        {
            var t = (i * Duration / Count) - (Duration / 2);
            time[i] = t;
            volts[i] = (float)Signal(kind, t, phase);
        }

        channel.SetWaveform(time, volts);
    }
}
