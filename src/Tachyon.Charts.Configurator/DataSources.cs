using System.Diagnostics;
using System.Globalization;
using PureHDF;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Configurator;

/// <summary>The data of a series in C#: its type, the call that makes it and the method behind the call.</summary>
/// <param name="Type">The C# type of the data.</param>
/// <param name="Method">The name of the method.</param>
/// <param name="Call">The call, such as <c>Sine(1000)</c>.</param>
/// <param name="Code">The method, indented by four spaces.</param>
/// <param name="Usings">The namespaces the method needs.</param>
internal sealed record DataCode(string Type, string Method, string Call, string Code, IReadOnlyList<string> Usings);

/// <summary>A reader of a module for a kind of source and a form of data, such as volumes from files in the 3D module.</summary>
/// <param name="Kind">The kind of source.</param>
/// <param name="Shape">The form of data it gives.</param>
/// <param name="Load">Reads the data for the preview.</param>
/// <param name="Code">Writes the same as a method of the given name.</param>
/// <param name="Problem">Names what the source misses, or <see langword="null"/>.</param>
/// <param name="Packages">The packages of the code: the name and whether it is one of Tachyon Charts.</param>
internal sealed record DataLoader(DataKind Kind, DataShape Shape, Func<SeriesSpec, object> Load, Func<SeriesSpec, string, DataCode> Code, Func<SeriesSpec, string?> Problem, IReadOnlyList<(string Name, bool IsOwn)> Packages);

/// <summary>A module that brings readers of data.</summary>
internal interface IDataLoaders
{
    /// <summary>Gets the readers.</summary>
    IReadOnlyList<DataLoader> Loaders { get; }
}

/// <summary>
/// The sources of data other than the generators: CSV files and live data. Each makes the same data for the preview
/// as the code it writes, in the form the type of the series asks for.
/// </summary>
internal static class DataSources
{
    /// <summary>The forms of data a CSV file can give, with the columns each takes after X.</summary>
    private static readonly Dictionary<DataShape, string> _csvShapes = new()
    {
        [DataShape.Points] = "one Y column",
        [DataShape.Values] = "one column",
        [DataShape.Rows] = "a column per row of bars",
        [DataShape.Groups] = "a column per group",
        [DataShape.Layers] = "a column per layer",
        [DataShape.Band] = "two Y columns",
        [DataShape.Bubbles] = "a Y and a size column",
        [DataShape.Errors] = "a Y, a lower and an upper error column",
        [DataShape.Points3D] = "a Y and a Z column",
    };

    /// <summary>Gets the kinds of source a type can take.</summary>
    /// <param name="type">The type.</param>
    /// <param name="catalog">The catalog, for the generators of live data.</param>
    /// <returns>The kinds, the generators first.</returns>
    public static IReadOnlyList<DataKind> Kinds(ChartTypeInfo type, ChartCatalog catalog)
    {
        var kinds = new List<DataKind> { DataKind.Sample };
        if (_csvShapes.ContainsKey(type.Shape) && type.SceneApply is null)
        {
            kinds.Add(DataKind.Csv);
        }

        if (type.Shape == DataShape.Points && catalog.Samples.Any(sample => sample.Curve is not null))
        {
            kinds.Add(DataKind.Live);
        }

        if (type.Shape is DataShape.Points or DataShape.Values or DataShape.Grid && type.SceneApply is null)
        {
            kinds.Add(DataKind.Hdf5);
        }

        foreach (var loader in catalog.Loaders)
        {
            if (loader.Shape == type.Shape && !kinds.Contains(loader.Kind))
            {
                kinds.Add(loader.Kind);
            }
        }

        return kinds;
    }

    /// <summary>Gets the reader of a module for the source of a series, if one reads it.</summary>
    /// <param name="series">The series.</param>
    /// <param name="type">Its type.</param>
    /// <param name="catalog">The catalog.</param>
    /// <returns>The reader, or <see langword="null"/>.</returns>
    public static DataLoader? Loader(SeriesSpec series, ChartTypeInfo type, ChartCatalog catalog) =>
        catalog.Loaders.FirstOrDefault(loader => loader.Kind == series.Data.Kind && loader.Shape == type.Shape);

    /// <summary>Gets what the columns of a CSV file mean for a form of data.</summary>
    /// <param name="shape">The form.</param>
    /// <returns>The text, or <see langword="null"/> when CSV does not fit.</returns>
    public static string? CsvColumns(DataShape shape) => _csvShapes.GetValueOrDefault(shape);

    /// <summary>Makes the data of a series from its source.</summary>
    /// <param name="series">The series.</param>
    /// <param name="type">Its type.</param>
    /// <param name="catalog">The catalog.</param>
    /// <returns>The data, of the type the samples of the shape have.</returns>
    /// <param name="running">Receives the timer of live data; without it live data stay empty.</param>
    public static object Load(SeriesSpec series, ChartTypeInfo type, ChartCatalog catalog, ICollection<IDisposable>? running) => Loader(series, type, catalog) is { } loader ? loader.Load(series) : series.Data.Kind switch
    {
        DataKind.Csv => FromCsv(series, type.Shape),
        DataKind.Live => Live(series, catalog, running),
        DataKind.Hdf5 => FromHdf5(series, type.Shape),
        _ => throw new InvalidOperationException($"The data of {series.Name} come from {series.Data.Kind}, which the configurator does not read yet."),
    };

    /// <summary>Writes the data of a series that are not made by a generator.</summary>
    /// <param name="series">The series.</param>
    /// <param name="type">Its type.</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="method">The name of the method.</param>
    /// <returns>The code.</returns>
    public static DataCode Code(SeriesSpec series, ChartTypeInfo type, ChartCatalog catalog, string method) => Loader(series, type, catalog) is { } loader ? loader.Code(series, method) : series.Data.Kind switch
    {
        DataKind.Csv => CsvCode(series, type, catalog, method),
        DataKind.Live => LiveCode(series, catalog, method),
        DataKind.Hdf5 => Hdf5Code(series, type, catalog, method),
        _ => throw new InvalidOperationException($"The data of {series.Name} come from {series.Data.Kind}; the code for such sources is not written yet."),
    };

    /// <summary>Gets the shared parts of the code of live data: the timers that keep running.</summary>
    /// <returns>The field, indented by four spaces.</returns>
    public static string LiveField() => """

            // The timers of the live data; kept here so that they keep running.
            private static readonly List<Timer> _timers = [];

        """;

    /// <summary>Names what a CSV source misses: its file or enough columns.</summary>
    /// <param name="series">The series.</param>
    /// <param name="type">Its type.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    public static string? CsvProblem(SeriesSpec series, ChartTypeInfo type) =>
        series.Data.Path is not { Length: > 0 } ? $"Choose the CSV file of {series.Name}."
        : Columns(series.Data.Options).Length < Needed(type.Shape) ? $"{series.Name} needs {_csvShapes[type.Shape]} from the CSV file; name them under Y columns."
        : null;

    private static CsvFormat Format(SpecValues options) =>
        new(Delimiter(options.GetText("delimiter", ",")), options.GetFlag("header", true), options.GetFlag("decimalComma", false));

    private static char Delimiter(string name) => name switch
    {
        "Semicolon" or ";" => ';',
        "Tab" => '\t',
        "Space" => ' ',
        _ => ',',
    };

    private static string[] Columns(SpecValues options) => options.GetText("y", string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // A column by its name, or by its number counted from 1.
    private static double[] Column(CsvTable table, string reference) =>
        int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? table.Column(number - 1) : table.Column(reference);

    private static string ColumnCode(string reference) =>
        int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? $"table.Column({number - 1})" : $"table.Column({CodeText.String(reference)})";

    // The X of a CSV source: a column, or the number of the row when none is given.
    private static double[] X(CsvTable table, SpecValues options) =>
        options.GetText("x", string.Empty) is { Length: > 0 } x ? Column(table, x) : [.. Enumerable.Range(0, table.RowCount).Select(row => (double)row)];

    private static string XCode(SpecValues options) =>
        options.GetText("x", string.Empty) is { Length: > 0 } x ? ColumnCode(x) : "[.. Enumerable.Range(0, table.RowCount).Select(row => (double)row)]";

    private static int Needed(DataShape shape) => shape switch
    {
        DataShape.Band or DataShape.Bubbles or DataShape.Points3D => 2,
        DataShape.Errors => 3,
        _ => 1,
    };

    private static object FromCsv(SeriesSpec series, DataShape shape)
    {
        var options = series.Data.Options;
        if (series.Data.Path is not { Length: > 0 } path)
        {
            throw new InvalidOperationException($"Choose the CSV file of {series.Name}.");
        }

        var table = CsvTable.Read(path, Format(options));
        var columns = Columns(options);
        if (columns.Length < Needed(shape))
        {
            throw new InvalidOperationException($"{series.Name} needs {_csvShapes[shape]} from the CSV file; name them under Y columns.");
        }

        var x = X(table, options);
        var ys = columns.Select(column => Column(table, column)).ToArray();
        return shape switch
        {
            DataShape.Points => Points(x, ys[0]),
            DataShape.Values => ys[0],
            DataShape.Rows or DataShape.Groups => ys,
            DataShape.Layers => ys.Select(y => Points(x, y)).ToArray(),
            DataShape.Band => (Points(x, ys[0]), Points(x, ys[1])),
            DataShape.Bubbles => (Points(x, ys[0]), ys[1].Select(size => (float)size).ToArray()),
            DataShape.Errors => (Points(x, ys[0]), ys[1], ys[2]),
            DataShape.Points3D => (x, ys[0], ys[1]),
            _ => throw new InvalidOperationException($"A CSV file gives no data for {shape}."),
        };
    }

    private static DataSource<double, double> Points(double[] x, double[] y)
    {
        var data = new DataSource<double, double>(Math.Max(1, x.Length));
        data.Append(x, y);
        return data;
    }

    private static DataCode CsvCode(SeriesSpec series, ChartTypeInfo type, ChartCatalog catalog, string method)
    {
        var (options, shape) = (series.Data.Options, type.Shape);
        var format = Format(options);
        var delimiter = format.Delimiter switch
        {
            '\t' => "'\\t'",
            var c => $"'{c}'",
        };
        var columns = Columns(options).Select(ColumnCode).ToArray();
        var body = shape switch
        {
            DataShape.Points => $"return Points({XCode(options)}, {columns[0]});",
            DataShape.Values => $"return {columns[0]};",
            DataShape.Rows or DataShape.Groups => $"return [{string.Join(", ", columns)}];",
            DataShape.Layers => $"var x = {XCode(options)};\n        return [{string.Join(", ", columns.Select(column => $"Points(x, {column})"))}];",
            DataShape.Band => $"var x = {XCode(options)};\n        return (Points(x, {columns[0]}), Points(x, {columns[1]}));",
            DataShape.Bubbles => $"return (Points({XCode(options)}, {columns[0]}), [.. {columns[1]}.Select(size => (float)size)]);",
            DataShape.Errors => $"return (Points({XCode(options)}, {columns[0]}), {columns[1]}, {columns[2]});",
            DataShape.Points3D => $"return ({XCode(options)}, {columns[0]}, {columns[1]});",
            _ => throw new InvalidOperationException($"A CSV file gives no data for {shape}."),
        };
        var returnType = catalog.FindSample(type.DefaultSample)!.ReturnType;
        var code = $$"""

                /// <summary>Reads the data of {{System.Security.SecurityElement.Escape(series.Name)}} from a CSV file.</summary>
                /// <returns>The data.</returns>
                public static {{returnType}} {{method}}()
                {
                    var table = CsvTable.Read({{CodeText.String(series.Data.Path ?? string.Empty)}}, new CsvFormat({{delimiter}}, HasHeader: {{CodeText.Flag(format.HasHeader)}}, DecimalComma: {{CodeText.Flag(format.DecimalComma)}}));
                    {{body}}
                }

            """;
        return new DataCode(returnType, method, method + "()", code, ["Tachyon.Charts.Core.Data"]);
    }

    /// <summary>Lists the datasets of numbers in an HDF5 file, with their paths.</summary>
    /// <param name="path">The file, or <see langword="null"/>.</param>
    /// <returns>The paths, such as <c>/group/pressure</c>; empty when the file cannot be read.</returns>
    public static IReadOnlyList<string> Hdf5Datasets(string? path)
    {
        if (path is not { Length: > 0 } || !File.Exists(path))
        {
            return [];
        }

        try
        {
            using var file = H5File.OpenRead(path);
            var found = new List<string>();
            Collect(file, string.Empty, found);
            return found;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException or FormatException)
        {
            return [];
        }
    }

    private static void Collect(IH5Group group, string prefix, List<string> found)
    {
        foreach (var child in group.Children())
        {
            var name = prefix + "/" + child.Name;
            if (child is IH5Dataset dataset && dataset.Type.Class is H5DataTypeClass.FixedPoint or H5DataTypeClass.FloatingPoint)
            {
                found.Add(name);
            }
            else if (child is IH5Group inner)
            {
                Collect(inner, name, found);
            }
        }
    }

    /// <summary>Names what an HDF5 source misses: its file or its dataset.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    public static string? Hdf5Problem(SeriesSpec series) =>
        series.Data.Path is not { Length: > 0 } ? $"Choose the HDF5 file of {series.Name}."
        : series.Data.Options.GetText("dataset", string.Empty).Length == 0 ? $"Name the dataset of {series.Name} in the HDF5 file, such as /pressure."
        : null;

    /// <summary>Reads the numbers of a dataset of any integer or floating point type, in the order of its elements.</summary>
    /// <param name="dataset">The dataset.</param>
    /// <returns>The numbers.</returns>
    /// <exception cref="NotSupportedException">The dataset holds no numbers.</exception>
    public static double[] Numbers(IH5Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var type = dataset.Type;
        var signed = type.Class != H5DataTypeClass.FixedPoint || type.FixedPoint.IsSigned;
        return (type.Class, type.Size, signed) switch
        {
            (H5DataTypeClass.FloatingPoint, 4, _) => [.. dataset.Read<float[]>().Select(value => (double)value)],
            (H5DataTypeClass.FloatingPoint, _, _) => dataset.Read<double[]>(),
            (H5DataTypeClass.FixedPoint, 1, true) => [.. dataset.Read<sbyte[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, 1, false) => [.. dataset.Read<byte[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, 2, true) => [.. dataset.Read<short[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, 2, false) => [.. dataset.Read<ushort[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, 4, true) => [.. dataset.Read<int[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, 4, false) => [.. dataset.Read<uint[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, _, true) => [.. dataset.Read<long[]>().Select(value => (double)value)],
            (H5DataTypeClass.FixedPoint, _, false) => [.. dataset.Read<ulong[]>().Select(value => (double)value)],
            _ => throw new NotSupportedException($"The dataset holds {type.Class}, not numbers."),
        };
    }

    /// <summary>Writes <see cref="Numbers"/> for the generated code.</summary>
    /// <returns>The method, indented by four spaces.</returns>
    public static string NumbersHelper() => """

            // The numbers of a dataset of any integer or floating point type, in the order of its elements.
            private static double[] Numbers(IH5Dataset dataset)
            {
                var type = dataset.Type;
                var signed = type.Class != H5DataTypeClass.FixedPoint || type.FixedPoint.IsSigned;
                return (type.Class, type.Size, signed) switch
                {
                    (H5DataTypeClass.FloatingPoint, 4, _) => [.. dataset.Read<float[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FloatingPoint, _, _) => dataset.Read<double[]>(),
                    (H5DataTypeClass.FixedPoint, 1, true) => [.. dataset.Read<sbyte[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, 1, false) => [.. dataset.Read<byte[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, 2, true) => [.. dataset.Read<short[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, 2, false) => [.. dataset.Read<ushort[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, 4, true) => [.. dataset.Read<int[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, 4, false) => [.. dataset.Read<uint[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, _, true) => [.. dataset.Read<long[]>().Select(value => (double)value)],
                    (H5DataTypeClass.FixedPoint, _, false) => [.. dataset.Read<ulong[]>().Select(value => (double)value)],
                    _ => throw new NotSupportedException($"The dataset holds {type.Class}, not numbers."),
                };
            }

        """;

    // Curves from one or two datasets, values from one, a grid from a dataset of two dimensions (rows, then columns).
    private static object FromHdf5(SeriesSpec series, DataShape shape)
    {
        if (Hdf5Problem(series) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        using var file = H5File.OpenRead(series.Data.Path!);
        var options = series.Data.Options;
        var dataset = file.Dataset(options.GetText("dataset", string.Empty));
        var values = Numbers(dataset);
        switch (shape)
        {
            case DataShape.Points:
                var x = options.GetText("xDataset", string.Empty) is { Length: > 0 } xName ? Numbers(file.Dataset(xName)) : [.. Enumerable.Range(0, values.Length).Select(i => (double)i)];
                return Points(x, values);
            case DataShape.Values:
                return values;
            default:
                var dimensions = dataset.Space.Dimensions;
                if (dimensions.Length != 2)
                {
                    throw new InvalidOperationException($"A grid needs a dataset of two dimensions; {options.GetText("dataset", string.Empty)} has {dimensions.Length}.");
                }

                var grid = new HeatmapData((int)dimensions[1], (int)dimensions[0]);
                grid.SetValues([.. values.Select(value => (float)value)]);
                return grid;
        }
    }

    private static DataCode Hdf5Code(SeriesSpec series, ChartTypeInfo type, ChartCatalog catalog, string method)
    {
        if (Hdf5Problem(series) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var options = series.Data.Options;
        var dataset = CodeText.String(options.GetText("dataset", string.Empty));
        var body = type.Shape switch
        {
            DataShape.Points => options.GetText("xDataset", string.Empty) is { Length: > 0 } xName
                ? $"return Points(Numbers(file.Dataset({CodeText.String(xName)})), Numbers(file.Dataset({dataset})));"
                : $"var values = Numbers(file.Dataset({dataset}));\n        return Points([.. Enumerable.Range(0, values.Length).Select(i => (double)i)], values);",
            DataShape.Values => $"return Numbers(file.Dataset({dataset}));",
            _ => $"var dataset = file.Dataset({dataset});\n        var dimensions = dataset.Space.Dimensions;\n        var grid = new HeatmapData((int)dimensions[1], (int)dimensions[0]);\n        grid.SetValues([.. Numbers(dataset).Select(value => (float)value)]);\n        return grid;",
        };
        var returnType = catalog.FindSample(type.DefaultSample)!.ReturnType;
        var code = $$"""

                /// <summary>Reads the data of {{System.Security.SecurityElement.Escape(series.Name)}} from an HDF5 file.</summary>
                /// <returns>The data.</returns>
                public static {{returnType}} {{method}}()
                {
                    using var file = H5File.OpenRead({{CodeText.String(series.Data.Path!)}});
                    {{body}}
                }

            """;
        return new DataCode(returnType, method, method + "()", code, ["Tachyon.Charts.Core.Data", "PureHDF"]);
    }

    /// <summary>Writes the helper that makes points from two columns.</summary>
    /// <returns>The method, indented by four spaces.</returns>
    public static string PointsHelper() => """

            private static DataSource<double, double> Points(double[] x, double[] y)
            {
                var data = new DataSource<double, double>(Math.Max(1, x.Length));
                data.Append(x, y);
                return data;
            }

        """;

    // Live data: the curve of a generator, appended at a rate into a FIFO that keeps the given number of points.
    private static object Live(SeriesSpec series, ChartCatalog catalog, ICollection<IDisposable>? running)
    {
        var curve = catalog.FindSample(series.Data.Sample)?.Curve ?? throw new InvalidOperationException($"Live data of {series.Name} need a curve as sample data.");
        var data = new FifoDataSource<double, double>(Math.Max(2, series.Data.Count), singleProducer: true);
        running?.Add(LiveFeeds.Start(data, curve, Rate(series)));
        return data;
    }

    private static double Rate(SeriesSpec series) => Math.Max(1, series.Data.Options.GetNumber("rate", 200));

    private static DataCode LiveCode(SeriesSpec series, ChartCatalog catalog, string method)
    {
        var sample = catalog.FindSample(series.Data.Sample)!;
        var rate = CodeText.Number(Rate(series));
        var code = $$"""

                /// <summary>Makes live data: {{System.Security.SecurityElement.Escape(sample.Name.ToLowerInvariant())}}, {{rate}} points per second, the newest kept.</summary>
                /// <param name="count">The number of points kept.</param>
                /// <returns>The data, which keep growing.</returns>
                public static FifoDataSource<double, double> {{method}}(int count)
                {
                    var data = new FifoDataSource<double, double>(count, singleProducer: true);
                    var clock = Stopwatch.StartNew();
                    var written = 0L;
                    _timers.Add(new Timer(_ =>
                    {
                        // Every point due since the last tick; the timer may come late, the points do not.
                        lock (data)
                        {
                            for (var due = (long)(clock.Elapsed.TotalSeconds * {{rate}}); written < due; written++)
                            {
                                var x = written / {{rate}};
                                data.Append(x, {{sample.CurveCode}});
                            }
                        }
                    }, null, 0, 15));
                    return data;
                }

            """;
        return new DataCode("FifoDataSource<double, double>", method, $"{method}({Math.Max(2, series.Data.Count).ToString(CultureInfo.InvariantCulture)})", code, ["Tachyon.Charts.Core.Data", "System.Diagnostics"]);
    }
}

/// <summary>The live data of a preview: a timer that appends to the data. Appending a point allocates nothing.</summary>
internal sealed class LiveFeeds
{
    private readonly FifoDataSource<double, double> _data;
    private readonly Func<double, double> _curve;
    private readonly double _rate;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _written;

    private LiveFeeds(FifoDataSource<double, double> data, Func<double, double> curve, double rate) => (_data, _curve, _rate) = (data, curve, rate);

    /// <summary>Starts appending the curve to the data.</summary>
    /// <param name="data">The data.</param>
    /// <param name="curve">The value for each X.</param>
    /// <param name="rate">The points per second.</param>
    /// <returns>The timer; disposing it stops the data.</returns>
    public static IDisposable Start(FifoDataSource<double, double> data, Func<double, double> curve, double rate)
    {
        var feed = new LiveFeeds(data, curve, rate);
        return new Timer(static state => ((LiveFeeds)state!).Tick(), feed, 0, 15);
    }

    /// <summary>Creates a feed without a timer, to drive it by hand.</summary>
    /// <param name="data">The data.</param>
    /// <param name="curve">The value for each X.</param>
    /// <param name="rate">The points per second.</param>
    /// <returns>The feed.</returns>
    internal static LiveFeeds Create(FifoDataSource<double, double> data, Func<double, double> curve, double rate) => new(data, curve, rate);

    /// <summary>Appends every point due since the last tick; the timer may come late, the points do not.</summary>
    internal void Tick()
    {
        lock (_data)
        {
            for (var due = (long)(_clock.Elapsed.TotalSeconds * _rate); _written < due; _written++)
            {
                var x = _written / _rate;
                _data.Append(x, _curve(x));
            }
        }
    }
}
