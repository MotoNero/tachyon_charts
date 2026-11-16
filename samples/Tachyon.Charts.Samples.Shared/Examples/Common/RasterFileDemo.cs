using System.Collections.Concurrent;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A test raster far larger than a chart usually holds: waves, a hill, noise and single hot cells, written once on
/// request beside the other local data of the showroom; its stages are built beside it in the background. The same
/// formula as the sample of the configurator.
/// </summary>
public static class RasterFileDemo
{
    /// <summary>The cells per side of the showroom page: 16384, 268 million cells, 1 GB.</summary>
    public const int PageSize = 16_384;

    private static readonly ConcurrentDictionary<(int, long), Lazy<RasterPyramid>> _open = new();

    /// <summary>Gets the path of the test raster of a size.</summary>
    /// <param name="width">The columns.</param>
    /// <param name="height">The rows.</param>
    /// <returns>The path.</returns>
    public static string PathOf(int width, long height) => Path.Combine(LargeFileDemo.Folder, $"raster-{width}x{height}.tras");

    /// <summary>Opens the raster, writes it first if it is missing, and starts its stages; each file opens once.</summary>
    /// <param name="width">The columns.</param>
    /// <param name="height">The rows.</param>
    /// <param name="reduction">How the stages sum up cells.</param>
    /// <returns>The stages, ready once their building is complete.</returns>
    public static RasterPyramid Open(int width, long height, RasterReduction reduction = RasterReduction.Maximum) => _open.GetOrAdd((width, height), size => new(() =>
    {
        var path = PathOf(size.Item1, size.Item2);
        if (!File.Exists(path))
        {
            Write(path, size.Item1, size.Item2);
        }

        return RasterPyramid.Build(RasterFile.Open(path), path + ".tmip", reduction);
    })).Value;

    /// <summary>Writes a test raster: the rows of a band on all cores, then in order.</summary>
    /// <param name="path">The path.</param>
    /// <param name="width">The columns.</param>
    /// <param name="height">The rows.</param>
    public static void Write(string path, int width, long height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = $"{path}.{Guid.NewGuid():N}.part";
        var columns = ColumnTerms(width);
        var band = Math.Max(1, (1 << 24) / width);
        var buffer = new float[(long)band * width];
        using (var writer = new RasterFileWriter(part, width, 0, 1, 0, 1))
        {
            for (long first = 0; first < height; first += band)
            {
                var rows = (int)Math.Min(band, height - first);
                Parallel.For(0, rows, row => MakeRow(first + row, height, columns, buffer.AsSpan(row * width, width)));
                writer.Append(buffer.AsSpan(0, rows * width));
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

    /// <summary>Gets the terms of the columns that every row shares.</summary>
    /// <param name="width">The columns.</param>
    /// <returns>Per column: the slow wave, sine and cosine of the slanted wave, and the place from 0 to 1.</returns>
    public static (float Slow, float Sine, float Cosine, float X)[] ColumnTerms(int width)
    {
        var terms = new (float, float, float, float)[width];
        for (var column = 0; column < width; column++)
        {
            var x = (double)column / width;
            terms[column] = ((float)Math.Sin(2 * Math.PI * 3 * x), (float)Math.Sin(2 * Math.PI * 17 * x), (float)Math.Cos(2 * Math.PI * 17 * x), (float)x);
        }

        return terms;
    }

    /// <summary>Makes a row: two waves, a hill, noise and now and then a hot cell.</summary>
    /// <param name="row">The row.</param>
    /// <param name="height">The rows of the raster.</param>
    /// <param name="columns">The terms of the columns.</param>
    /// <param name="values">Receives the row.</param>
    public static void MakeRow(long row, long height, ReadOnlySpan<(float Slow, float Sine, float Cosine, float X)> columns, Span<float> values)
    {
        var y = (double)row / height;
        var (slow, sine, cosine) = ((float)Math.Cos(2 * Math.PI * 2 * y), (float)Math.Sin(2 * Math.PI * 11 * y), (float)Math.Cos(2 * Math.PI * 11 * y));
        var dy = (float)((y - 0.3) * (y - 0.3));
        for (var column = 0; column < values.Length; column++)
        {
            var c = columns[column];
            var dx = (c.X - 0.7f) * (c.X - 0.7f);
            var hash = ((ulong)row * 0x9E3779B97F4A7C15UL) ^ ((ulong)column * 0xC2B2AE3D27D4EB4FUL);
            hash ^= hash >> 29;
            hash *= 0xBF58476D1CE4E5B9UL;
            hash ^= hash >> 32;
            var noise = ((hash & 0xFFFF) / 65535f) - 0.5f;
            var hot = hash % 1_000_003 == 0 ? 3f : 0f;
            values[column] = (c.Slow * slow) + (0.5f * ((c.Sine * cosine) + (c.Cosine * sine))) + (0.8f * MathF.Exp(-(dx + dy) * 60)) + (0.15f * noise) + hot;
        }
    }
}
