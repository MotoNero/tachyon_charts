using System.Collections.Concurrent;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A scanned landscape far larger than memory: X, Y and Z as three float columns of a file, written once on request
/// beside the other local data of the showroom; its pages are sorted beside it in the background. The same formula as
/// the sample of the configurator.
/// </summary>
public static class CloudPagesDemo
{
    /// <summary>The points of the showroom page: 200 million, 2.4 GB.</summary>
    public const int PageCount = 200_000_000;

    private const int Piece = 1 << 22;

    private static readonly ConcurrentDictionary<long, Lazy<CloudPages>> _open = new();

    /// <summary>Gets the path of the test file of a number of points.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The path.</returns>
    public static string PathOf(long count) => Path.Combine(LargeFileDemo.Folder, $"landscape-{count}.tcol");

    /// <summary>Opens the points, writes them first if they are missing, and starts their pages; each file opens once.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The pages, ready once their building is complete.</returns>
    public static CloudPages Open(long count) => _open.GetOrAdd(count, n => new(() =>
    {
        var path = PathOf(n);
        if (!File.Exists(path))
        {
            Write(path, n);
        }

        var file = ColumnFile.Open(path);
        return CloudPages.Build(file.Column<float>(0), file.Column<float>(1), file.Column<float>(2), path + ".tcld");
    })).Value;

    /// <summary>Makes the points of a piece: hills, a fine ripple and a little noise; the piece seeds its random numbers.</summary>
    /// <param name="piece">The piece.</param>
    /// <param name="rows">Receives X, Y and Z of each point in turn.</param>
    public static void MakePiece(long piece, Span<float> rows)
    {
        var random = new Random((int)piece); // NOSONAR: makes sample data, no security
        for (var i = 0; i < rows.Length / 3; i++)
        {
            var (a, b) = ((random.NextDouble() * 2) - 1, (random.NextDouble() * 2) - 1);
            var height = (0.45 * Math.Sin(a * 4) * Math.Cos(b * 3)) + (0.15 * Math.Sin((a + b) * 11)) + (0.02 * random.NextDouble());
            (rows[i * 3], rows[(i * 3) + 1], rows[(i * 3) + 2]) = ((float)a, (float)b, (float)height);
        }
    }

    /// <summary>Writes the test file: pieces on all cores, written in order.</summary>
    /// <param name="path">The path.</param>
    /// <param name="count">The number of points.</param>
    public static void Write(string path, long count)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = $"{path}.{Guid.NewGuid():N}.part";
        using (var writer = new ColumnFileWriter<float>(part, 3, 0, 1))
        {
            var pieces = (count + Piece - 1) / Piece;
            var buffers = new float[32][];
            for (long first = 0; first < pieces; first += buffers.Length)
            {
                var last = Math.Min(pieces, first + buffers.Length);
                Parallel.For(first, last, piece =>
                {
                    var length = (int)Math.Min(Piece, count - (piece * Piece));
                    MakePiece(piece, (buffers[piece - first] ??= new float[Piece * 3]).AsSpan(0, length * 3));
                });
                for (var piece = first; piece < last; piece++)
                {
                    var length = (int)Math.Min(Piece, count - (piece * Piece));
                    writer.Append(buffers[piece - first].AsSpan(0, length * 3));
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
