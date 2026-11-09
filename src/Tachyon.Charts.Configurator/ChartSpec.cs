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

    /// <summary>Gets the settings of the scene the series go into, such as the camera of a 3D chart; see <see cref="SceneInfo"/>.</summary>
    public SpecValues Scene { get; init; } = new();
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
}
