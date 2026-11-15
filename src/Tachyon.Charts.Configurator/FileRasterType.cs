using System.Collections.Concurrent;
using System.Globalization;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// A heatmap or spectrogram of a raster file far larger than memory, read through its stages. The sample writes the
/// test raster of the showroom page once on request; a file of one's own comes as a raster file (<c>.tras</c>).
/// </summary>
internal static class FileRasterType
{
    /// <summary>The id of the type.</summary>
    public const string Id = "fileRaster";

    /// <summary>The id of the sample.</summary>
    public const string SampleId = "testRaster";

    private static readonly ConcurrentDictionary<(string, RasterReduction), Lazy<RasterPyramid>> _open = new();

    // One mapping per file, shared by the stages of every reduction.
    private static readonly ConcurrentDictionary<string, Lazy<RasterFile>> _rasters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the type.</summary>
    public static ChartTypeInfo Type { get; } = new(Id, "Heatmap from a file", ChartCategory.TwoD, DataShape.RasterFile, SampleId,
        [
            new("colormap", "Colormap", ParameterKind.Choice, "Turbo") { Choices = CoreModule.Colormaps },
            new("reduction", "Stages keep", ParameterKind.Choice, nameof(RasterReduction.Maximum)) { Choices = Enum.GetNames<RasterReduction>(), Description = "Maximum keeps single peaks, Mean shows smooth fields." },
            new("rowsAlongX", "Rows along X", ParameterKind.Flag, false) { Description = "The rows of the file run along the X axis, as the rows of a spectrogram written over time." },
            new("refining", "Refining hint", ParameterKind.Flag, true) { Description = "A plate with the share built so far while the stages are built." },
        ], Apply, Code)
    { Usings = ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.Core.Overlays", "Tachyon.Charts.Rendering"] };

    /// <summary>Gets the sample: the test raster of the showroom page, as many cells per side as the count.</summary>
    public static SampleInfo Sample { get; } = new(
        SampleId,
        "A test raster of waves and hot cells, written once",
        DataShape.RasterFile,
        16_384,
        size =>
        {
            if (!File.Exists(PathOf(size)))
            {
                Write(PathOf(size), size);
            }

            return Raster(PathOf(size));
        },
        "RasterFile",
        SampleCode)
    { MinimumCount = 1024, MaximumCount = 65_536 };

    /// <summary>Gets the reader of raster files of one's own.</summary>
    public static DataLoader Loader { get; } = new(
        DataKind.File,
        DataShape.RasterFile,
        series => Raster(Path.GetFullPath(series.Data.Path!)),
        FileCode,
        series => series.Data.Path is { Length: > 0 } ? null : $"Choose the raster file (.tras) of {series.Name}.",
        []);

    /// <summary>Gets the path of the test raster of a size.</summary>
    /// <param name="size">The cells per side.</param>
    /// <returns>The path.</returns>
    public static string PathOf(int size) => Path.Combine(LargeFileType.Folder, string.Create(CultureInfo.InvariantCulture, $"raster-{size}x{size}.tras"));

    // The sample and a file give the raster; its stages follow from the reduction the series asks for.
    private static RasterPyramid Open(string path, RasterReduction reduction) => _open.GetOrAdd((Path.GetFullPath(path), reduction), key => new(() =>
    {
        return RasterPyramid.Build(Raster(key.Item1), key.Item1 + (key.Item2 == RasterReduction.Maximum ? ".tmip" : $".{key.Item2}.tmip".ToLowerInvariant()), key.Item2);
    })).Value;

    private static RasterFile Raster(string path) => _rasters.GetOrAdd(path, full => new(() => RasterFile.Open(full))).Value;

    private static void Write(string path, int size)
    {
        Directory.CreateDirectory(LargeFileType.Folder);
        var part = $"{path}.{Guid.NewGuid():N}.part";
        var columns = ColumnTerms(size);
        var band = Math.Max(1, (1 << 24) / size);
        var buffer = new float[(long)band * size];
        using (var writer = new RasterFileWriter(part, size, 0, 1, 0, 1))
        {
            for (long first = 0; first < size; first += band)
            {
                var rows = (int)Math.Min(band, size - first);
                Parallel.For(0, rows, row => MakeRow(first + row, size, columns, buffer.AsSpan(row * size, size)));
                writer.Append(buffer.AsSpan(0, rows * size));
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

    private static (float Slow, float Sine, float Cosine, float X)[] ColumnTerms(int width)
    {
        var terms = new (float, float, float, float)[width];
        for (var column = 0; column < width; column++)
        {
            var x = (double)column / width;
            terms[column] = ((float)Math.Sin(2 * Math.PI * 3 * x), (float)Math.Sin(2 * Math.PI * 17 * x), (float)Math.Cos(2 * Math.PI * 17 * x), (float)x);
        }

        return terms;
    }

    // Two waves, a hill, noise and now and then a hot cell: the same formula as the showroom page.
    private static void MakeRow(long row, long height, ReadOnlySpan<(float Slow, float Sine, float Cosine, float X)> columns, Span<float> values)
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

    private static Colormap ColormapOf(string name) => typeof(Colormaps).GetProperty(name)?.GetValue(null) as Colormap ?? Colormaps.Turbo;

    private static RasterReduction ReductionOf(SeriesSpec series) => Enum.TryParse<RasterReduction>(series.Parameters.GetText("reduction", nameof(RasterReduction.Maximum)), out var reduction) ? reduction : RasterReduction.Maximum;

    private static void Apply(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var raster = (RasterFile)data;
        var pyramid = Open(raster.Path, ReductionOf(series));
        chart.AddFileRaster(pyramid, s => s.Name(series.Name).RowsAlongX(values.GetFlag("rowsAlongX", false)).ColorAxis(a => a.Colormap(ColormapOf(values.GetText("colormap", "Turbo")))));
        if (values.GetFlag("refining", true))
        {
            chart.AddOverlay(surface => new RefiningOverlay(surface));
        }
    }

    private static string Code(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var reduction = ReductionOf(series);
        var stages = reduction == RasterReduction.Maximum ? "\".tmip\"" : $"\".{reduction.ToString().ToLowerInvariant()}.tmip\"";
        var code = $".AddFileRaster(RasterPyramid.Build({data}, {data}.Path + {stages}, RasterReduction.{reduction}), s => s.Name({CodeText.String(series.Name)}){(values.GetFlag("rowsAlongX", false) ? ".RowsAlongX()" : string.Empty)}.ColorAxis(a => a.Colormap(Colormaps.{values.GetText("colormap", "Turbo")})))";
        return values.GetFlag("refining", true) ? code + ".AddOverlay(chart => new RefiningOverlay(chart))" : code;
    }

    private static string SampleCode(string method) => SampleCatalog.Header("a test raster of waves, a hill, noise and hot cells, as many cells per side as the count, written once and kept", method, "RasterFile", "The cells per side.") + """
            {
                // Beside the other local data; the file stays open while the application runs.
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");
                var path = Path.Combine(folder, $"raster-{count}x{count}.tras");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(folder);
                    var columns = new (float Slow, float Sine, float Cosine, float X)[count];
                    for (var column = 0; column < count; column++)
                    {
                        var x = (double)column / count;
                        columns[column] = ((float)Math.Sin(2 * Math.PI * 3 * x), (float)Math.Sin(2 * Math.PI * 17 * x), (float)Math.Cos(2 * Math.PI * 17 * x), (float)x);
                    }

                    using (var writer = new RasterFileWriter(path + ".part", count, 0, 1, 0, 1))
                    {
                        var values = new float[count];
                        for (long row = 0; row < count; row++)
                        {
                            var y = (double)row / count;
                            var (slow, sine, cosine) = ((float)Math.Cos(2 * Math.PI * 2 * y), (float)Math.Sin(2 * Math.PI * 11 * y), (float)Math.Cos(2 * Math.PI * 11 * y));
                            var dy = (float)((y - 0.3) * (y - 0.3));
                            for (var column = 0; column < count; column++)
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

                            writer.Append(values);
                        }
                    }

                    File.Move(path + ".part", path, overwrite: true);
                }

                return RasterFile.Open(path);
            }

        """;

    private static DataCode FileCode(SeriesSpec series, string method) => new(
        "RasterFile",
        method,
        method + "()",
        $$"""

                /// <summary>Opens the raster of {{System.Security.SecurityElement.Escape(series.Name)}}; it stays open while the application runs.</summary>
                /// <returns>The raster.</returns>
                public static RasterFile {{method}}() => RasterFile.Open({{CodeText.String(series.Data.Path ?? string.Empty)}});

            """,
        ["Tachyon.Charts.Core.Data"]);
}
