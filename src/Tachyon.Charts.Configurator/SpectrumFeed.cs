using Tachyon.Charts.Core.Instruments;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Spectra as a measurement would bring them: a carrier that drifts up in frequency beside a steady one. Kept twice, as
/// C# for the preview and as text for the code, side by side.
/// </summary>
internal static class SpectrumFeed
{
    /// <summary>Adds the spectrum of a step to a display.</summary>
    /// <param name="display">The display.</param>
    /// <param name="step">The step, which moves the drifting carrier.</param>
    /// <param name="frequencies">The frequencies, filled here.</param>
    /// <param name="levels">The levels, filled here.</param>
    public static void Drift(SpectrumDisplay display, int step, double[] frequencies, float[] levels)
    {
        var center = 1.5e6 + (1.5e4 * (step % 400));
        for (var i = 0; i < frequencies.Length; i++)
        {
            frequencies[i] = i * 1e7 / (frequencies.Length - 1);
            var drifting = Math.Exp(-Math.Pow((frequencies[i] - center) / 2e5, 2));
            var steady = 0.5 * Math.Exp(-Math.Pow((frequencies[i] - 7e6) / 1.5e5, 2));
            levels[i] = (float)(-85 + (65 * Math.Max(drifting, steady)) + (1.5 * Math.Sin((i * 0.37) + (step * 0.5))));
        }

        display.SetTrace(frequencies, levels);
    }

    /// <summary>Gets <see cref="Drift"/> as C#.</summary>
    public const string DriftCode = """

            // Adds the spectrum of a step: a carrier that drifts up in frequency beside a steady one.
            private static void Drift(SpectrumDisplay display, int step, double[] frequencies, float[] levels)
            {
                var center = 1.5e6 + (1.5e4 * (step % 400));
                for (var i = 0; i < frequencies.Length; i++)
                {
                    frequencies[i] = i * 1e7 / (frequencies.Length - 1);
                    var drifting = Math.Exp(-Math.Pow((frequencies[i] - center) / 2e5, 2));
                    var steady = 0.5 * Math.Exp(-Math.Pow((frequencies[i] - 7e6) / 1.5e5, 2));
                    levels[i] = (float)(-85 + (65 * Math.Max(drifting, steady)) + (1.5 * Math.Sin((i * 0.37) + (step * 0.5))));
                }

                display.SetTrace(frequencies, levels);
            }

        """;

    /// <summary>Starts new spectra at a rate, after the ones a display has: a timer, which disposing stops.</summary>
    /// <param name="display">The display.</param>
    /// <param name="rate">The spectra per second.</param>
    /// <returns>The timer.</returns>
    public static IDisposable Start(SpectrumDisplay display, double rate)
    {
        var (frequencies, levels) = (new double[1024], new float[1024]);
        var step = 300;
        return new Timer(
            _ =>
            {
                lock (frequencies)
                {
                    Drift(display, step++, frequencies, levels);
                }
            },
            null,
            0,
            Math.Max(1, (int)(1000 / rate)));
    }

    /// <summary>Gets <see cref="Start"/> as C#, with a field that keeps the timer alive.</summary>
    public const string StartCode = """

            // New spectra keep coming at a rate, as from a measurement; the field keeps the timer alive.
            private static Timer? _feed;

            private static void StartFeed(SpectrumDisplay display, double rate)
            {
                var (frequencies, levels) = (new double[1024], new float[1024]);
                var step = 300;
                _feed = new Timer(
                    _ =>
                    {
                        lock (frequencies)
                        {
                            NextSpectrum(display, step++, frequencies, levels);
                        }
                    },
                    null,
                    0,
                    Math.Max(1, (int)(1000 / rate)));
            }

            // The spectrum of a step: a carrier that drifts up in frequency beside a steady one.
            private static void NextSpectrum(SpectrumDisplay display, int step, double[] frequencies, float[] levels)
            {
                var center = 1.5e6 + (1.5e4 * (step % 400));
                for (var i = 0; i < frequencies.Length; i++)
                {
                    frequencies[i] = i * 1e7 / (frequencies.Length - 1);
                    var drifting = Math.Exp(-Math.Pow((frequencies[i] - center) / 2e5, 2));
                    var steady = 0.5 * Math.Exp(-Math.Pow((frequencies[i] - 7e6) / 1.5e5, 2));
                    levels[i] = (float)(-85 + (65 * Math.Max(drifting, steady)) + (1.5 * Math.Sin((i * 0.37) + (step * 0.5))));
                }

                display.SetTrace(frequencies, levels);
            }

        """;
}
