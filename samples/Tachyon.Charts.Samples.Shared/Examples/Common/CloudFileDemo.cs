using System.Collections.Concurrent;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A test file of a scatter: X and Y as two float columns, three clusters of different spread and a thin ring, written
/// once on request beside the other local data of the showroom; its tiles are built beside it in the background. The
/// same formula as the sample of the configurator.
/// </summary>
public static class CloudFileDemo
{
    /// <summary>The points of the showroom page: 200 million, 1.6 GB.</summary>
    public const int PageCount = 200_000_000;

    private const int Piece = 1 << 22;

    private static readonly ConcurrentDictionary<int, Lazy<PointTiles>> _open = new();

    /// <summary>Gets the path of the test file of a number of points.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The path.</returns>
    public static string PathOf(int count) => Path.Combine(LargeFileDemo.Folder, $"cloud-{count}.tcol");

    /// <summary>Opens the points, writes them first if they are missing, and starts their tiles; each file opens once.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The tiles, ready once their building is complete.</returns>
    public static PointTiles Open(int count) => _open.GetOrAdd(count, n => new(() =>
    {
        var path = PathOf(n);
        if (!File.Exists(path))
        {
            Write(path, n);
        }

        var file = ColumnFile.Open(path);
        return PointTiles.Build(file.Column<float>(0), file.Column<float>(1), path + ".tpts");
    })).Value;

    /// <summary>Makes the points of a piece: the piece number seeds its random numbers, so any piece can be made alone.</summary>
    /// <param name="piece">The piece.</param>
    /// <param name="rows">Receives X and Y of each point in turn.</param>
    public static void MakePiece(long piece, Span<float> rows)
    {
        var random = new Random((int)piece); // NOSONAR: makes sample data, no security
        for (var i = 0; i < rows.Length / 2; i++)
        {
            double px, py;
            if (i % 101 == 0)
            {
                var angle = random.NextDouble() * 2 * Math.PI;
                (px, py) = (0.5 + (4 * Math.Cos(angle)), 1.5 + (4 * Math.Sin(angle)));
            }
            else
            {
                var (cx, cy, spread) = (i % 7) switch
                {
                    < 4 => (1.0, 1.0, 0.3),
                    < 6 => (3.0, 2.0, 0.8),
                    _ => (-2.0, 3.0, 0.15),
                };
                (px, py) = (cx + (Normal(random) * spread), cy + (Normal(random) * spread));
            }

            (rows[i * 2], rows[(i * 2) + 1]) = ((float)px, (float)py);
        }
    }

    private static double Normal(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    // Pieces on all cores, written in order; into a file of its own first, so that a half written file is never opened.
    private static void Write(string path, int count)
    {
        Directory.CreateDirectory(LargeFileDemo.Folder);
        var part = $"{path}.{Guid.NewGuid():N}.part";
        using (var writer = new ColumnFileWriter<float>(part, 2, 0, 1))
        {
            var pieces = (count + Piece - 1) / Piece;
            var buffers = new float[32][];
            for (var first = 0; first < pieces; first += buffers.Length)
            {
                var last = Math.Min(pieces, first + buffers.Length);
                Parallel.For(first, last, piece =>
                {
                    var length = (int)Math.Min(Piece, count - ((long)piece * Piece));
                    MakePiece(piece, (buffers[piece - first] ??= new float[Piece * 2]).AsSpan(0, length * 2));
                });
                for (var piece = first; piece < last; piece++)
                {
                    var length = (int)Math.Min(Piece, count - ((long)piece * Piece));
                    writer.Append(buffers[piece - first].AsSpan(0, length * 2));
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
