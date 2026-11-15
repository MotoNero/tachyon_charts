using System.Collections.Concurrent;
using System.Globalization;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// The sample of a cloud from a file: the scanned landscape of the showroom page as three float columns, written once
/// on request beside the other local data of Tachyon Charts; its pages are sorted beside it in the background.
/// </summary>
internal static class CloudFileSamples3D
{
    /// <summary>The id of the sample.</summary>
    public const string Id = "landscapeFile";

    private const int Piece = 1 << 22;

    private static readonly ConcurrentDictionary<int, Lazy<CloudPages>> _open = new();

    /// <summary>Gets the folder of the test files.</summary>
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");

    /// <summary>Gets the sample.</summary>
    public static SampleInfo Sample { get; } = new(
        Id,
        "A scanned landscape from a file, written once",
        DataShape.CloudPages,
        200_000_000,
        count => Open(count),
        "CloudPages",
        Code)
    { MinimumCount = 1000, MaximumCount = 2_000_000_000 };

    /// <summary>Opens the pages of the test file, writing it first if it is missing; each opens once.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The pages.</returns>
    public static CloudPages Open(int count) => _open.GetOrAdd(count, n => new(() =>
    {
        var path = Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture, $"landscape-{n}.tcol"));
        if (!File.Exists(path))
        {
            Write(path, n);
        }

        var file = ColumnFile.Open(path);
        return CloudPages.Build(file.Column<float>(0), file.Column<float>(1), file.Column<float>(2), path + ".tcld");
    })).Value;

    // Hills, a fine ripple and a little noise; each piece seeds its own random numbers: the same as the showroom page.
    private static void MakePiece(long piece, Span<float> rows)
    {
        var random = new Random((int)piece); // NOSONAR: makes sample data, no security
        for (var i = 0; i < rows.Length / 3; i++)
        {
            var (a, b) = ((random.NextDouble() * 2) - 1, (random.NextDouble() * 2) - 1);
            var height = (0.45 * Math.Sin(a * 4) * Math.Cos(b * 3)) + (0.15 * Math.Sin((a + b) * 11)) + (0.02 * random.NextDouble());
            (rows[i * 3], rows[(i * 3) + 1], rows[(i * 3) + 2]) = ((float)a, (float)b, (float)height);
        }
    }

    private static void Write(string path, long count)
    {
        Directory.CreateDirectory(Folder);
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

    private static string Code(string method) => SampleCatalog.Header("a scanned landscape as three float columns of a file, written once and kept; its pages sorted once beside it", method, "CloudPages", "The number of points.") + """
            {
                // Beside the other local data; the file stays open while the application runs.
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");
                var path = Path.Combine(folder, $"landscape-{count}.tcol");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(folder);
                    const int Piece = 1 << 22;
                    using (var writer = new ColumnFileWriter<float>(path + ".part", 3, 0, 1))
                    {
                        var rows = new float[Piece * 3];
                        for (long piece = 0; piece * Piece < count; piece++)
                        {
                            var length = (int)Math.Min(Piece, count - (piece * Piece));
                            var random = new Random((int)piece);
                            for (var i = 0; i < length; i++)
                            {
                                var (a, b) = ((random.NextDouble() * 2) - 1, (random.NextDouble() * 2) - 1);
                                var height = (0.45 * Math.Sin(a * 4) * Math.Cos(b * 3)) + (0.15 * Math.Sin((a + b) * 11)) + (0.02 * random.NextDouble());
                                (rows[i * 3], rows[(i * 3) + 1], rows[(i * 3) + 2]) = ((float)a, (float)b, (float)height);
                            }

                            writer.Append(rows.AsSpan(0, length * 3));
                        }
                    }

                    File.Move(path + ".part", path, overwrite: true);
                }

                var file = ColumnFile.Open(path);
                return CloudPages.Build(file.Column<float>(0), file.Column<float>(1), file.Column<float>(2), path + ".tcld");
            }

        """;
}
