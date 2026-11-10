namespace Tachyon.Charts.Configurator;

/// <summary>
/// The description of a chart that the configurator edits: from it come both the preview and the code, so the two can
/// never differ. It is saved as versioned JSON, see <see cref="ChartSpecJson"/>.
/// </summary>
public sealed class ChartSpec
{
    /// <summary>The version of the format written today.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Gets or sets the version of the format.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Gets or sets the title, empty for none.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of a built-in theme, or <see langword="null"/> for the theme of the application.</summary>
    public string? Theme { get; set; }

    /// <summary>Gets or sets a value indicating whether the X axis runs downwards.</summary>
    public bool XDown { get; set; }

    /// <summary>Gets or sets the name of the group whose charts share their ranges and cursor, or <see langword="null"/>.</summary>
    public string? Group { get; set; }

    /// <summary>Gets the horizontal axes; the first is the default for the series.</summary>
    public List<AxisSpec> XAxes { get; init; } = [];

    /// <summary>Gets the vertical axes; the first is the default for the series.</summary>
    public List<AxisSpec> YAxes { get; init; } = [];

    /// <summary>Gets the series, drawn in this order.</summary>
    public List<SeriesSpec> Series { get; init; } = [];

    /// <summary>Gets the interactions.</summary>
    public List<ModifierSpec> Modifiers { get; init; } = [];

    /// <summary>Gets or sets the legend, or <see langword="null"/> for none.</summary>
    public LegendSpec? Legend { get; set; }

    /// <summary>Gets or sets a value indicating whether the generated window gives the chart a context menu that saves it.</summary>
    public bool ExportMenu { get; set; }

    /// <summary>Gets the annotations: lines, bands, boxes and text on the first axes.</summary>
    public List<AnnotationSpec> Annotations { get; init; } = [];

    /// <summary>Gets or sets a second legend, for example for the series of a second axis, or <see langword="null"/> for none.</summary>
    public LegendSpec? SecondLegend { get; set; }

    /// <summary>Gets the settings of the scene the series go into, such as the camera of a 3D chart; see <see cref="SceneInfo"/>.</summary>
    public SpecValues Scene { get; init; } = new();
}

/// <summary>An annotation of the chart.</summary>
public sealed class AnnotationSpec
{
    /// <summary>Gets or sets the kind: <c>hline</c>, <c>vline</c>, <c>band</c>, <c>box</c> or <c>text</c>.</summary>
    public string Type { get; set; } = "hline";

    /// <summary>Gets the values of its parameters.</summary>
    public SpecValues Parameters { get; init; } = new();
}

/// <summary>How an axis maps its values.</summary>
public enum AxisKind
{
    /// <summary>Linear numbers.</summary>
    Numeric,

    /// <summary>Powers of ten.</summary>
    Logarithmic,

    /// <summary>Dates and times.</summary>
    DateTime,

    /// <summary>Names instead of numbers, one per place 0, 1, 2 and so on.</summary>
    Category,
}

/// <summary>An axis of a <see cref="ChartSpec"/>.</summary>
public sealed class AxisSpec
{
    /// <summary>Gets or sets the identifier the series refer to.</summary>
    public string Id { get; set; } = "x";

    /// <summary>Gets or sets how the axis maps its values.</summary>
    public AxisKind Kind { get; set; }

    /// <summary>Gets or sets the title, empty for none.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the lowest value shown, or <see langword="null"/> to follow the data.</summary>
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the highest value shown, or <see langword="null"/> to follow the data.</summary>
    public double? Maximum { get; set; }

    /// <summary>Gets or sets a value indicating whether the axis runs the other way.</summary>
    public bool IsInverted { get; set; }

    /// <summary>Gets or sets a value indicating whether the axis sits on the far side: right or top.</summary>
    public bool IsOpposite { get; set; }

    /// <summary>Gets or sets a value indicating whether a time axis leaves out the weekends, as for prices of working days.</summary>
    public bool SkipsWeekends { get; set; }

    /// <summary>Gets or sets the names of the places of a <see cref="AxisKind.Category"/> axis, separated by commas.</summary>
    public string Categories { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the axis runs through the plot where the other axis is 0, as for axes in the origin.</summary>
    public bool CrossesZero { get; set; }

    /// <summary>Gets or sets the ranges left out of the axis, written as <c>from..to</c> and separated by commas, such as <c>10..90</c>.</summary>
    public string Breaks { get; set; } = string.Empty;

    /// <summary>Gets or sets the pane of a vertical axis, from 0 at the top; panes stack and share the horizontal axis.</summary>
    public int Pane { get; set; }

    /// <summary>Gets or sets the share of the height the pane takes, against the other panes.</summary>
    public double PaneWeight { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether the axis is hidden; it still scales its series.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Reads <see cref="Breaks"/>.</summary>
    /// <returns>The ranges, or <see langword="null"/> when the text is no list of ranges.</returns>
    public IReadOnlyList<(double From, double To)>? ReadBreaks()
    {
        var ranges = new List<(double, double)>();
        foreach (var part in Breaks.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = part.Split("..", StringSplitOptions.TrimEntries);
            if (ends.Length != 2
                || !double.TryParse(ends[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var from)
                || !double.TryParse(ends[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var to)
                || !(to > from))
            {
                return null;
            }

            ranges.Add((from, to));
        }

        return ranges;
    }

    /// <summary>Reads <see cref="Categories"/>.</summary>
    /// <returns>The names.</returns>
    public string[] ReadCategories() => Categories.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Gets a value indicating whether the range follows the data.</summary>
    public bool IsAutoRange => Minimum is null || Maximum is null;
}

/// <summary>A series of a <see cref="ChartSpec"/>: its type, its parameters and where its data come from.</summary>
public sealed class SeriesSpec
{
    /// <summary>Gets or sets the identifier of the type, see <see cref="ChartCatalog"/>.</summary>
    public string Type { get; set; } = "line";

    /// <summary>Gets or sets the name shown in legends and tooltips.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the horizontal axis, or <see langword="null"/> for the first.</summary>
    public string? XAxis { get; set; }

    /// <summary>Gets or sets the vertical axis, or <see langword="null"/> for the first.</summary>
    public string? YAxis { get; set; }

    /// <summary>Gets the values of the parameters of the type; missing ones take their default.</summary>
    public SpecValues Parameters { get; init; } = new();

    /// <summary>Gets or sets where the data come from.</summary>
    public DataSpec Data { get; set; } = new();
}

/// <summary>Where the data of a series come from.</summary>
public enum DataKind
{
    /// <summary>Data made up by a generator of the configurator, see <see cref="SampleCatalog"/>.</summary>
    Sample,

    /// <summary>Columns of a CSV file.</summary>
    Csv,

    /// <summary>Data made up while the chart runs, at a set rate.</summary>
    Live,

    /// <summary>
    /// A medical file or series. Its place is never saved: paths of medical data often hold names or numbers of people,
    /// and configurations are passed on.
    /// </summary>
    MedicalFile,

    /// <summary>Datasets of an HDF5 file: one dimension for curves and values, two for grids, three for volumes.</summary>
    Hdf5,

    /// <summary>A file of points, a volume or a model in 3D.</summary>
    File,
}

/// <summary>Where the data of a series come from and how many there are.</summary>
public sealed class DataSpec
{
    private string? _path;

    /// <summary>Gets or sets the kind of source.</summary>
    public DataKind Kind { get; set; }

    /// <summary>Gets or sets the identifier of the generator for <see cref="DataKind.Sample"/> and <see cref="DataKind.Live"/>.</summary>
    public string Sample { get; set; } = "sine";

    /// <summary>Gets or sets the number of values a generator makes.</summary>
    public int Count { get; set; } = 1000;

    /// <summary>Gets the further options of the source, such as the columns of a CSV file.</summary>
    public SpecValues Options { get; init; } = new();

    /// <summary>
    /// Gets or sets the file of a CSV source. For a medical file it is kept for the preview only and never saved.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Path
    {
        get => _path;
        set => _path = value;
    }

    /// <summary>Gets or sets the file as saved: the path of a CSV file, never one of medical data; reading drops such a path.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("path")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SavedPath
    {
        get => Kind == DataKind.MedicalFile ? null : _path;
        set => _path = value;
    }
}

/// <summary>An interaction of a <see cref="ChartSpec"/>.</summary>
public sealed class ModifierSpec
{
    /// <summary>Gets or sets the identifier of the interaction, see <see cref="ChartCatalog.Modifiers"/>.</summary>
    public string Type { get; set; } = "zoompan";

    /// <summary>Gets the values of its parameters.</summary>
    public SpecValues Parameters { get; init; } = new();
}

/// <summary>The legend of a <see cref="ChartSpec"/>.</summary>
public sealed class LegendSpec
{
    /// <summary>Gets or sets the title, empty for none.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets where the legend stands, the name of a <c>LegendPlacement</c> such as <c>TopRight</c> or <c>Bottom</c>.</summary>
    public string Placement { get; set; } = "TopRight";

    /// <summary>Gets or sets the names of the series it lists, separated by commas; empty for all.</summary>
    public string Series { get; set; } = string.Empty;

    /// <summary>Reads <see cref="Series"/>.</summary>
    /// <returns>The names.</returns>
    public string[] ReadSeries() => Series.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
