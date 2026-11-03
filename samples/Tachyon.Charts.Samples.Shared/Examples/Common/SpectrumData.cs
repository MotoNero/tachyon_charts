using Tachyon.Charts.Core.Instruments;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A display and traces for the spectrum examples.</summary>
public static class SpectrumData
{
    /// <summary>Creates the display of this scene.</summary>
    /// <param name="passing">Whether the signal stays below the mask. The test scene violates it.</param>
    /// <returns>The display.</returns>
    public static SpectrumDisplay CreateDisplay(bool passing = false)
    {
        var display = new SpectrumDisplay { Mask = new FrequencyMask([0, 3e6, 3.2e6, 1e7], passing ? [-5, -5, -9, -9] : [-5, -5, -25, -25]), ShowsDensity = true };
        display.SetView(0, 1e7, 0);
        return display;
    }

    /// <summary>Delivers one spectrum.</summary>
    /// <param name="display">The display.</param>
    /// <param name="frequencies">The frequencies.</param>
    /// <param name="levels">The levels, overwritten.</param>
    /// <param name="random">The noise.</param>
    public static void Deliver(SpectrumDisplay display, double[] frequencies, float[] levels, Random random)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(random);
        for (var i = 0; i < levels.Length; i++)
        {
            var level = -70 + (6 * random.NextDouble());
            foreach (var (center, height) in new[] { (2e6, -12.0), (4.5e6, -30.0), (7e6, -22.0) })
            {
                var distance = (frequencies[i] - center) / 8e4;
                level = Math.Max(level, height - (distance * distance * 6) + random.NextDouble());
            }

            levels[i] = (float)level;
        }

        display.SetTrace(frequencies, levels);
    }
}
