using System.Diagnostics;
using System.Globalization;
using Tachyon.Charts.Core.Fluent;

namespace Tachyon.Charts.Configurator;

/// <summary>An editable field of the configurator, built from the metadata; the apps turn it into a control.</summary>
public sealed class FieldModel
{
    private readonly Func<object> _read;
    private readonly Action<object> _write;

    internal FieldModel(string group, ParameterInfo info, Func<object> read, Action<object> write)
    {
        (Group, Info, _read, _write) = (group, info, read, write);
    }

    /// <summary>Gets the heading the field belongs to.</summary>
    public string Group { get; }

    /// <summary>Gets what the field holds.</summary>
    public ParameterInfo Info { get; }

    /// <summary>Gets the current value: a <see cref="double"/>, <see cref="bool"/> or <see cref="string"/>.</summary>
    public object Value => _read();

    /// <summary>Sets a value from the control.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A sentence on what is wrong, or <see langword="null"/> when the value was taken.</returns>
    public string? Set(object value)
    {
        if (Info.Check(value) is { } problem)
        {
            return problem;
        }

        // Controls may report the value they were given; that is no change.
        if (!Equals(value, Value))
        {
            _write(value);
        }

        return null;
    }

    /// <summary>Sets a value from text, as typed into a box.</summary>
    /// <param name="text">The text; numbers with a point or a comma.</param>
    /// <returns>A sentence on what is wrong, or <see langword="null"/> when the value was taken.</returns>
    public string? SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Info.Kind is not (ParameterKind.Number or ParameterKind.Integer))
        {
            return Set(text);
        }

        return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Set(number)
            : $"{Info.Label} needs a number.";
    }

    /// <summary>Gets the value as text for a box.</summary>
    public string Text => Value switch
    {
        double number => number.ToString("0.######", CultureInfo.InvariantCulture),
        bool flag => flag ? "yes" : "no",
        var other => other.ToString() ?? string.Empty,
    };
}

/// <summary>
/// The state of a configurator window: the description being edited, the selected series, the fields of the settings
/// and the code. Every change rebuilds code and preview from the same description and raises <see cref="Changed"/>.
/// </summary>
public sealed class ConfiguratorSession
{
    private readonly List<IDisposable> _running = [];

    private readonly CodeGenerator _generator;
    private ChartSpec _spec;
    private int _selected;

    /// <summary>Initializes a session with a line chart.</summary>
    /// <param name="catalog">The types, samples and interactions.</param>
    public ConfiguratorSession(ChartCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _generator = new CodeGenerator(catalog);
        _spec = catalog.CreateSpec(catalog.Types[0].Id);
        Refresh();
    }

    /// <summary>Occurs after every change, on the thread that made it.</summary>
    public event Action? Changed;

    /// <summary>Occurs when fields come or go, so that the settings are built anew; a changed value alone does not raise it.</summary>
    public event Action? FieldsChanged;

    /// <summary>Gets the types, samples and interactions.</summary>
    public ChartCatalog Catalog { get; }

    /// <summary>Gets the description being edited.</summary>
    public ChartSpec Spec => _spec;

    /// <summary>Gets or sets the options of the code.</summary>
    public CodeOptions Options
    {
        get;
        set
        {
            field = value ?? throw new ArgumentNullException(nameof(value));
            Refresh();
        }
    } = new();

    /// <summary>Gets or sets the index of the series whose fields show.</summary>
    public int SelectedSeries
    {
        get => _selected;
        set
        {
            _selected = Math.Clamp(value, 0, Math.Max(0, _spec.Series.Count - 1));
            FieldsChanged?.Invoke();
        }
    }

    /// <summary>Gets the code, by the name of its tab: C#, view model, window, code behind and project file.</summary>
    public IReadOnlyDictionary<string, string> Code { get; private set; } = new Dictionary<string, string>();

    /// <summary>Gets what is wrong with the description now; empty when the preview and the code are up to date.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = [];

    /// <summary>Gets how long the last change took to describe the chart and write the code.</summary>
    public TimeSpan LastUpdate { get; private set; }

    /// <summary>Gets the fields of the whole chart: title, theme, X downwards, group and legend.</summary>
    public IReadOnlyList<FieldModel> ChartFields =>
    [
        .. ChartOptionFields,
        .. Catalog.SceneOf(_spec) is { } scene
            ? scene.Parameters.Select(parameter => Field("Scene", parameter, () => _spec.Scene.Get(parameter.Name) ?? parameter.Default, value => SetValue(_spec.Scene, parameter.Name, value)))
            : [],
    ];

    private IReadOnlyList<FieldModel> ChartOptionFields =>
    [
        Field("Chart", new("title", "Title", ParameterKind.Text, string.Empty), () => _spec.Title, value => _spec.Title = (string)value),
        Field("Chart", new("theme", "Theme", ParameterKind.Choice, "Application") { Choices = ["Application", .. ChartCatalog.Themes] }, () => _spec.Theme ?? "Application", value => _spec.Theme = (string)value == "Application" ? null : (string)value),
        Field("Chart", new("xDown", "X axis downwards", ParameterKind.Flag, false), () => _spec.XDown, value => _spec.XDown = (bool)value),
        Field("Chart", new("legend", "Legend", ParameterKind.Flag, false), () => _spec.Legend is not null, value => _spec.Legend = (bool)value ? _spec.Legend ?? new LegendSpec() : null, structural: true),
        .. LegendFields("Legend", _spec.Legend),
        Field("Chart", new("secondLegend", "Second legend", ParameterKind.Flag, false) { Description = "For example for the series of a second axis." }, () => _spec.SecondLegend is not null, value => _spec.SecondLegend = (bool)value ? _spec.SecondLegend ?? new LegendSpec { Placement = "BottomRight" } : null, structural: true),
        .. LegendFields("Second legend", _spec.SecondLegend),
        Field("Chart", new("export", "Export menu", ParameterKind.Flag, false) { Description = "In the generated window a right click on the chart saves it as PNG, SVG or PDF, its data as CSV, or records a video." }, () => _spec.ExportMenu, value => _spec.ExportMenu = (bool)value),
        Field("Chart", new("group", "Group", ParameterKind.Text, string.Empty) { Description = "Charts of one group share range and cursor." }, () => _spec.Group ?? string.Empty, value => _spec.Group = ((string)value).Length > 0 ? (string)value : null),
    ];

    /// <summary>Gets the fields of the axes.</summary>
    public IReadOnlyList<FieldModel> AxisFields
    {
        get
        {
            var fields = new List<FieldModel>();
            if (Catalog.OwnsAxes(_spec))
            {
                return fields;
            }

            foreach (var (axes, name) in new[] { (_spec.XAxes, "X axis"), (_spec.YAxes, "Y axis") })
            {
                foreach (var axis in axes)
                {
                    var group = $"{name} {axis.Id}";
                    fields.Add(Field(group, new("title", "Title", ParameterKind.Text, string.Empty), () => axis.Title, value => axis.Title = (string)value));
                    fields.Add(Field(group, new("kind", "Scale", ParameterKind.Choice, nameof(AxisKind.Numeric)) { Choices = Enum.GetNames<AxisKind>() }, () => axis.Kind.ToString(), value => axis.Kind = Enum.Parse<AxisKind>((string)value), structural: true));
                    fields.Add(Field(group, new("auto", "Follow the data", ParameterKind.Flag, true), () => axis.IsAutoRange, value => (axis.Minimum, axis.Maximum) = (bool)value ? ((double?)null, (double?)null) : (axis.Minimum ?? 0, axis.Maximum ?? 10)));
                    fields.Add(Field(group, new("minimum", "Minimum", ParameterKind.Number, 0.0), () => axis.Minimum ?? 0, value => (axis.Minimum, axis.Maximum) = ((double)value, axis.Maximum ?? 10)));
                    fields.Add(Field(group, new("maximum", "Maximum", ParameterKind.Number, 10.0), () => axis.Maximum ?? 10, value => (axis.Minimum, axis.Maximum) = (axis.Minimum ?? 0, (double)value)));
                    fields.Add(Field(group, new("inverted", "Inverted", ParameterKind.Flag, false), () => axis.IsInverted, value => axis.IsInverted = (bool)value));
                    fields.Add(Field(group, new("opposite", "Far side", ParameterKind.Flag, false), () => axis.IsOpposite, value => axis.IsOpposite = (bool)value));
                    if (axis.Kind == AxisKind.DateTime)
                    {
                        fields.Add(Field(group, new("weekends", "Without weekends", ParameterKind.Flag, false), () => axis.SkipsWeekends, value => axis.SkipsWeekends = (bool)value));
                    }

                    if (axis.Kind == AxisKind.Category)
                    {
                        fields.Add(Field(group, new("categories", "Names of the places", ParameterKind.Text, string.Empty) { Description = "One per place 0, 1, 2 …, separated by commas." }, () => axis.Categories, value => axis.Categories = (string)value));
                    }

                    fields.Add(Field(group, new("origin", "Through the origin", ParameterKind.Flag, false) { Description = "The axis runs where the other axis is 0." }, () => axis.CrossesZero, value => axis.CrossesZero = (bool)value));
                    fields.Add(Field(group, new("breaks", "Scale breaks", ParameterKind.Text, string.Empty) { Description = "Ranges left out, such as 10..90, separated by commas." }, () => axis.Breaks, value => axis.Breaks = (string)value));
                    fields.Add(Field(group, new("hidden", "Hidden", ParameterKind.Flag, false), () => axis.IsHidden, value => axis.IsHidden = (bool)value));
                    if (name == "Y axis")
                    {
                        fields.Add(Field(group, new("pane", "Pane", ParameterKind.Integer, 0.0) { Minimum = 0, Maximum = 9, Description = "Panes stack from 0 at the top and share the horizontal axis." }, () => (double)axis.Pane, value => axis.Pane = (int)(double)value));
                        fields.Add(Field(group, new("paneWeight", "Height of the pane", ParameterKind.Number, 1.0) { Minimum = 0.05, Maximum = 20, Description = "Its share against the other panes." }, () => axis.PaneWeight, value => axis.PaneWeight = (double)value));
                    }
                }
            }

            return fields;
        }
    }

    /// <summary>Gets the fields of the selected series: name, data and the parameters of its type.</summary>
    public IReadOnlyList<FieldModel> SeriesFields
    {
        get
        {
            if (_spec.Series.Count == 0)
            {
                return [];
            }

            var series = _spec.Series[_selected];
            var type = Catalog.FindType(series.Type)!;
            var group = $"{type.Name} {_selected + 1}";
            var samples = Catalog.Samples.Where(sample => sample.Shape == type.Shape).Select(sample => sample.Id).ToArray();
            var fields = new List<FieldModel>
            {
                Field(group, new("name", "Name", ParameterKind.Text, string.Empty), () => series.Name, value => series.Name = (string)value),
            };
            fields.AddRange(DataFields(group, series, type, samples));
            if (_spec.YAxes.Count > 1)
            {
                fields.Add(Field(group, new("yAxis", "Y axis", ParameterKind.Choice, _spec.YAxes[0].Id) { Choices = [.. _spec.YAxes.Select(axis => axis.Id)] }, () => series.YAxis ?? _spec.YAxes[0].Id, value => series.YAxis = (string)value));
            }

            foreach (var parameter in type.Parameters)
            {
                fields.Add(Field(group, parameter, () => series.Parameters.Get(parameter.Name) ?? parameter.Default, value => SetValue(series.Parameters, parameter.Name, value)));
            }

            return fields;
        }
    }

    /// <summary>Gets the fields of the interactions: one switch each, followed by its parameters while it is on.</summary>
    public IReadOnlyList<FieldModel> ModifierFields
    {
        get
        {
            var fields = new List<FieldModel>();
            foreach (var info in Catalog.Modifiers)
            {
                var spec = _spec.Modifiers.FirstOrDefault(modifier => modifier.Type == info.Id);
                fields.Add(Field("Interaction", new(info.Id, info.Name, ParameterKind.Flag, false), () => spec is not null, value => Toggle(info, (bool)value), structural: true));
                if (spec is null)
                {
                    continue;
                }

                foreach (var parameter in info.Parameters)
                {
                    fields.Add(Field("Interaction", parameter with { Label = "   " + parameter.Label }, () => spec.Parameters.Get(parameter.Name) ?? parameter.Default, value => SetValue(spec.Parameters, parameter.Name, value)));
                }
            }

            return fields;
        }
    }

    /// <summary>Starts a new chart with one series of a type.</summary>
    /// <param name="typeId">The type.</param>
    public void NewChart(string typeId)
    {
        _spec = Catalog.CreateSpec(typeId);
        _selected = 0;
        Structure();
    }

    /// <summary>Replaces the description, for example by one read from a file.</summary>
    /// <param name="spec">The description.</param>
    public void Load(ChartSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _selected = 0;
        Structure();
    }

    /// <summary>Adds a series of a type with its sample data and selects it.</summary>
    /// <param name="typeId">The type.</param>
    public void AddSeries(string typeId)
    {
        var added = Catalog.CreateSpec(typeId).Series[0];
        added.Name = Unique(added.Name);

        // One volume pane per chart: further candles start without one.
        if (typeId == "candles" && _spec.Series.Exists(series => series.Type == "candles" && series.Parameters.GetFlag("volume", true)))
        {
            added.Parameters.Set("volume", false);
        }

        _spec.Series.Add(added);
        _selected = _spec.Series.Count - 1;
        Structure();
    }

    /// <summary>Removes the selected series; the last one stays.</summary>
    public void RemoveSeries()
    {
        if (_spec.Series.Count > 1)
        {
            _spec.Series.RemoveAt(_selected);
            _selected = Math.Min(_selected, _spec.Series.Count - 1);
            Structure();
        }
    }

    /// <summary>Adds a second Y axis on the far side, which series can choose.</summary>
    public void AddYAxis()
    {
        var id = "y" + (_spec.YAxes.Count + 1).ToString(CultureInfo.InvariantCulture);
        _spec.YAxes.Add(new AxisSpec { Id = id, Title = id, IsOpposite = true });
        Structure();
    }

    /// <summary>Describes the chart of the preview.</summary>
    /// <param name="chart">The chart description.</param>
    public void Apply(ChartBuilder chart)
    {
        // The live data of the preview before stop; this one starts its own.
        foreach (var running in _running)
        {
            running.Dispose();
        }

        _running.Clear();
        SpecChartBuilder.Apply(_spec, Catalog, chart, _running);
    }

    private static void SetValue(SpecValues values, string name, object value)
    {
        switch (value)
        {
            case double number:
                values.Set(name, number);
                break;
            case bool flag:
                values.Set(name, flag);
                break;
            default:
                values.Set(name, (string)value);
                break;
        }
    }

    /// <summary>Gets the fields of the annotations, one group each.</summary>
    public IReadOnlyList<FieldModel> AnnotationFields
    {
        get
        {
            var fields = new List<FieldModel>();
            for (var i = 0; i < _spec.Annotations.Count; i++)
            {
                var annotation = _spec.Annotations[i];
                var info = ChartCatalog.FindAnnotation(annotation.Type)!;
                var group = $"Annotation {i + 1} · {info.Name}";
                foreach (var parameter in info.Parameters)
                {
                    fields.Add(Field(group, parameter, () => annotation.Parameters.Get(parameter.Name) ?? parameter.Default, value => SetValue(annotation.Parameters, parameter.Name, value)));
                }
            }

            return fields;
        }
    }

    /// <summary>Gets the kinds of annotations, by identifier and name.</summary>
    public static IReadOnlyList<(string Id, string Name)> AnnotationKinds { get; } = [.. AnnotationCatalog.All.Select(info => (info.Id, info.Name))];

    /// <summary>Adds an annotation of a kind.</summary>
    /// <param name="kind">The identifier of the kind.</param>
    public void AddAnnotation(string kind)
    {
        _spec.Annotations.Add(new AnnotationSpec { Type = kind });
        Structure();
    }

    /// <summary>Removes the last annotation.</summary>
    public void RemoveAnnotation()
    {
        if (_spec.Annotations.Count > 0)
        {
            _spec.Annotations.RemoveAt(_spec.Annotations.Count - 1);
            Structure();
        }
    }

    // Where the data come from, then what that source needs: a generator and a count, a CSV file and its columns, or a
    // curve with the rate of live data.
    private List<FieldModel> DataFields(string group, SeriesSpec series, ChartTypeInfo type, string[] samples)
    {
        var data = series.Data;
        var options = data.Options;
        var kinds = DataSources.Kinds(type, Catalog);
        var fields = new List<FieldModel>();
        if (kinds.Count > 1)
        {
            fields.Add(Field(group, new("source", "Data from", ParameterKind.Choice, nameof(DataKind.Sample)) { Choices = [.. kinds.Select(kind => kind.ToString())] }, () => data.Kind.ToString(), value =>
            {
                data.Kind = Enum.Parse<DataKind>((string)value);
                if (data.Kind == DataKind.Live && Catalog.FindSample(data.Sample)?.Curve is null)
                {
                    data.Sample = Catalog.Samples.First(sample => sample.Curve is not null).Id;
                }
            }, structural: true));
        }

        switch (data.Kind)
        {
            case DataKind.Csv:
                CsvFields(group, type, data, fields);
                break;
            case DataKind.Hdf5:
                // The datasets of a readable file to choose from, with their dimensions; otherwise a path to type.
                fields.Add(Field(group, new("file", "HDF5 file", ParameterKind.File, string.Empty), () => data.Path ?? string.Empty, value => data.Path = (string)value, structural: true));
                var datasets = DataSources.Hdf5Datasets(data.Path);
                fields.Add(Field(group, datasets.Count > 0
                    ? new("dataset", "Dataset", ParameterKind.Choice, datasets[0]) { Choices = datasets }
                    : new("dataset", "Dataset", ParameterKind.Text, string.Empty) { Description = "Its path in the file, such as /pressure: one dimension for curves and values, two for grids, three for volumes." }, () => options.GetText("dataset", string.Empty), value => SetValue(options, "dataset", value)));
                if (type.Shape == DataShape.Points)
                {
                    fields.Add(Field(group, datasets.Count > 0
                        ? new("xDataset", "X dataset", ParameterKind.Choice, string.Empty) { Choices = ["", .. datasets], Description = "Empty for the number of each value." }
                        : new("xDataset", "X dataset", ParameterKind.Text, string.Empty) { Description = "Empty for the number of each value." }, () => options.GetText("xDataset", string.Empty), value => SetValue(options, "xDataset", value)));
                }

                break;
            case DataKind.File:
                fields.Add(Field(group, new("file", "File", ParameterKind.File, string.Empty) { Description = "Points: XYZ, PLY or LAS; volumes: NIfTI or raw; models: STL or OBJ." }, () => data.Path ?? string.Empty, value => data.Path = (string)value));
                break;
            case DataKind.Live:
                fields.Add(Field(group, new("curve", "Curve", ParameterKind.Choice, data.Sample) { Choices = [.. Catalog.Samples.Where(sample => sample.Curve is not null).Select(sample => sample.Id)] }, () => data.Sample, value => data.Sample = (string)value));
                fields.Add(Field(group, new("rate", "Points per second", ParameterKind.Number, 200.0) { Minimum = 1, Maximum = 1_000_000 }, () => options.GetNumber("rate", 200), value => SetValue(options, "rate", value)));
                fields.Add(Field(group, new("count", "Points kept", ParameterKind.Integer, 1000.0) { Minimum = 2, Maximum = 10_000_000 }, () => (double)data.Count, value => data.Count = (int)(double)value));
                break;
            default:
                fields.Add(Field(group, new("sample", "Sample data", ParameterKind.Choice, type.DefaultSample) { Choices = samples }, () => data.Sample, value => data.Sample = (string)value));
                fields.Add(Field(group, new("count", "Number of values", ParameterKind.Integer, 1000.0) { Minimum = Catalog.FindSample(data.Sample)?.MinimumCount ?? 2, Maximum = 10_000_000 }, () => (double)data.Count, value => data.Count = (int)(double)value));
                break;
        }

        return fields;
    }

    private IEnumerable<FieldModel> LegendFields(string name, LegendSpec? legend) => legend is null ? [] :
    [
        Field("Chart", new(name + "Title", "   Title", ParameterKind.Text, string.Empty), () => legend.Title, value => legend.Title = (string)value),
        Field("Chart", new(name + "Placement", "   Place", ParameterKind.Choice, "TopRight") { Choices = Enum.GetNames<Core.Input.LegendPlacement>() }, () => legend.Placement, value => legend.Placement = (string)value),
        Field("Chart", new(name + "Series", "   Series", ParameterKind.Text, string.Empty) { Description = "The names of the series it lists, separated by commas; empty for all." }, () => legend.Series, value => legend.Series = (string)value),
    ];

    private const string OwnFile = "(own file)";

    private const string RowNumber = "(number of the row)";

    // The fields of a CSV source: an example or an own file, its format, and its columns to choose from.
    private void CsvFields(string group, ChartTypeInfo type, DataSpec data, List<FieldModel> fields)
    {
        var options = data.Options;
        var examples = DataSources.CsvExamples;
        if (examples.Count > 0)
        {
            fields.Add(Field(group, new("example", "Example file", ParameterKind.Choice, OwnFile) { Choices = [OwnFile, .. examples.Select(Path.GetFileName).OfType<string>()], Description = "Files that come with the configurator." }, () => examples.FirstOrDefault(example => data.Path is { Length: > 0 } path && string.Equals(example, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) is { } chosen ? Path.GetFileName(chosen) : OwnFile, value =>
            {
                if (examples.FirstOrDefault(example => Path.GetFileName(example) == (string)value) is { } chosen)
                {
                    UseCsv(type, data, chosen);
                }
            }, structural: true));
        }

        fields.Add(Field(group, new("file", "CSV file", ParameterKind.File, string.Empty), () => data.Path ?? string.Empty, value => UseCsv(type, data, (string)value), structural: true));
        fields.Add(Field(group, new("delimiter", "Delimiter", ParameterKind.Choice, "Comma") { Choices = ["Comma", "Semicolon", "Tab", "Space"] }, () => options.GetText("delimiter", "Comma"), value => SetValue(options, "delimiter", value), structural: true));
        fields.Add(Field(group, new("header", "First line names the columns", ParameterKind.Flag, true), () => options.GetFlag("header", true), value => SetValue(options, "header", value), structural: true));
        fields.Add(Field(group, new("decimalComma", "Decimal comma", ParameterKind.Flag, false) { Description = "Numbers like 3,14 instead of 3.14." }, () => options.GetFlag("decimalComma", false), value => SetValue(options, "decimalComma", value)));

        // Without a readable file the columns are typed: names or numbers from 1.
        var names = DataSources.CsvNames(data.Path, options);
        if (names.Count == 0)
        {
            fields.Add(Field(group, new("x", "X column", ParameterKind.Text, string.Empty) { Description = "Its name or number from 1; empty for the number of the row." }, () => options.GetText("x", string.Empty), value => SetValue(options, "x", value)));
            fields.Add(Field(group, new("y", "Y columns", ParameterKind.Text, string.Empty) { Description = "Names or numbers from 1, separated by commas: " + DataSources.CsvColumns(type.Shape) + "." }, () => options.GetText("y", string.Empty), value => SetValue(options, "y", value)));
            return;
        }

        // A name with a comma is chosen by its number, as the chosen columns are kept separated by commas.
        var choices = names.Select((name, index) => name.Contains(',', StringComparison.Ordinal) ? (index + 1).ToString(CultureInfo.InvariantCulture) : name).ToArray();
        if (type.Shape is not (DataShape.Values or DataShape.Rows or DataShape.Groups))
        {
            fields.Add(Field(group, new("x", "X column", ParameterKind.Choice, RowNumber) { Choices = [RowNumber, .. choices] }, () => options.GetText("x", string.Empty) is { Length: > 0 } x ? x : RowNumber, value => SetValue(options, "x", (string)value == RowNumber ? string.Empty : value)));
        }

        string[]? roles = type.Shape switch
        {
            DataShape.Band => ["Lower column", "Upper column"],
            DataShape.Bubbles => ["Y column", "Size column"],
            DataShape.Errors => ["Y column", "Lower error column", "Upper error column"],
            DataShape.Points3D => ["Y column", "Z column"],
            _ => null,
        };
        if (roles is null)
        {
            var description = type.Shape switch
            {
                DataShape.Points => "Each column chosen draws its own line, named after the column.",
                DataShape.Values => "Each column chosen becomes its own series, named after the column.",
                _ => "Chosen: " + DataSources.CsvColumns(type.Shape) + ".",
            };
            fields.Add(Field(group, new("y", "Y columns", ParameterKind.Choices, string.Empty) { Choices = choices, Description = description }, () => string.Join(",", DataSources.Columns(options)), value => SetValue(options, "y", value)));
            return;
        }

        // Forms with a column per role choose each one apart.
        for (var role = 0; role < roles.Length; role++)
        {
            var at = role;
            fields.Add(Field(group, new("y" + role.ToString(CultureInfo.InvariantCulture), roles[role], ParameterKind.Choice, choices[0]) { Choices = choices }, () => DataSources.Columns(options) is var chosen && at < chosen.Length && choices.Contains(chosen[at]) ? chosen[at] : string.Empty, value =>
            {
                var chosen = DataSources.Columns(options).ToList();
                while (chosen.Count < roles.Length)
                {
                    chosen.Add(choices[Math.Min(chosen.Count, choices.Length - 1)]);
                }

                chosen[at] = (string)value;
                options.Set("y", string.Join(",", chosen));
            }));
        }
    }

    // Takes a CSV file: guesses its format and chooses the first column as X and the next ones as Y.
    private static void UseCsv(ChartTypeInfo type, DataSpec data, string path)
    {
        data.Path = path;
        if (path.Length == 0 || !File.Exists(path))
        {
            return;
        }

        var options = data.Options;
        var format = DataSources.GuessCsv(path);
        options.Set("delimiter", DataSources.DelimiterName(format.Delimiter));
        options.Set("header", format.HasHeader);
        options.Set("decimalComma", format.DecimalComma);
        var names = DataSources.CsvNames(path, options).Select((name, index) => name.Contains(',', StringComparison.Ordinal) ? (index + 1).ToString(CultureInfo.InvariantCulture) : name).ToArray();
        var hasX = names.Length > 1;
        options.Set("x", hasX && type.Shape is not (DataShape.Values or DataShape.Rows or DataShape.Groups) ? names[0] : string.Empty);
        var rest = hasX ? names[1..] : names;
        options.Set("y", string.Join(",", type.Shape is DataShape.Rows or DataShape.Groups or DataShape.Layers ? rest : rest.Take(DataSources.Needed(type.Shape))));
    }

    private FieldModel Field(string group, ParameterInfo info, Func<object> read, Action<object> write, bool structural = false) =>
        new(group, info, read, value =>
        {
            write(value);
            if (structural)
            {
                Structure();
            }
            else
            {
                Refresh();
            }
        });

    private void Toggle(ModifierInfo info, bool on)
    {
        _spec.Modifiers.RemoveAll(modifier => modifier.Type == info.Id);
        if (on)
        {
            _spec.Modifiers.Add(new ModifierSpec { Type = info.Id });
        }
    }

    private string Unique(string name)
    {
        var unique = name;
        for (var i = 2; _spec.Series.Any(series => series.Name == unique); i++)
        {
            unique = name + " " + i.ToString(CultureInfo.InvariantCulture);
        }

        return unique;
    }

    private void Structure()
    {
        Refresh();
        FieldsChanged?.Invoke();
    }

    // Describes the chart once without drawing it, so that what only the description finds, such as an axis name that
    // occurs twice, shows as a problem instead of failing in the preview.
    private string? Describe()
    {
        try
        {
            var builder = new ChartBuilder();
            SpecChartBuilder.Apply(_spec, Catalog, builder);
            builder.ToDefinition();
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException or InvalidCastException)
        {
            return exception.Message;
        }
    }

    // Writes the code; the preview follows in the apps through Changed. Problems keep the last good code.
    private void Refresh()
    {
        var clock = Stopwatch.StartNew();
        Problems = Catalog.Check(_spec);
        if (Problems.Count == 0 && Describe() is { } failure)
        {
            Problems = [failure];
        }

        if (Problems.Count == 0)
        {
            var options = Options;
            var window = options.Framework == UiFramework.Wpf ? "MainWindow.xaml" : "MainWindow.axaml";
            Code = new Dictionary<string, string>
            {
                ["C#"] = _generator.Setup(_spec, options),
                ["View model"] = _generator.ViewModel(_spec, options),
                [window] = _generator.WindowMarkup(_spec, options),
                ["Code behind"] = CodeGenerator.WindowCode(_spec, options),
                ["Project"] = _generator.Project(_spec, options)[options.ProjectName + ".csproj"],
            };
        }

        LastUpdate = clock.Elapsed;
        Changed?.Invoke();
    }
}
