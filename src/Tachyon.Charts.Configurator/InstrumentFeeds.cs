using System.Diagnostics;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Instruments;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Live acquisitions of an oscilloscope: the signals of the channels at a random phase each time, as a free-running
/// signal comes in; the trigger holds the picture still. Each formula stands once for the preview and once as code.
/// </summary>
internal static class ScopeFeed
{
    private const int Count = 20_000;
    private const double Interval = 1e-6;

    /// <summary>Calculates a test signal.</summary>
    /// <param name="kind">The signal: 0 sine, 1 square, 2 burst, 3 triangle.</param>
    /// <param name="time">The time in seconds.</param>
    /// <returns>The value in volts.</returns>
    public static double Signal(int kind, double time)
    {
        var angle = 2 * Math.PI * 1000 * time;
        return kind switch
        {
            0 => 1.5 * Math.Sin(angle),
            1 => Math.Sin(angle) >= 0 ? 1 : -1,
            2 => 2 * Math.Exp(-Math.Abs((time + 0.01) * 1000 % 1 * 6)) * Math.Cos(angle * 12),
            _ => 2 / Math.PI * Math.Asin(Math.Sin(angle)),
        };
    }

    /// <summary>Starts the acquisitions.</summary>
    /// <param name="display">The display.</param>
    /// <param name="signals">The signal of each channel.</param>
    /// <param name="noise">The noise in volts, which also lets the amplitude vary from one acquisition to the next.</param>
    /// <param name="rate">The acquisitions per second.</param>
    /// <returns>The timer; disposing it stops the acquisitions.</returns>
    public static IDisposable Start(ScopeDisplay display, int[] signals, double noise, double rate)
    {
        var trigger = new ScopeTrigger(display);
        var random = new Random(1); // NOSONAR: makes sample data, no security
        var channels = new float[signals.Length][];
        for (var c = 0; c < channels.Length; c++)
        {
            channels[c] = new float[Count];
        }

        return new Timer(
            _ =>
            {
                lock (channels)
                {
                    Acquire(trigger, signals, noise, random, channels);
                }
            },
            null,
            0,
            Math.Max(1, (int)(1000 / rate)));
    }

    private static void Acquire(ScopeTrigger trigger, int[] signals, double noise, Random random, float[][] channels)
    {
        var shift = random.NextDouble() / 1000;
        var scale = 1 + (noise * (random.NextDouble() - 0.5));
        for (var c = 0; c < channels.Length; c++)
        {
            for (var i = 0; i < Count; i++)
            {
                channels[c][i] = (float)((Signal(signals[c], (i * Interval) - 0.01 + shift) * scale) + (noise * (random.NextDouble() - 0.5)));
            }
        }

        trigger.Process(Interval, channels);
    }

    /// <summary>The same as code: a field and the methods that start and make the acquisitions.</summary>
    public const string Code = """

            // New acquisitions keep coming, each at a random phase as from a free-running signal; the trigger holds the
            // picture still. The field keeps the timer alive.
            private static Timer? _acquisitions;

            private static void StartAcquisitions(ScopeDisplay display, int[] signals, double noise, double rate)
            {
                var trigger = new ScopeTrigger(display);
                var random = new Random(1);
                var channels = new float[signals.Length][];
                for (var c = 0; c < channels.Length; c++)
                {
                    channels[c] = new float[20_000];
                }

                _acquisitions = new Timer(
                    _ =>
                    {
                        lock (channels)
                        {
                            Acquire(trigger, signals, noise, random, channels);
                        }
                    },
                    null,
                    0,
                    Math.Max(1, (int)(1000 / rate)));
            }

            // One acquisition of 20 ms at 1 MHz; the noise also lets the amplitude vary from one to the next.
            private static void Acquire(ScopeTrigger trigger, int[] signals, double noise, Random random, float[][] channels)
            {
                var shift = random.NextDouble() / 1000;
                var scale = 1 + (noise * (random.NextDouble() - 0.5));
                for (var c = 0; c < channels.Length; c++)
                {
                    for (var i = 0; i < channels[c].Length; i++)
                    {
                        channels[c][i] = (float)((Signal(signals[c], (i * 1e-6) - 0.01 + shift) * scale) + (noise * (random.NextDouble() - 0.5)));
                    }
                }

                trigger.Process(1e-6, channels);
            }

            // The test signals: 0 sine, 1 square, 2 burst, 3 triangle.
            private static double Signal(int kind, double time)
            {
                var angle = 2 * Math.PI * 1000 * time;
                return kind switch
                {
                    0 => 1.5 * Math.Sin(angle),
                    1 => Math.Sin(angle) >= 0 ? 1 : -1,
                    2 => 2 * Math.Exp(-Math.Abs((time + 0.01) * 1000 % 1 * 6)) * Math.Cos(angle * 12),
                    _ => 2 / Math.PI * Math.Asin(Math.Sin(angle)),
                };
            }

        """;
}

/// <summary>Spectra of three carriers in noise, as a measurement delivers them one after the other.</summary>
internal static class CarrierFeed
{
    /// <summary>Delivers one spectrum of the three carriers in noise.</summary>
    /// <param name="display">The display.</param>
    /// <param name="frequencies">The frequencies, from 0 to 10 MHz.</param>
    /// <param name="levels">Receives the levels.</param>
    /// <param name="random">The noise.</param>
    public static void Deliver(SpectrumDisplay display, double[] frequencies, float[] levels, Random random)
    {
        for (var i = 0; i < levels.Length; i++)
        {
            frequencies[i] = i * 1e7 / (frequencies.Length - 1);
            var level = -70 + (6 * random.NextDouble());
            foreach (var (center, height) in (ReadOnlySpan<(double, double)>)[(2e6, -12.0), (4.5e6, -30.0), (7e6, -22.0)])
            {
                var distance = (frequencies[i] - center) / 8e4;
                level = Math.Max(level, height - (distance * distance * 6) + random.NextDouble());
            }

            levels[i] = (float)level;
        }

        display.SetTrace(frequencies, levels);
    }

    /// <summary>The method of <see cref="Deliver"/> as code.</summary>
    public const string DeliverCode = """

            // Delivers one spectrum: three carriers in noise from 0 to 10 MHz.
            private static void Deliver(SpectrumDisplay display, double[] frequencies, float[] levels, Random random)
            {
                for (var i = 0; i < levels.Length; i++)
                {
                    frequencies[i] = i * 1e7 / (frequencies.Length - 1);
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

        """;

    /// <summary>Starts delivering spectra.</summary>
    /// <param name="display">The display.</param>
    /// <param name="count">The number of frequencies.</param>
    /// <param name="rate">The spectra per second.</param>
    /// <returns>The timer; disposing it stops the spectra.</returns>
    public static IDisposable Start(SpectrumDisplay display, int count, double rate)
    {
        var (frequencies, levels) = (new double[count], new float[count]);
        var random = new Random(2); // NOSONAR: makes sample data, no security
        return new Timer(
            _ =>
            {
                lock (frequencies)
                {
                    Deliver(display, frequencies, levels, random);
                }
            },
            null,
            0,
            Math.Max(1, (int)(1000 / rate)));
    }

    /// <summary>The start of the spectra as code, with <see cref="DeliverCode"/>.</summary>
    public const string StartCode = """

            // New spectra keep coming at a rate, as from a measurement; the field keeps the timer alive.
            private static Timer? _spectra;

            private static void StartSpectra(SpectrumDisplay display, int count, double rate)
            {
                var (frequencies, levels) = (new double[count], new float[count]);
                var random = new Random(2);
                _spectra = new Timer(
                    _ =>
                    {
                        lock (frequencies)
                        {
                            NextCarriers(display, frequencies, levels, random);
                        }
                    },
                    null,
                    0,
                    Math.Max(1, (int)(1000 / rate)));
            }

        """ + NextCarriersCode;

    // The same formula under its own name, so that the code of a sample with Deliver can stand beside it.
    private const string NextCarriersCode = """

            // The next spectrum: three carriers in noise from 0 to 10 MHz.
            private static void NextCarriers(SpectrumDisplay display, double[] frequencies, float[] levels, Random random)
            {
                for (var i = 0; i < levels.Length; i++)
                {
                    frequencies[i] = i * 1e7 / (frequencies.Length - 1);
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

        """;
}

/// <summary>Live channels of a monitor: the signals of the samples go on at a rate from the newest sample.</summary>
internal static class SweepFeed
{
    /// <summary>Calculates the values of all channels at a time: four channels are two leads of a heartbeat, a pulse wave and breathing, three a heartbeat, a pulse wave and breathing of the smaller sample, others sines.</summary>
    /// <param name="time">The time in seconds.</param>
    /// <param name="values">Receives one value per channel.</param>
    public static void Values(double time, Span<float> values)
    {
        if (values.Length == 4)
        {
            var beat = time * 72 / 60 % 1;
            var ecg = (0.12 * Bell(beat, 0.18, 0.025)) - (0.15 * Bell(beat, 0.36, 0.008)) + Bell(beat, 0.39, 0.010) - (0.25 * Bell(beat, 0.42, 0.010)) + (0.3 * Bell(beat, 0.62, 0.045));
            values[0] = (float)ecg;
            values[1] = (float)((ecg * 0.8) + (0.05 * Math.Sin(time * 2 * Math.PI * 0.3)));
            var pulse = (beat + 0.85) % 1;
            values[2] = (float)((0.9 * Bell(pulse, 0.25, 0.07)) + (0.3 * Bell(pulse, 0.5, 0.06)));
            values[3] = (float)Math.Sin(time * 2 * Math.PI * 0.25);
        }
        else if (values.Length == 3)
        {
            var beat = (time * 1.2) % 1;
            values[0] = (float)((Math.Exp(-((beat - 0.3) * (beat - 0.3)) / 0.0004) * 1.2) - (0.2 * Math.Exp(-((beat - 0.35) * (beat - 0.35)) / 0.001)));
            values[1] = (float)(Math.Sin(Math.PI * Math.Min(beat / 0.4, 1)) * 0.8);
            values[2] = (float)Math.Sin(2 * Math.PI * time / 4);
        }
        else
        {
            for (var c = 0; c < values.Length; c++)
            {
                values[c] = (float)Math.Sin(2 * Math.PI * (c + 1) * time);
            }
        }
    }

    private static double Bell(double x, double center, double width) => Math.Exp(-((x - center) * (x - center)) / (2 * width * width));

    /// <summary>Starts appending samples after the newest one.</summary>
    /// <param name="data">The channels.</param>
    /// <param name="rate">The samples per second.</param>
    /// <returns>The timer; disposing it stops the samples.</returns>
    public static IDisposable Start(SweepData data, double rate)
    {
        var start = double.IsNaN(data.Head) ? 0 : data.Head + (1 / rate);
        var clock = Stopwatch.StartNew();
        var written = 0L;
        var values = new float[data.Channels];
        return new Timer(
            _ =>
            {
                // Every sample due since the last tick; the timer may come late, the samples do not.
                lock (values)
                {
                    for (var due = (long)(clock.Elapsed.TotalSeconds * rate); written < due; written++)
                    {
                        var time = start + (written / rate);
                        Values(time, values);
                        data.Append(time, values);
                    }
                }
            },
            null,
            0,
            10);
    }

    /// <summary>The same as code.</summary>
    public const string Code = """

            // New samples keep coming at a rate after the newest one; the field keeps the timer alive.
            private static Timer? _samples;

            private static void StartSamples(SweepData data, double rate)
            {
                var start = double.IsNaN(data.Head) ? 0 : data.Head + (1 / rate);
                var clock = Stopwatch.StartNew();
                var written = 0L;
                var values = new float[data.Channels];
                _samples = new Timer(
                    _ =>
                    {
                        // Every sample due since the last tick; the timer may come late, the samples do not.
                        lock (values)
                        {
                            for (var due = (long)(clock.Elapsed.TotalSeconds * rate); written < due; written++)
                            {
                                var time = start + (written / rate);
                                NextValues(time, values);
                                data.Append(time, values);
                            }
                        }
                    },
                    null,
                    0,
                    10);
            }

            // The values of all channels at a time: four channels are two leads of a heartbeat, a pulse wave and
            // breathing, three a heartbeat, a pulse wave and breathing, others sines.
            private static void NextValues(double time, float[] values)
            {
                if (values.Length == 4)
                {
                    var beat = time * 72 / 60 % 1;
                    var ecg = (0.12 * Gauss(beat, 0.18, 0.025)) - (0.15 * Gauss(beat, 0.36, 0.008)) + Gauss(beat, 0.39, 0.010) - (0.25 * Gauss(beat, 0.42, 0.010)) + (0.3 * Gauss(beat, 0.62, 0.045));
                    values[0] = (float)ecg;
                    values[1] = (float)((ecg * 0.8) + (0.05 * Math.Sin(time * 2 * Math.PI * 0.3)));
                    var pulse = (beat + 0.85) % 1;
                    values[2] = (float)((0.9 * Gauss(pulse, 0.25, 0.07)) + (0.3 * Gauss(pulse, 0.5, 0.06)));
                    values[3] = (float)Math.Sin(time * 2 * Math.PI * 0.25);
                }
                else if (values.Length == 3)
                {
                    var beat = (time * 1.2) % 1;
                    values[0] = (float)((Math.Exp(-((beat - 0.3) * (beat - 0.3)) / 0.0004) * 1.2) - (0.2 * Math.Exp(-((beat - 0.35) * (beat - 0.35)) / 0.001)));
                    values[1] = (float)(Math.Sin(Math.PI * Math.Min(beat / 0.4, 1)) * 0.8);
                    values[2] = (float)Math.Sin(2 * Math.PI * time / 4);
                }
                else
                {
                    for (var c = 0; c < values.Length; c++)
                    {
                        values[c] = (float)Math.Sin(2 * Math.PI * (c + 1) * time);
                    }
                }
            }

            private static double Gauss(double x, double center, double width) => Math.Exp(-((x - center) * (x - center)) / (2 * width * width));

        """;
}
