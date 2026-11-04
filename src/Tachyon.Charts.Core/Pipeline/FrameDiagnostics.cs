using System.Diagnostics;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// Collects the numbers of the diagnostics overlay and turns them into lines a few times per second.
/// </summary>
internal sealed class FrameDiagnostics
{
    private const int Intervals = 256;
    private static readonly long _updateInterval = Stopwatch.Frequency / 4;

    private readonly long[] _intervals = new long[Intervals];
    private readonly long[] _sorted = new long[Intervals];
    private long _windowStart = Stopwatch.GetTimestamp();
    private long _lastFrame;
    private int _intervalCount;
    private int _nextInterval;
    private int _frames;
    private long _cpuTicks;
    private long _drawCalls;
    private long _uploadedBytes;

    /// <summary>Gets when the lines are due again.</summary>
    public long NextUpdate => _windowStart + _updateInterval;

    /// <summary>Counts a finished frame.</summary>
    /// <param name="frameStart">When the frame started.</param>
    /// <param name="cpuTicks">The time until the frame was handed to the device, in stopwatch ticks.</param>
    /// <param name="drawCalls">The draw calls of the frame.</param>
    /// <param name="uploadedBytes">The bytes uploaded for the frame.</param>
    public void Record(long frameStart, long cpuTicks, long drawCalls, long uploadedBytes)
    {
        // A pause longer than a second is waiting for data, not a slow frame.
        var interval = frameStart - _lastFrame;
        if (_lastFrame != 0 && interval < Stopwatch.Frequency)
        {
            _intervals[_nextInterval] = interval;
            _nextInterval = (_nextInterval + 1) % Intervals;
            _intervalCount = Math.Min(_intervalCount + 1, Intervals);
        }

        _lastFrame = frameStart;
        _frames++;
        _cpuTicks += cpuTicks;
        _drawCalls += drawCalls;
        _uploadedBytes += uploadedBytes;
    }

    /// <summary>Writes the lines of the last period and starts a new one.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="drawnPoints">The points drawn in the last frame.</param>
    /// <param name="sourcePoints">The points in the data.</param>
    /// <param name="backend">The name of the renderer.</param>
    /// <param name="text">Receives the lines.</param>
    public void Update(long now, long drawnPoints, long sourcePoints, string backend, DiagnosticsText text)
    {
        var seconds = Math.Max((now - _windowStart) / (double)Stopwatch.Frequency, 1e-6);
        var frames = Math.Max(_frames, 1);
        text.Add("FPS", _frames / seconds, "F0", string.Empty);
        if (_intervalCount > 0)
        {
            var sorted = _sorted.AsSpan(0, _intervalCount);
            _intervals.AsSpan(0, _intervalCount).CopyTo(sorted);
            sorted.Sort();
            text.Add("Frame p50", ToMilliseconds(sorted[(sorted.Length - 1) / 2]), "F2", "ms");
            text.Add("Frame p99", ToMilliseconds(sorted[(int)((sorted.Length - 1) * 0.99)]), "F2", "ms");
        }

        text.Add("CPU", ToMilliseconds(_cpuTicks) / frames, "F2", "ms per frame");
        text.Add("Draw calls", _drawCalls / (double)frames, "N0", string.Empty);
        text.Add("Points drawn", drawnPoints, "N0", string.Empty);
        text.Add("Points in data", sourcePoints, "N0", string.Empty);
        text.Add("Upload", _uploadedBytes / (double)frames, "N0", "B per frame");
        text.Add(backend);

        _windowStart = now;
        _frames = 0;
        _cpuTicks = 0;
        _drawCalls = 0;
        _uploadedBytes = 0;
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
