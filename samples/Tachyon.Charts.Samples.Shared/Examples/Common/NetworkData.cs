using System.Numerics;
using Tachyon.Charts.Core.Instruments;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A measured filter for the network analyzer.</summary>
public static class NetworkData
{
    /// <summary>Creates the measured filter.</summary>
    /// <param name="passing">Whether the limits fit the filter. The test scene has limits that fail.</param>
    /// <returns>The display.</returns>
    public static NetworkDisplay CreateDisplay(bool passing)
    {
        var display = new NetworkDisplay();
        var transmission = display.AddTrace("S21");
        var reflection = display.AddTrace("S11");
        const int Count = 801;
        var frequencies = new double[Count];
        var through = new Complex[Count];
        var back = new Complex[Count];
        for (var i = 0; i < Count; i++)
        {
            frequencies[i] = 1e9 + (i * 1e9 / (Count - 1));
            var detuning = (frequencies[i] - 1.5e9) / 1.2e8;
            var response = 1 / (1 + Math.Pow(detuning, 6));
            through[i] = Math.Max(Math.Sqrt(response), 1e-4);
            back[i] = Math.Max(Math.Sqrt(1 - response), 0.03);
        }

        transmission.SetSweep(frequencies, through);
        reflection.SetSweep(frequencies, back);
        transmission.AddLimit(new LimitLine(LimitKind.Minimum, 1.45e9, -1, 1.55e9, -1));
        transmission.AddLimit(passing ? new LimitLine(LimitKind.Maximum, 1e9, -20, 1.15e9, -20) : new LimitLine(LimitKind.Maximum, 1e9, -40, 1.25e9, -40));
        reflection.AddLimit(passing ? new LimitLine(LimitKind.Maximum, 1.47e9, -20, 1.53e9, -20) : new LimitLine(LimitKind.Maximum, 1.4e9, -20, 1.6e9, -20));
        display.AddMarker(1.5e9);
        display.AddMarker(1.7e9);
        return display;
    }
}
