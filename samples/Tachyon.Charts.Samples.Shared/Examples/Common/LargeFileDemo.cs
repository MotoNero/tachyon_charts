using System.Collections.Concurrent;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Resampling;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A test file far larger than a usual chart holds: floats at one microsecond each, written once on request beside the
/// other local data of the showroom and kept for the next visit. The values are a slow wave, a faster one, noise and a
/// rare spike, so that the picture shows whether an outlier survives the reduction to pixels.
/// </summary>
public static class LargeFileDemo
{
    /// <summary>The number of values of the showroom page: a billion floats, 4 GB.</summary>
    public const int PageCount = 1_000_000_000;

    private const int Chunk = 1 << 20;
    private const int Batch = 64;

    private static readonly ConcurrentDictionary<int, Lazy<(FileColumn<float> Column, ColumnPyramid<float> Pyramid)>> _open = new();

    /// <summary>Gets the folder of the test files.</summary>
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");

    /// <summary>Gets the path of the test file of a number of values.</summary>
    /// <param name="count">The number of values.</param>
    /// <returns>The path.</returns>
    public static string PathOf(int count) => Path.Combine(Folder, $"large-file-{count}.tcol");

    /// <summary>Gets the value at an index: the same formula as the sample of the configurator.</summary>
    /// <param name="index">The index.</param>
    /// <param name="count">The number of values.</param>
    /// <returns>The value.</returns>
    public static float Value(long index, int count)
    {
        var slow = Math.Sin(2 * Math.PI * 3 * index / count);
        var fast = 0.25 * Math.Sin(2 * Math.PI * index / 2_000_000.0);
        var noise = (((ulong)index * 0x9E3779B97F4A7C15UL) >> 40) / (double)(1 << 24) - 0.5;
        var spike = index % 97_654_321 == 12_345_678 ? 1.5 : 0;
        return (float)(slow + fast + (0.15 * noise) + spike);
    }

    /// <summary>
    /// Opens the test file of a number of values, writes it first if it is missing, and builds its pyramid in the
    /// background. Each file is opened once and stays open while the application runs.
    /// </summary>
    /// <param name="count">The number of values.</param>
    /// <returns>The column and its pyramid.</returns>
    public static (FileColumn<float> Column, ColumnPyramid<float> Pyramid) Open(int count) => _open.GetOrAdd(count, n => new(() =>
    {
        var path = PathOf(n);
        if (!File.Exists(path))
        {
            Write(path, n);
        }

        var column = ColumnFile.Open(path).Column<float>();
        return (column, ColumnPyramid<float>.Build(column, path + ".tpyr"));
    })).Value;

    // Chunks on all cores, written in order; into a file of its own first, so that a half written file is never opened.
    private static void Write(string path, int count)
    {
        Directory.CreateDirectory(Folder);
        var part = $"{path}.{Guid.NewGuid():N}.part";
        using (var writer = new ColumnFileWriter<float>(part, 1, 0, 1e-6))
        {
            var buffers = new float[Batch][];
            var chunks = (count + Chunk - 1) / Chunk;
            for (var first = 0; first < chunks; first += Batch)
            {
                var last = Math.Min(chunks, first + Batch);
                Parallel.For(first, last, chunk =>
                {
                    var start = (long)chunk * Chunk;
                    var values = buffers[chunk - first] ??= new float[Chunk];
                    var length = (int)Math.Min(Chunk, count - start);
                    for (var i = 0; i < length; i++)
                    {
                        values[i] = Value(start + i, count);
                    }
                });
                for (var chunk = first; chunk < last; chunk++)
                {
                    writer.Append(buffers[chunk - first].AsSpan(0, (int)Math.Min(Chunk, count - ((long)chunk * Chunk))));
                }
            }
        }

        try
        {
            File.Move(part, path);
        }
        catch (IOException)
        {
            // Another process wrote the same file meanwhile; its copy is as good.
            File.Delete(part);
        }
    }
}
