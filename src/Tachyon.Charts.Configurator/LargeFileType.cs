using System.Collections.Concurrent;
using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Resampling;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>A column of a file and its pyramid, made into a line on given axes.</summary>
/// <param name="Type">The type of the values, which the code names.</param>
/// <param name="Count">The number of values.</param>
/// <param name="Create">Makes the line on its axes, with a thickness and a color or none for the usual one.</param>
/// <param name="Ready">Completes when the pyramid is built.</param>
internal sealed record LargeFileData(ColumnType Type, long Count, Func<AxisBase, AxisBase, double, Rgba8?, ChartSeries> Create, Task Ready);

/// <summary>
/// A line over a file larger than memory: a column file, read through the pyramid of its extremes. The sample writes a
/// test file once on request, the same as the showroom page; a file of one's own comes as a column file.
/// </summary>
internal static class LargeFileType
{
    /// <summary>The id of the type.</summary>
    public const string Id = "fileLine";

    /// <summary>The id of the sample, the test file of the showroom.</summary>
    public const string SampleId = "largeFile";

    private const int Chunk = 1 << 20;
    private const int Batch = 64;

    private static readonly ConcurrentDictionary<string, Lazy<LargeFileData>> _open = new(StringComparer.OrdinalIgnoreCase);
    private static readonly (string, bool)[] _packages = [];

    /// <summary>Gets the folder of the test files, beside the other local data of Tachyon Charts.</summary>
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");

    /// <summary>Gets the type.</summary>
    public static ChartTypeInfo Type { get; } = new(Id, "Line from a large file", ChartCategory.TwoD, DataShape.LargeFile, SampleId,
        [
            new("thickness", "Thickness", ParameterKind.Number, 1.0) { Minimum = 0.5, Maximum = 10 },
            new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the usual blue." },
            new("refining", "Refining hint", ParameterKind.Flag, true) { Description = "A plate with the share read so far while the pyramid grows." },
        ], Apply, Code)
    { Usings = ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Resampling", "Tachyon.Charts.Core.Series", "Tachyon.Charts.Core.Overlays", "Tachyon.Charts.Rendering"] };

    /// <summary>Gets the sample: the test file of the showroom page.</summary>
    public static SampleInfo Sample { get; } = new(
        SampleId,
        "A test file of floats, written once",
        DataShape.LargeFile,
        1_000_000_000,
        count => Open(PathOf(count), () => Write(PathOf(count), count)),
        "(FileColumn<float> Column, ColumnPyramid<float> Pyramid)",
        SampleCode)
    { MinimumCount = 1000, MaximumCount = 2_000_000_000 };

    /// <summary>Gets the reader of column files of one's own.</summary>
    public static DataLoader Loader { get; } = new(
        DataKind.File,
        DataShape.LargeFile,
        series => Open(series.Data.Path!, null),
        FileCode,
        series => series.Data.Path is { Length: > 0 } ? null : $"Choose the column file of {series.Name}.",
        _packages);

    /// <summary>Gets the path of the test file of a number of values.</summary>
    /// <param name="count">The number of values.</param>
    /// <returns>The path.</returns>
    public static string PathOf(int count) => Path.Combine(Folder, $"large-file-{count.ToString(CultureInfo.InvariantCulture)}.tcol");

    /// <summary>Gets the value at an index: the same formula as the showroom page.</summary>
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

    // Each file is opened once and stays open while the configurator runs; its pyramid is built once in the background.
    private static LargeFileData Open(string path, Action? write) => _open.GetOrAdd(Path.GetFullPath(path), full => new(() =>
    {
        if (write is not null && !File.Exists(full))
        {
            write();
        }

        var file = ColumnFile.Open(full);
        return file.Type switch
        {
            ColumnType.Single => Make<float>(file, full),
            ColumnType.Int16 => Make<short>(file, full),
            ColumnType.Int32 => Make<int>(file, full),
            _ => Make<double>(file, full),
        };
    })).Value;

    private static LargeFileData Make<T>(ColumnFile file, string path)
        where T : unmanaged, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
    {
        var column = file.Column<T>();
        var pyramid = ColumnPyramid<T>.Build(column, path + ".tpyr");
        return new LargeFileData(file.Type, column.Count, (x, y, thickness, color) =>
        {
            var line = new ColumnLineSeries<T>(column, pyramid, x, y) { Thickness = thickness };
            if (color is { } chosen)
            {
                line.Color = chosen;
            }

            return line;
        }, pyramid.Completion);
    }

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

    private static void Apply(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var file = (LargeFileData)data;
        var values = series.Parameters;
        chart.AddSeries(
            (x, y) =>
            {
                var line = file.Create(x, y, values.GetNumber("thickness", 1), SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? color : null);
                line.Name = series.Name;
                return line;
            },
            axes.X,
            axes.Y);
        if (values.GetFlag("refining", true))
        {
            chart.AddOverlay(surface => new RefiningOverlay(surface));
        }
    }

    private static string Code(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var type = series.Data.Kind == DataKind.File && series.Data.Path is { Length: > 0 } path && File.Exists(path) ? TypeName(Open(path, null).Type) : "float";
        var style = $"Name = {CodeText.String(series.Name)}, Thickness = {CodeText.Number(values.GetNumber("thickness", 1))}";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            style += $", Color = {CodeText.Color(color)}";
        }

        var code = $".AddSeries((x, y) => new ColumnLineSeries<{type}>({data}.Column, {data}.Pyramid, x, y) {{ {style} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
        return values.GetFlag("refining", true) ? code + ".AddOverlay(chart => new RefiningOverlay(chart))" : code;
    }

    private static string TypeName(ColumnType type) => type switch
    {
        ColumnType.Single => "float",
        ColumnType.Int16 => "short",
        ColumnType.Int32 => "int",
        _ => "double",
    };

    private static string SampleCode(string method) => SampleCatalog.Header("a test file of floats at one microsecond each, written once and kept", method, "(FileColumn<float> Column, ColumnPyramid<float> Pyramid)") + $$"""
            {
                // Beside the other local data; the file stays open while the application runs.
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "Samples");
                var path = Path.Combine(folder, $"large-file-{count}.tcol");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(folder);
                    var part = path + ".part";
                    using (var writer = new ColumnFileWriter<float>(part, 1, 0, 1e-6))
                    {
                        var values = new float[1 << 20];
                        for (long start = 0; start < count; start += values.Length)
                        {
                            var length = (int)Math.Min(values.Length, count - start);
                            Parallel.For(0, length, i =>
                            {
                                var index = start + i;
                                var slow = Math.Sin(2 * Math.PI * 3 * index / count);
                                var fast = 0.25 * Math.Sin(2 * Math.PI * index / 2_000_000.0);
                                var noise = (((ulong)index * 0x9E3779B97F4A7C15UL) >> 40) / (double)(1 << 24) - 0.5;
                                var spike = index % 97_654_321 == 12_345_678 ? 1.5 : 0;
                                values[i] = (float)(slow + fast + (0.15 * noise) + spike);
                            });
                            writer.Append(values.AsSpan(0, length));
                        }
                    }

                    File.Move(part, path, overwrite: true);
                }

                var column = ColumnFile.Open(path).Column<float>();
                return (column, ColumnPyramid<float>.Build(column, path + ".tpyr"));
            }

        """;

    private static DataCode FileCode(SeriesSpec series, string method)
    {
        var type = series.Data.Path is { Length: > 0 } path && File.Exists(path) ? TypeName(Open(path, null).Type) : "double";
        var returnType = $"(FileColumn<{type}> Column, ColumnPyramid<{type}> Pyramid)";
        return new(
            returnType,
            method,
            method + "()",
            $$"""

                    /// <summary>Opens the column file of {{System.Security.SecurityElement.Escape(series.Name)}}; it stays open while the application runs, its pyramid is kept beside it.</summary>
                    /// <returns>The column and its pyramid.</returns>
                    public static {{returnType}} {{method}}()
                    {
                        var path = {{CodeText.String(series.Data.Path ?? string.Empty)}};
                        var column = ColumnFile.Open(path).Column<{{type}}>();
                        return (column, ColumnPyramid<{{type}}>.Build(column, path + ".tpyr"));
                    }

                """,
            ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Resampling"]);
    }
}
