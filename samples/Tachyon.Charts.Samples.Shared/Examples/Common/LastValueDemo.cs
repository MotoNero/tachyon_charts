using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Three temperatures that keep coming in, close to each other at times, so that their marks must move apart.</summary>
public static class LastValueDemo
{
    /// <summary>How to operate the page.</summary>
    public const string Hint = "The marks at the axis show the newest value of each line and move apart when they meet | Wheel: zoom | Drag: pan";

    /// <summary>The names of the lines.</summary>
    public static readonly string[] Names = ["Inlet", "Core", "Outlet"];

    /// <summary>The colors of the lines.</summary>
    public static readonly Tachyon.Charts.Rendering.Rgba8[] Colors = [new(76, 201, 240, 255), new(255, 190, 11, 255), new(247, 37, 133, 255)];

    private static readonly Lock _gate = new();
    private static FifoDataSource<double, double>[]? _live;

    /// <summary>Gets the lines of a fixed time, the same on every run.</summary>
    /// <param name="points">The number of points per line.</param>
    /// <returns>The lines.</returns>
    public static DataSource<double, double>[] Recorded(int points)
    {
        var lines = new DataSource<double, double>[Names.Length];
        for (var line = 0; line < lines.Length; line++)
        {
            lines[line] = new DataSource<double, double>(points);
            for (var i = 0; i < points; i++)
            {
                lines[line].Append(i * 0.1, Value(line, i * 0.1));
            }
        }

        return lines;
    }

    /// <summary>Gets lines that grow by 50 points per second in the background.</summary>
    /// <returns>The lines.</returns>
    public static FifoDataSource<double, double>[] Live()
    {
        lock (_gate)
        {
            if (_live is not null)
            {
                return _live;
            }

            _live = [.. Names.Select(_ => new FifoDataSource<double, double>(3000))];
            var lines = _live;
            var thread = new Thread(() => Produce(lines)) { IsBackground = true, Name = "Temperatures" };
            thread.Start();
            return _live;
        }
    }

    private static void Produce(FifoDataSource<double, double>[] lines)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var written = 0;
        while (!Environment.HasShutdownStarted)
        {
            var due = (int)(clock.Elapsed.TotalSeconds * 50);
            for (; written < due; written++)
            {
                var t = written * 0.02;
                for (var line = 0; line < lines.Length; line++)
                {
                    lines[line].Append(t, Value(line, t));
                }
            }

            Thread.Sleep(20);
        }
    }

    // Three slow waves around 60 °C that cross now and then.
    private static double Value(int line, double t) =>
        60 + (8 * Math.Sin((t * 0.21) + (line * 2.1))) + (2 * Math.Sin((t * 1.7) + line)) + (line - 1);
}
