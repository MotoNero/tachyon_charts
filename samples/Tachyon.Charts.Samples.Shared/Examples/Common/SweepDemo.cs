using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>An invented patient monitor: two ECG leads, a pulse wave and the breath, 25 000 samples per second each.</summary>
public static class SweepDemo
{
    /// <summary>The samples per second of each channel; four channels make 100 000 in all.</summary>
    public const int Rate = 25_000;

    /// <summary>The width of the screen in seconds.</summary>
    public const double Window = 6;

    /// <summary>How to operate a monitor display.</summary>
    public const string Hint = "The trace writes from the left over the previous one | Wheel on an axis: zoom the values";

    /// <summary>The value ranges of the channels, from the top.</summary>
    public static readonly DoubleRange[] Ranges = [new(-0.6, 1.4), new(-0.6, 1.4), new(-0.1, 1.1), new(-1.2, 1.2)];

    /// <summary>The names of the channels, from the top.</summary>
    public static readonly string[] Names = ["ECG I", "ECG II", "Pleth", "Resp"];

    private static readonly Lock _gate = new();
    private static readonly Dictionary<SweepMode, SweepData> _running = [];

    /// <summary>Gets a monitor that keeps running in the background, one per mode.</summary>
    /// <param name="mode">How the display runs.</param>
    /// <returns>The samples.</returns>
    public static SweepData Live(SweepMode mode)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(mode, out var data))
            {
                return data;
            }

            data = new SweepData(Names.Length, (int)(Rate * (Window + 1)), Window) { Mode = mode };
            _running[mode] = data;
            var thread = new Thread(() => Produce(data)) { IsBackground = true, Name = "Monitor " + mode };
            thread.Start();
            return data;
        }
    }

    /// <summary>Fills a monitor with a given number of seconds at once, for pictures that are the same on every run.</summary>
    /// <param name="mode">How the display runs.</param>
    /// <param name="seconds">How long the monitor ran.</param>
    /// <returns>The samples.</returns>
    public static SweepData Recorded(SweepMode mode, double seconds)
    {
        var data = new SweepData(Names.Length, (int)(Rate * (Window + 1)), Window) { Mode = mode };
        var count = (int)(seconds * Rate);
        var times = new double[1000];
        var values = new float[times.Length * Names.Length];
        for (var done = 0; done < count; done += times.Length)
        {
            var length = Math.Min(times.Length, count - done);
            Fill(done, times.AsSpan(0, length), values.AsSpan(0, length * Names.Length));
            data.Append(times.AsSpan(0, length), values.AsSpan(0, length * Names.Length));
        }

        return data;
    }

    // Blocks of 10 ms on a clock, so that the sample rate holds however the thread is scheduled.
    private static void Produce(SweepData data)
    {
        var times = new double[Rate / 100];
        var values = new float[times.Length * Names.Length];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long written = 0;
        while (true)
        {
            var due = (long)(clock.Elapsed.TotalSeconds * Rate);
            while (written + times.Length <= due)
            {
                Fill(written, times, values);
                data.Append(times, values);
                written += times.Length;
            }

            Thread.Sleep(5);
        }
    }

    private static void Fill(long first, Span<double> times, Span<float> values)
    {
        for (var i = 0; i < times.Length; i++)
        {
            var t = (first + i) / (double)Rate;
            times[i] = t;
            var beat = t * 72 / 60 % 1;
            var ecg = Ecg(beat);
            values[(i * 4) + 0] = (float)ecg;
            values[(i * 4) + 1] = (float)((ecg * 0.8) + (0.05 * Math.Sin(t * 2 * Math.PI * 0.3)));
            values[(i * 4) + 2] = (float)Pulse((beat + 0.85) % 1);
            values[(i * 4) + 3] = (float)Math.Sin(t * 2 * Math.PI * 0.25);
        }
    }

    // P wave, QRS complex and T wave as bells over one beat.
    private static double Ecg(double beat) =>
        (0.12 * Bell(beat, 0.18, 0.025)) - (0.15 * Bell(beat, 0.36, 0.008)) + Bell(beat, 0.39, 0.010)
        - (0.25 * Bell(beat, 0.42, 0.010)) + (0.3 * Bell(beat, 0.62, 0.045));

    private static double Pulse(double beat) => (0.9 * Bell(beat, 0.25, 0.07)) + (0.3 * Bell(beat, 0.5, 0.06));

    private static double Bell(double x, double center, double width) => Math.Exp(-((x - center) * (x - center)) / (2 * width * width));
}
