using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Resampling;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The sample „recording“ of a line from a large file: ten million values per second recorded into a ring of files on
/// the disk, as many values as chosen, the same as the showroom page. The values only come while the preview draws them.
/// </summary>
internal static class RecordingSample
{
    /// <summary>The id of the sample.</summary>
    public const string Id = "recording";

    /// <summary>The values per second.</summary>
    public const double Rate = 10e6;

    /// <summary>The values of each file of the ring.</summary>
    public const int FileValues = 1 << 26;

    private static readonly ConcurrentDictionary<int, Lazy<LargeFileData>> _open = new();

    /// <summary>Gets the sample.</summary>
    public static SampleInfo Sample { get; } = new(
        Id,
        "A recording of 10 million values per second into a ring on the disk",
        DataShape.LargeFile,
        17 * FileValues,
        count => _open.GetOrAdd(count, n => new(() => Start(n))).Value,
        "(ColumnRecorder<float> Column, ColumnRecorder<float> Pyramid)",
        Code)
    { MinimumCount = 2 * FileValues, MaximumCount = 31 * FileValues };

    /// <summary>Gets the folder of the recordings, beside the other local data of Tachyon Charts.</summary>
    public static string Folder { get; } = Path.Combine(LargeFileType.Folder, "Recording");

    private static LargeFileData Start(int count)
    {
        // A folder per process; those of processes that ended are cleared.
        if (Directory.Exists(Folder))
        {
            foreach (var old in Directory.GetDirectories(Folder))
            {
                try
                {
                    Directory.Delete(old, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Another process records there.
                }
            }
        }

        var folder = Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}-{count}"));
        var feed = new Feed(new ColumnRecorder<float>(folder, "measurement", 0, 1 / Rate, Math.Max(2, count / FileValues), FileValues));
        return new LargeFileData(ColumnType.Single, count, (x, y, thickness, color) =>
        {
            var line = new ColumnLineSeries<float>(feed, feed, x, y) { Thickness = thickness };
            if (color is { } chosen)
            {
                line.Color = chosen;
            }

            return line;
        }, Task.CompletedTask);
    }

    private static string Code(string method) => SampleCatalog.Header("a recording of 10 million values per second into a ring of files on the disk", method, "(ColumnRecorder<float> Column, ColumnRecorder<float> Pyramid)") + $$"""
            {
                // A ring of files of 64 million values each beside the other local data; the oldest file leaves as a new one comes.
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples", "Recording", Environment.ProcessId.ToString());
                var recorder = new ColumnRecorder<float>(folder, "measurement", xStart: 0, xStep: 1e-7, maxFiles: Math.Max(2, count / (1 << 26)), fileValues: 1 << 26);

                // The measurement: chunks of 100,000 values as the clock goes, a slow wave with noise and a peak each second.
                var thread = new Thread(() =>
                {
                    var chunk = new float[100_000];
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    for (long written = 0; ; written += chunk.Length)
                    {
                        while (written + chunk.Length > clock.Elapsed.TotalSeconds * 10e6)
                        {
                            Thread.Sleep(1);
                        }

                        var wave = (float)Math.Sin(written * 1e-7 * 0.5);
                        for (var i = 0; i < chunk.Length; i++)
                        {
                            chunk[i] = wave + ((Random.Shared.NextSingle() - 0.5f) * 0.3f);
                        }

                        if (written / 10_000_000 != (written + chunk.Length) / 10_000_000)
                        {
                            chunk[chunk.Length / 2] = 1.5f;
                        }

                        recorder.Append(chunk);
                    }
                }) { IsBackground = true };
                thread.Start();
                return (recorder, recorder);
            }

        """;

    // The recorder for the line; a frame that reads it keeps the values coming, a second without one pauses them.
    private sealed class Feed : IColumnSource<float>, IColumnPyramid<float>
    {
        private const int Chunk = 100_000;
        private const int Pattern = 1 << 20;

        private readonly ColumnRecorder<float> _recorder;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastFrame;

        public Feed(ColumnRecorder<float> recorder)
        {
            _recorder = recorder;
            new Thread(Write) { IsBackground = true, Name = "Recording sample" }.Start();
        }

        public long Count => _recorder.Count;

        public double XStart => _recorder.XStart;

        public double XStep => _recorder.XStep;

        public long Version => _recorder.Version;

        public bool IsComplete => true;

        public double Progress => 1;

        public int BuiltPieces => 0;

        public MinMax<float> Total => _recorder.Total;

        public ReadOnlySpan<float> Read(long start, int maxLength)
        {
            Drawn();
            return _recorder.Read(start, maxLength);
        }

        public MinMax<float> Query(long start, long end)
        {
            Drawn();
            return _recorder.Query(start, end);
        }

        public MinMax<float> QueryBlocks(long start, long end)
        {
            Drawn();
            return _recorder.QueryBlocks(start, end);
        }

        public MinMax<float> QueryAvailable(long start, long end) => Query(start, end);

        private void Drawn() => Volatile.Write(ref _lastFrame, _clock.ElapsedMilliseconds);

        private void Write()
        {
            var pattern = new float[Pattern];
            var random = new Random(5); // NOSONAR: sample data drawn the same each time, nothing secret
            for (var i = 0; i < Pattern; i++)
            {
                pattern[i] = (float)(random.NextDouble() - 0.5) * 0.3f;
            }

            var chunk = new float[Chunk];
            long written = 0;
            var start = _clock.Elapsed.TotalSeconds;
            while (!Environment.HasShutdownStarted)
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
}
