using System.Collections.Concurrent;
using System.Globalization;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// A scatter far larger than memory as a density: the points of a column file with X and Y, sorted once into tiles
/// beside it. The sample writes the test file of the showroom page once on request; a file of one's own comes as a
/// column file of two columns.
/// </summary>
internal static class PointDensityType
{
    /// <summary>The id of the type.</summary>
    public const string Id = "pointDensity";

    /// <summary>The id of the sample.</summary>
    public const string SampleId = "cloudFile";

    private const int Piece = 1 << 22;

    private static readonly ConcurrentDictionary<string, Lazy<PointTiles>> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the type.</summary>
    public static ChartTypeInfo Type { get; } = new(Id, "Scatter from a file", ChartCategory.TwoD, DataShape.PointTiles, SampleId,
        [
            new("colormap", "Colormap", ParameterKind.Choice, "Inferno") { Choices = CoreModule.Colormaps },
            new("log", "Logarithmic colors", ParameterKind.Flag, true) { Description = "Colors follow the logarithm of the counts, so that sparse and dense parts both show." },
            new("budget", "Points per frame", ParameterKind.Integer, 4_000_000.0) { Minimum = 100_000, Maximum = 50_000_000, Description = "The most points read from the tiles per frame; more make narrow views finer and slower." },
            new("refining", "Refining hint", ParameterKind.Flag, true) { Description = "A plate with the share built so far while the tiles are sorted." },
        ], Apply, Code)
    { Usings = ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.Core.Overlays"] };

    /// <summary>Gets the sample: the test file of the showroom page.</summary>
    public static SampleInfo Sample { get; } = new(
        SampleId,
        "A test file of points in clusters, written once",
        DataShape.PointTiles,
        200_000_000,
        count => Open(PathOf(count), () => Write(PathOf(count), count)),
        "PointTiles",
        SampleCode)
    { MinimumCount = 1000, MaximumCount = 2_000_000_000 };

    /// <summary>Gets the reader of column files of one's own: X in the first column, Y in the second.</summary>
    public static DataLoader Loader { get; } = new(
        DataKind.File,
        DataShape.PointTiles,
        series => Open(series.Data.Path!, null),
        FileCode,
        series => series.Data.Path is { Length: > 0 } ? null : $"Choose the column file of {series.Name}, with X and Y in two columns.",
        []);

    /// <summary>Gets the path of the test file of a number of points.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The path.</returns>
    public static string PathOf(int count) => Path.Combine(LargeFileType.Folder, $"cloud-{count.ToString(CultureInfo.InvariantCulture)}.tcol");

    /// <summary>Makes the points of a piece: the same formula as the showroom page.</summary>
    /// <param name="piece">The piece, which seeds its random numbers.</param>
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

    // Each file opens once and stays open while the configurator runs; its tiles are sorted once in the background.
    private static PointTiles Open(string path, Action? write) => _open.GetOrAdd(Path.GetFullPath(path), full => new(() =>
    {
        if (write is not null && !File.Exists(full))
        {
            write();
        }

        var file = ColumnFile.Open(full);
        if (file.ColumnCount < 2)
        {
            throw new InvalidDataException($"{full} has one column; a scatter needs X and Y.");
        }

        return file.Type == ColumnType.Single
            ? PointTiles.Build(file.Column<float>(0), file.Column<float>(1), full + ".tpts")
            : PointTiles.Build(file.Column<double>(0), file.Column<double>(1), full + ".tpts");
    })).Value;

    private static void Write(string path, int count)
    {
        Directory.CreateDirectory(LargeFileType.Folder);
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

    private static Colormap ColormapOf(string name) => typeof(Colormaps).GetProperty(name)?.GetValue(null) as Colormap ?? Colormaps.Inferno;

    private static void Apply(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddPointDensity((PointTiles)data, s =>
        {
            s.Name(series.Name).PointBudget((int)values.GetNumber("budget", 4_000_000)).ColorAxis(a =>
            {
                a.Colormap(ColormapOf(values.GetText("colormap", "Inferno")));
                if (values.GetFlag("log", true))
                {
                    a.Log();
                }
            });
        });
        if (values.GetFlag("refining", true))
        {
            chart.AddOverlay(surface => new RefiningOverlay(surface));
        }
    }

    private static string Code(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var budget = (int)values.GetNumber("budget", 4_000_000);
        var axis = $"a => a.Colormap(Colormaps.{values.GetText("colormap", "Inferno")}){(values.GetFlag("log", true) ? ".Log()" : string.Empty)}";
        var code = $".AddPointDensity({data}, s => s.Name({CodeText.String(series.Name)}){(budget != 4_000_000 ? $".PointBudget({budget.ToString(CultureInfo.InvariantCulture)})" : string.Empty)}.ColorAxis({axis}))";
        return values.GetFlag("refining", true) ? code + ".AddOverlay(chart => new RefiningOverlay(chart))" : code;
    }

    private static string SampleCode(string method) => SampleCatalog.Header("points in three clusters and a ring as two float columns of a file, written once and kept; their tiles sorted once beside them", method, "PointTiles", "The number of points.") + """
            {
                // Beside the other local data; the file stays open while the application runs.
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");
                var path = Path.Combine(folder, $"cloud-{count}.tcol");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(folder);
                    const int Piece = 1 << 22;
                    using (var writer = new ColumnFileWriter<float>(path + ".part", 2, 0, 1))
                    {
                        var rows = new float[Piece * 2];
                        for (long piece = 0; piece * Piece < count; piece++)
                        {
                            var length = (int)Math.Min(Piece, count - (piece * Piece));
                            var random = new Random((int)piece);
                            for (var i = 0; i < length; i++)
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
                                    var (a, b) = (Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()), Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()));
                                    (px, py) = (cx + (a * spread), cy + (b * spread));
                                }

                                (rows[i * 2], rows[(i * 2) + 1]) = ((float)px, (float)py);
                            }

                            writer.Append(rows.AsSpan(0, length * 2));
                        }
                    }

                    File.Move(path + ".part", path, overwrite: true);
                }

                var file = ColumnFile.Open(path);
                return PointTiles.Build(file.Column<float>(0), file.Column<float>(1), path + ".tpts");
            }

        """;

    private static DataCode FileCode(SeriesSpec series, string method)
    {
        var type = series.Data.Path is { Length: > 0 } path && File.Exists(path) && ColumnTypeOf(path) == ColumnType.Single ? "float" : "double";
        return new(
            "PointTiles",
            method,
            method + "()",
            $$"""

                    /// <summary>Opens the points of {{System.Security.SecurityElement.Escape(series.Name)}}: X in the first column of the file, Y in the second; the tiles are kept beside it.</summary>
                    /// <returns>The tiles.</returns>
                    public static PointTiles {{method}}()
                    {
                        var path = {{CodeText.String(series.Data.Path ?? string.Empty)}};
                        var file = ColumnFile.Open(path);
                        return PointTiles.Build(file.Column<{{type}}>(0), file.Column<{{type}}>(1), path + ".tpts");
                    }

                """,
            ["Tachyon.Charts.Core.Data"]);
    }

    private static ColumnType ColumnTypeOf(string path)
    {
        using var file = ColumnFile.Open(path);
        return file.Type;
    }
}
