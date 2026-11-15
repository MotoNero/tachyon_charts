using System.Diagnostics;
using System.Globalization;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Resampling;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A measurement recorded on the disk as it arrives: ten million values per second into a ring of 17 files of 64 million
/// values, about 1.1 billion values and 4.6 GB, beside the other local data. The values only come while a chart draws
/// them: a second without a frame pauses the recording, the next frame goes on.
/// </summary>
public sealed class RecordingDemo : IColumnSource<float>, IColumnPyramid<float>
{
    /// <summary>The values per second.</summary>
    public const double Rate = 10e6;

    /// <summary>The files of the ring.</summary>
    public const int Files = 17;

    private const int Chunk = 100_000;
    private const int Pattern = 1 << 20;
    private static readonly Lazy<RecordingDemo> _shared = new(() => new RecordingDemo(Path.Combine(LargeFileDemo.Folder, "Recording")));

    private readonly ColumnRecorder<float> _recorder;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastFrame;

    private RecordingDemo(string root)
    {
        // A folder per process; those of processes that ended are cleared.
        if (Directory.Exists(root))
        {
            foreach (var old in Directory.GetDirectories(root))
            {
                try
                {
                    Directory.Delete(old, recursive: true);
                }
                catch (IOException)
                {
                    // Another showroom records there.
                }
                catch (UnauthorizedAccessException)
                {
                    // The same.
                }
            }
        }

        var folder = Path.Combine(root, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _recorder = new ColumnRecorder<float>(folder, "measurement", xStart: 0, xStep: 1 / Rate, maxFiles: Files);
        new Thread(Write) { IsBackground = true, Name = "Recording demo" }.Start();
    }

    /// <summary>Gets the recording of the process, started on first use.</summary>
    public static RecordingDemo Shared => _shared.Value;

    /// <summary>Gets the recorder.</summary>
    public ColumnRecorder<float> Recorder => _recorder;

    /// <inheritdoc />
    public long Count => _recorder.Count;

    /// <inheritdoc />
    public double XStart => _recorder.XStart;

    /// <inheritdoc />
    public double XStep => _recorder.XStep;

    /// <inheritdoc />
    public long Version => _recorder.Version;

    /// <inheritdoc />
    public bool IsComplete => true;

    /// <inheritdoc />
    public double Progress => 1;

    /// <inheritdoc />
    public int BuiltPieces => 0;

    /// <inheritdoc />
    public MinMax<float> Total => _recorder.Total;

    /// <inheritdoc />
    public ReadOnlySpan<float> Read(long start, int maxLength)
    {
        Drawn();
        return _recorder.Read(start, maxLength);
    }

    /// <inheritdoc />
    public MinMax<float> Query(long start, long end)
    {
        Drawn();
        return _recorder.Query(start, end);
    }

    /// <inheritdoc />
    public MinMax<float> QueryBlocks(long start, long end)
    {
        Drawn();
        return _recorder.QueryBlocks(start, end);
    }

    /// <inheritdoc />
    public MinMax<float> QueryAvailable(long start, long end) => Query(start, end);

    private void Drawn() => Volatile.Write(ref _lastFrame, _clock.ElapsedMilliseconds);

    // Values as the clock goes, a slow wave with noise and a peak each second; paused while no chart draws.
    private void Write()
    {
        var pattern = new float[Pattern];
        var random = new Random(5);
        for (var i = 0; i < Pattern; i++)
        {
            pattern[i] = (float)(random.NextDouble() - 0.5) * 0.3f;
        }

        var chunk = new float[Chunk];
        long written = 0;
        var start = _clock.Elapsed.TotalSeconds;
        while (true)
        {
            if (_clock.ElapsedMilliseconds - Volatile.Read(ref _lastFrame) > 1000 && written > 0)
            {
                Thread.Sleep(50);
                start = _clock.Elapsed.TotalSeconds - (written / Rate);
                continue;
            }

            if (written + Chunk > (_clock.Elapsed.TotalSeconds - start) * Rate)
            {
                Thread.Sleep(1);
                continue;
            }

            var wave = (float)Math.Sin(written / Rate * 0.5);
            var offset = (int)(written % Pattern);
            for (var i = 0; i < Chunk; i++)
            {
                chunk[i] = wave + pattern[(offset + i) & (Pattern - 1)];
            }

            if (written / (long)Rate != (written + Chunk) / (long)Rate)
            {
                chunk[Chunk / 2] = 1.5f;
            }

            _recorder.Append(chunk);
            written += Chunk;
        }
    }
}
