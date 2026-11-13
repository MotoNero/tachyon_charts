using Tachyon.Charts.Core.Fluent;

namespace Tachyon.Charts.Configurator;

/// <summary>What kind of value a parameter takes; the configurator shows a field to match.</summary>
public enum ParameterKind
{
    /// <summary>A number between <see cref="ParameterInfo.Minimum"/> and <see cref="ParameterInfo.Maximum"/>.</summary>
    Number,

    /// <summary>A whole number between <see cref="ParameterInfo.Minimum"/> and <see cref="ParameterInfo.Maximum"/>.</summary>
    Integer,

    /// <summary>Yes or no.</summary>
    Flag,

    /// <summary>One of <see cref="ParameterInfo.Choices"/>.</summary>
    Choice,

    /// <summary>Any of <see cref="ParameterInfo.Choices"/>, kept as text separated by commas in the order of the choices.</summary>
    Choices,

    /// <summary>A color as #RRGGBB or #RRGGBBAA, or empty for the color of the theme.</summary>
    Color,

    /// <summary>The path of a file; the apps offer to choose it.</summary>
    File,

    /// <summary>Free text.</summary>
    Text,
}

/// <summary>A parameter of a chart type or an interaction, from which the configurator builds its field.</summary>
/// <param name="Name">The name in <see cref="SpecValues"/>.</param>
/// <param name="Label">The label of the field.</param>
/// <param name="Kind">The kind of value.</param>
/// <param name="Default">The value when none is set: a <see cref="double"/>, <see cref="bool"/> or <see cref="string"/>.</param>
public sealed record ParameterInfo(string Name, string Label, ParameterKind Kind, object Default)
{
    /// <summary>Gets the lowest number allowed.</summary>
    public double Minimum { get; init; } = double.NegativeInfinity;

    /// <summary>Gets the highest number allowed.</summary>
    public double Maximum { get; init; } = double.PositiveInfinity;

    /// <summary>Gets the choices of a <see cref="ParameterKind.Choice"/>.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>Gets a sentence on what the parameter does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets a number from a set of values, or the default.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The number.</returns>
    public double Number(SpecValues values) => (values ?? throw new ArgumentNullException(nameof(values))).GetNumber(Name, Default is double number ? number : 0);

    /// <summary>Gets a yes/no value from a set of values, or the default.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The value.</returns>
    public bool Flag(SpecValues values) => (values ?? throw new ArgumentNullException(nameof(values))).GetFlag(Name, Default is true);

    /// <summary>Gets a text from a set of values, or the default.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The text.</returns>
    public string Text(SpecValues values) => (values ?? throw new ArgumentNullException(nameof(values))).GetText(Name, Default as string ?? string.Empty);

    /// <summary>Gets a sentence on what is wrong with a value, or <see langword="null"/> when it fits.</summary>
    /// <param name="value">The value as kept in <see cref="SpecValues"/>.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    public string? Check(object? value) => (Kind, value) switch
    {
        (_, null) => null,
        (ParameterKind.Number or ParameterKind.Integer, double number) when number < Minimum || number > Maximum => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Label} must lie between {Minimum} and {Maximum}."),
        (ParameterKind.Integer, double number) when number != Math.Floor(number) => $"{Label} must be a whole number.",
        (ParameterKind.Number or ParameterKind.Integer, double) => null,
        (ParameterKind.Flag, bool) => null,
        (ParameterKind.Choice, string text) when !Choices.Contains(text) => $"{Label} must be one of {string.Join(", ", Choices)}.",
        (ParameterKind.Choices, string text) when ParameterInfo.Split(text).Any(item => !Choices.Contains(item)) => $"{Label} takes only {string.Join(", ", Choices)}.",
        (ParameterKind.Color, string text) when text.Length > 0 && !SpecColor.TryParse(text, out _) => $"{Label} must be a color like #3A86FF.",
        (ParameterKind.Choice or ParameterKind.Choices or ParameterKind.Color or ParameterKind.Text or ParameterKind.File, string) => null,
        _ => $"{Label} has a value of the wrong kind.",
    };

    /// <summary>Splits the value of a <see cref="ParameterKind.Choices"/> into the chosen items.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<string> Split(string value) =>
        (value ?? throw new ArgumentNullException(nameof(value))).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>The groups of the configurator.</summary>
public enum ChartCategory
{
    /// <summary>2D charts.</summary>
    TwoD,

    /// <summary>Instruments such as the oscilloscope.</summary>
    Instrument,

    /// <summary>3D charts, from the 3D add-on.</summary>
    ThreeD,

    /// <summary>Medical images and volumes, from the medical add-on.</summary>
    Medical,
}

/// <summary>The form of the data a type draws.</summary>
public enum DataShape
{
    /// <summary>Pairs of X and Y.</summary>
    Points,

    /// <summary>Rows of values, one group per position, as for bars.</summary>
    Rows,

    /// <summary>A grid of values, as for a heatmap.</summary>
    Grid,

    /// <summary>Pairs of X and Y with a size per point.</summary>
    Bubbles,

    /// <summary>Two curves over the same X.</summary>
    Band,

    /// <summary>Several curves over the same X that stack.</summary>
    Layers,

    /// <summary>Pairs of X and Y with a lower and an upper error per point.</summary>
    Errors,

    /// <summary>Groups of measured values, one distribution per group.</summary>
    Groups,

    /// <summary>One list of measured values.</summary>
    Values,

    /// <summary>Values with names, one slice each.</summary>
    Slices,

    /// <summary>Points in space: X, Y and Z.</summary>
    Points3D,

    /// <summary>Points in space with a diameter each.</summary>
    Bubbles3D,

    /// <summary>Rows of a spectrum over time.</summary>
    Spectrogram,

    /// <summary>Values in a box of voxels.</summary>
    Volume,

    /// <summary>Gains over azimuth and elevation.</summary>
    Pattern,

    /// <summary>A grid with the edges of its cells, which may differ in size.</summary>
    UnevenGrid,

    /// <summary>A grid and the region it was measured in.</summary>
    MaskedGrid,

    /// <summary>Values over the angle.</summary>
    Polar,

    /// <summary>Mixtures of three parts.</summary>
    Ternary,

    /// <summary>Impedances: resistance and reactance.</summary>
    Smith,

    /// <summary>Shapes, some with holes.</summary>
    Polygons,

    /// <summary>Prices over time: open, high, low, close and volume.</summary>
    Candles,

    /// <summary>Tasks over time in rows, with links between them.</summary>
    Gantt,

    /// <summary>A model of triangles.</summary>
    Model,

    /// <summary>A medical image, a display of one picture with its spacing and orientation.</summary>
    MedicalImage,

    /// <summary>A medical volume in a display of its planes.</summary>
    MedicalVolume,

    /// <summary>The display of an oscilloscope with its channels.</summary>
    Scope,

    /// <summary>The display of a spectrum analyzer with its trace.</summary>
    Spectrum,

    /// <summary>The display of a network analyzer with its traces.</summary>
    Network,

    /// <summary>Channels of a sweeping monitor.</summary>
    Sweep,
}

/// <summary>One of several linked charts a type fills, such as a plane of a volume; all of them share the data.</summary>
/// <param name="Name">The name, also the name of its chart and method in the generated code, such as <c>Axial</c>.</param>
/// <param name="Apply">Describes the chart from the series and its data.</param>
/// <param name="Code">Writes the same as C# statements on <c>chart</c>, from the series and the name of its data.</param>
public sealed record ViewInfo(string Name, Action<ChartBuilder, SeriesSpec, object> Apply, Func<SeriesSpec, string, string> Code);

/// <summary>A template: a description to start from, such as a medical display with its measuring tools.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Create">Makes a new description.</param>
public sealed record TemplateInfo(string Id, string Name, Func<ChartCatalog, ChartSpec> Create);

/// <summary>A page of the showroom as a description: the same charts, series, axes, interactions and data.</summary>
/// <param name="Page">The identifier of the page in the showroom.</param>
/// <param name="Create">Makes the description.</param>
public sealed record PageInfo(string Page, Func<ChartCatalog, ChartSpec> Create);

/// <summary>A chart type the configurator offers, with its parameters and how it is built.</summary>
public sealed class ChartTypeInfo
{
    /// <summary>Initializes a type.</summary>
    /// <param name="id">The identifier in <see cref="SeriesSpec.Type"/>.</param>
    /// <param name="name">The name shown.</param>
    /// <param name="category">The group.</param>
    /// <param name="shape">The form of its data.</param>
    /// <param name="defaultSample">The generator of its sample data.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="apply">Adds a series of this type to a chart description; the data have the type of the sample's <see cref="SampleInfo.ReturnType"/>.</param>
    /// <param name="code">Writes the same step as C#, see <see cref="SeriesCodeWriter"/>.</param>
    public ChartTypeInfo(string id, string name, ChartCategory category, DataShape shape, string defaultSample, IReadOnlyList<ParameterInfo> parameters, Action<ChartBuilder, SeriesSpec, object, SeriesAxes> apply, SeriesCodeWriter code)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(name);
        (Id, Name, Category, Shape, DefaultSample, Parameters, Apply, Code) =
            (id, name, category, shape, defaultSample, parameters ?? throw new ArgumentNullException(nameof(parameters)), apply ?? throw new ArgumentNullException(nameof(apply)), code ?? throw new ArgumentNullException(nameof(code)));
    }

    /// <summary>Gets the identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the name shown.</summary>
    public string Name { get; }

    /// <summary>Gets the group.</summary>
    public ChartCategory Category { get; }

    /// <summary>Gets the form of the data.</summary>
    public DataShape Shape { get; }

    /// <summary>Gets the generator of the sample data.</summary>
    public string DefaultSample { get; }

    /// <summary>Gets the parameters.</summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; }

    /// <summary>Gets how a series of this type is added to a chart description.</summary>
    public Action<ChartBuilder, SeriesSpec, object, SeriesAxes> Apply { get; }

    /// <summary>Gets how the same step is written as C#.</summary>
    public SeriesCodeWriter Code { get; }

    /// <summary>Gets the namespaces the code of this type needs beyond the usual ones.</summary>
    public IReadOnlyList<string> Usings { get; init; } = [];

    /// <summary>
    /// Gets the linked charts the type fills by its settings, or <see langword="null"/> for one chart. A type with views
    /// fills the description alone; <see cref="Apply"/> describes its first view.
    /// </summary>
    public Func<SeriesSpec, IReadOnlyList<ViewInfo>>? Views { get; init; }

    /// <summary>
    /// Gets how what was done in the preview, such as measurements drawn by hand, goes over from the data before a change
    /// into the data made for it: the series and data before, then the series and data after. <see langword="null"/>
    /// when nothing is to keep. It is called only when both read the same source.
    /// </summary>
    public Action<SeriesSpec, object, SeriesSpec, object>? TakeOver { get; init; }

    /// <summary>
    /// Gets the part of the chart that shows, in the units of the axes the annotations use, from the series and its data;
    /// a new annotation starts in it. For types whose axes are not those of the description, such as instruments with
    /// levels in dBm and frequencies in hertz; <see langword="null"/> for the others.
    /// </summary>
    public Func<SeriesSpec, object, (Core.Numerics.DoubleRange X, Core.Numerics.DoubleRange Y)>? Region { get; init; }

    /// <summary>
    /// Gets what keeps the data of the preview coming, such as new spectra of a live measurement, from the series and its
    /// data; disposing it stops them. <see langword="null"/> for data that stay as they are.
    /// </summary>
    public Func<SeriesSpec, object, IDisposable?>? Start { get; init; }

    /// <summary>
    /// Gets how the data of a sample or source turn into what the type draws, such as the display of an oscilloscope
    /// from its waveforms, after <see cref="Prepare"/>; <see langword="null"/> when the type draws them as they are.
    /// </summary>
    public Func<SeriesSpec, object, object>? Make { get; init; }

    /// <summary>
    /// Gets what the type sets right when a series takes other data (another sample, file or kind of source), such as
    /// settings that only fit the data before; <see langword="null"/> for nothing.
    /// </summary>
    public Action<SeriesSpec>? OnNewData { get; init; }

    /// <summary>Gets the number of columns the views are arranged in, by the settings; one when not given.</summary>
    public Func<SeriesSpec, int>? Columns { get; init; }

    /// <summary>Gets the packages of Tachyon Charts the code of this type needs beyond the adapter, with their versions.</summary>
    public IReadOnlyList<(string Name, string Version)> Packages { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the type brings its own hidden axes and fills the chart alone, as a pie does.
    /// The axes of the description are then left out.
    /// </summary>
    public bool OwnsAxes { get; init; }

    /// <summary>
    /// Gets how the data are set up before the chart describes them, such as the controls of an instrument display, or
    /// <see langword="null"/> for none. <see cref="PrepareCode"/> writes the same as C# statements.
    /// </summary>
    public Action<SeriesSpec, object>? Prepare { get; init; }

    /// <summary>Gets the statements that do what <see cref="Prepare"/> does, from the series and the name of its data.</summary>
    public Func<SeriesSpec, string, IEnumerable<string>>? PrepareCode { get; init; }

    /// <summary>Gets static methods the code of this type calls, indented by four spaces, or empty for none.</summary>
    public string Helpers { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the X axis of a new chart of this type is a time axis.</summary>
    public bool UsesTime { get; init; }

    /// <summary>
    /// Gets a value indicating whether a new chart of this type gets the usual interactions although it brings its own
    /// axes; a pie or a 3D scene has interactions of its own and gets none.
    /// </summary>
    public bool TakesInteractions { get; init; }

    /// <summary>
    /// Gets how a series of this type is added to the scene of its category (see <see cref="SceneInfo"/>), with the
    /// builder of the scene, the series and its data; <see langword="null"/> for types that go into the chart itself.
    /// Such types own the axes; their code writer writes the call on the builder of the scene.
    /// </summary>
    public Action<object, SeriesSpec, object>? SceneApply { get; init; }

    /// <summary>Gets a parameter by name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The parameter.</returns>
    /// <exception cref="KeyNotFoundException">The type has no such parameter.</exception>
    public ParameterInfo Parameter(string name) => Parameters.FirstOrDefault(parameter => parameter.Name == name) ?? throw new KeyNotFoundException($"{Name} has no parameter {name}.");
}

/// <summary>
/// Writes the call that adds a series to a chart description, such as <c>.AddLine(data, s =&gt; s.Name("Signal"))</c>, the
/// same step <see cref="ChartTypeInfo.Apply"/> takes for the preview.
/// </summary>
/// <param name="series">The series.</param>
/// <param name="data">The C# name of the variable that holds its data.</param>
/// <param name="axes">The axes it uses.</param>
/// <returns>The call, starting with a dot.</returns>
public delegate string SeriesCodeWriter(SeriesSpec series, string data, SeriesAxes axes);

/// <summary>The axes a series uses, by identifier.</summary>
/// <param name="X">The horizontal axis.</param>
/// <param name="Y">The vertical axis.</param>
public readonly record struct SeriesAxes(string X, string Y)
{
    /// <summary>Gets a value indicating whether the series goes into a 3D box that an earlier series of the scene set up, so that it leaves the axes alone.</summary>
    public bool InSharedBox { get; init; }
}

/// <summary>An interaction the configurator offers.</summary>
/// <param name="Id">The identifier in <see cref="ModifierSpec.Type"/>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Parameters">Its parameters.</param>
/// <param name="Apply">Adds it to the interactions of a chart.</param>
/// <param name="Code">Writes the same call as C#, such as <c>.ZoomPan()</c>.</param>
public sealed record ModifierInfo(string Id, string Name, IReadOnlyList<ParameterInfo> Parameters, Action<ModifierBuilder, SpecValues> Apply, Func<SpecValues, string> Code);

/// <summary>A part of the catalog, such as the 2D types of the core or the types of an add-on.</summary>
public interface IChartModule
{
    /// <summary>Gets the chart types.</summary>
    IReadOnlyList<ChartTypeInfo> Types { get; }

    /// <summary>Gets the generators of sample data.</summary>
    IReadOnlyList<SampleInfo> Samples { get; }

    /// <summary>Gets the scenes that take the series of a category, such as the 3D scene.</summary>
    IReadOnlyList<SceneInfo> Scenes => [];

    /// <summary>Gets the templates of the module.</summary>
    IReadOnlyList<TemplateInfo> Templates => [];

    /// <summary>Gets the pages of the showroom this module describes completely.</summary>
    IReadOnlyList<PageInfo> Pages => [];
}

/// <summary>
/// A builder of its own that takes all series of a category, as a 3D scene does. The series go into it in one call,
/// which then writes them into the chart.
/// </summary>
/// <param name="Category">The category whose series it takes.</param>
/// <param name="Parameters">The settings of the scene, kept in <see cref="ChartSpec.Scene"/>.</param>
/// <param name="Apply">Creates the builder from the description, hands it to the action that adds the series, and writes it into the chart.</param>
/// <param name="Code">Writes the same call in C#: the part before the series, such as <c>.Scene3D(s => s</c>, and the part after them.</param>
/// <param name="Usings">The namespaces of the call.</param>
/// <param name="Package">The package the generated project needs for it.</param>
/// <param name="PackageVersion">The version of that package.</param>
public sealed record SceneInfo(ChartCategory Category, IReadOnlyList<ParameterInfo> Parameters, Action<ChartBuilder, ChartSpec, Action<object>> Apply, Func<ChartSpec, (string Start, string End)> Code, IReadOnlyList<string> Usings, string Package, string PackageVersion);

/// <summary>
/// All chart types, sample data and interactions the configurator offers. Add-ons such as 3D and Medical add their
/// own module when their package is there; without it they stay out.
/// </summary>
public sealed class ChartCatalog
{
    private readonly List<ChartTypeInfo> _types = [];
    private readonly List<SampleInfo> _samples = [];
    private readonly List<SceneInfo> _scenes = [];
    private readonly List<DataLoader> _loaders = [];
    private readonly List<TemplateInfo> _templates = [];
    private readonly List<PageInfo> _pages = [];

    // The modules of the add-ons by type and assembly; loaded only when the assembly is there.
    private static readonly string[] _addOns =
    [
        "Tachyon.Charts.Configurator.ThreeD.ThreeDModule, Tachyon.Charts.Configurator.ThreeD",
        "Tachyon.Charts.Configurator.Medical.MedicalModule, Tachyon.Charts.Configurator.Medical",
    ];

    /// <summary>Initializes a catalog with the 2D types of the core.</summary>
    public ChartCatalog()
    {
        Add(new CoreModule());
    }

    /// <summary>Gets the chart types.</summary>
    public IReadOnlyList<ChartTypeInfo> Types => _types;

    /// <summary>Gets the generators of sample data.</summary>
    public IReadOnlyList<SampleInfo> Samples => _samples;

    /// <summary>Gets the templates of the modules.</summary>
    public IReadOnlyList<TemplateInfo> Templates => _templates;

    /// <summary>Gets the pages of the showroom the modules describe.</summary>
    public IReadOnlyList<PageInfo> Pages => _pages;

    /// <summary>Gets the description of a page of the showroom.</summary>
    /// <param name="page">The identifier of the page.</param>
    /// <returns>The description, or <see langword="null"/> when no module describes the page.</returns>
    public ChartSpec? CreatePage(string page) => _pages.Find(known => known.Page == page)?.Create(this);

    // Whether a series fills linked charts by its settings.
    private bool HasViews(SeriesSpec series) => FindType(series.Type)?.Views is { } views && views(series).Count > 0;

    /// <summary>Gets the linked charts of a description and their columns, or <see langword="null"/> for one chart.</summary>
    /// <param name="spec">The description.</param>
    /// <returns>The views and the number of columns.</returns>
    public (IReadOnlyList<ViewInfo> Views, int Columns)? ViewsOf(ChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Series.Count != 1 || FindType(spec.Series[0].Type) is not { Views: { } views } type)
        {
            return null;
        }

        var series = spec.Series[0];
        var linked = views(series);
        return linked.Count == 0 ? null : (linked, Math.Max(1, type.Columns?.Invoke(series) ?? 1));
    }

    /// <summary>Gets the interactions.</summary>
    public IReadOnlyList<ModifierInfo> Modifiers { get; } = CoreModule.CreateModifiers();

    /// <summary>Gets the names of the built-in themes.</summary>
    public static IReadOnlyList<string> Themes { get; } = ["TachyonDark", "TachyonLight", "HighContrast", "Neon", "Instrument", "Signal", "Paper"];

    /// <summary>Adds the types and samples of a module.</summary>
    /// <param name="module">The module.</param>
    /// <exception cref="ArgumentException">A type or sample is there already.</exception>
    public void Add(IChartModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        foreach (var type in module.Types)
        {
            if (_types.Any(known => known.Id == type.Id))
            {
                throw new ArgumentException($"The type {type.Id} is there already.", nameof(module));
            }

            _types.Add(type);
        }

        foreach (var sample in module.Samples)
        {
            if (_samples.Any(known => known.Id == sample.Id))
            {
                throw new ArgumentException($"The sample {sample.Id} is there already.", nameof(module));
            }

            _samples.Add(sample);
        }

        _scenes.AddRange(module.Scenes.Where(scene => _scenes.TrueForAll(known => known.Category != scene.Category)));
        _templates.AddRange(module.Templates);
        _pages.AddRange(module.Pages);
        if (module is IDataLoaders readers)
        {
            _loaders.AddRange(readers.Loaders);
        }
    }

    /// <summary>Gets the readers of data the modules bring.</summary>
    internal IReadOnlyList<DataLoader> Loaders => _loaders;

    /// <summary>Creates the catalog with the core and every add-on module whose assembly is there; the others stay out.</summary>
    /// <returns>The catalog.</returns>
    public static ChartCatalog CreateWithAddOns()
    {
        var catalog = new ChartCatalog();
        foreach (var name in _addOns)
        {
            if (Type.GetType(name, throwOnError: false) is { } type && Activator.CreateInstance(type) is IChartModule module)
            {
                catalog.Add(module);
            }
        }

        return catalog;
    }

    /// <summary>Gets a kind of annotation by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The kind, or <see langword="null"/>.</returns>
    internal static AnnotationInfo? FindAnnotation(string id) => AnnotationCatalog.All.FirstOrDefault(info => info.Id == id);

    /// <summary>Gets the scene the series of a description go into, or <see langword="null"/> when they go into the chart itself.</summary>
    /// <param name="spec">The description.</param>
    /// <returns>The scene.</returns>
    public SceneInfo? SceneOf(ChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var type = spec.Series.Select(series => FindType(series.Type)).FirstOrDefault(type => type?.SceneApply is not null);
        return type is null ? null : _scenes.Find(scene => scene.Category == type.Category);
    }

    /// <summary>Gets a type by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The type, or <see langword="null"/>.</returns>
    public ChartTypeInfo? FindType(string id) => _types.FirstOrDefault(type => type.Id == id);

    /// <summary>Gets a generator by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The generator, or <see langword="null"/>.</returns>
    public SampleInfo? FindSample(string id) => _samples.FirstOrDefault(sample => sample.Id == id);

    /// <summary>Gets an interaction by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The interaction, or <see langword="null"/>.</returns>
    public ModifierInfo? FindModifier(string id) => Modifiers.FirstOrDefault(modifier => modifier.Id == id);

    /// <summary>Creates the description of a chart with one series of a type, its sample data and the usual interactions.</summary>
    /// <param name="typeId">The type.</param>
    /// <returns>The description.</returns>
    /// <exception cref="KeyNotFoundException">The type is unknown.</exception>
    public ChartSpec CreateSpec(string typeId)
    {
        var type = FindType(typeId) ?? throw new KeyNotFoundException($"The type {typeId} is unknown.");
        var spec = new ChartSpec { Title = type.Name };
        spec.XAxes.Add(new AxisSpec { Id = "x", Title = "x" });
        spec.YAxes.Add(new AxisSpec { Id = "y", Title = "y" });
        spec.Series.Add(new SeriesSpec { Type = type.Id, Name = type.Name, Data = new DataSpec { Sample = type.DefaultSample, Count = FindSample(type.DefaultSample)?.DefaultCount ?? 1000 } });
        if (type.UsesTime)
        {
            (spec.XAxes[0].Kind, spec.XAxes[0].Id, spec.XAxes[0].Title) = (AxisKind.DateTime, "time", "Time");
        }

        if (!type.OwnsAxes || type.TakesInteractions)
        {
            spec.Modifiers.Add(new ModifierSpec { Type = "zoompan" });
            spec.Modifiers.Add(new ModifierSpec { Type = "crosshair" });
        }

        return spec;
    }

    /// <summary>Gets a value indicating whether a series of the description brings its own axes, see <see cref="ChartTypeInfo.OwnsAxes"/>.</summary>
    /// <param name="spec">The description.</param>
    /// <returns><see langword="true"/> when the axes of the description are left out.</returns>
    public bool OwnsAxes(ChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Series.Exists(series => FindType(series.Type)?.OwnsAxes == true);
    }

    /// <summary>Lists what is wrong with a description: unknown types, axes, samples and interactions, and values that do not fit.</summary>
    /// <param name="spec">The description.</param>
    /// <returns>One sentence per problem; empty when all is well.</returns>
    public IReadOnlyList<string> Check(ChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var problems = new List<string>();
        if (spec.XAxes.Count == 0 || spec.YAxes.Count == 0)
        {
            problems.Add("A chart needs at least one horizontal and one vertical axis.");
        }

        if (spec.Theme is { } theme && !Themes.Contains(theme))
        {
            problems.Add($"The theme {theme} is unknown.");
        }

        // Further charts: each is checked on its own; linked views and the overview belong to a single chart.
        for (var i = 0; i < spec.Charts.Count; i++)
        {
            var further = spec.Charts[i];
            problems.AddRange(Check(further).Select(problem => $"Chart {i + 2}: {problem}"));
            if (further.Charts.Count > 0)
            {
                problems.Add($"Chart {i + 2}: only the first chart of a description has further charts.");
            }
        }

        if (spec.Charts.Count > 0 && (ViewsOf(spec) is not null || spec.Charts.Exists(further => ViewsOf(further) is not null)))
        {
            problems.Add("Linked views fill a description alone; remove the further charts.");
        }

        if (spec.Charts.Count > 0 && (spec.Overview || spec.Charts.Exists(further => further.Overview)))
        {
            problems.Add("The overview goes with a single chart; remove the further charts or the overview.");
        }

        if (spec.ExportMenu && spec.Series.Exists(HasViews))
        {
            problems.Add("The export menu saves one chart; linked views leave it out.");
        }

        if (spec.Annotations.Count > 0 && spec.Series.Exists(HasViews))
        {
            problems.Add("Annotations go into a single chart; linked charts leave them out.");
        }

        foreach (var series in spec.Series)
        {
            CheckSeries(spec, series, problems);
        }

        CheckOwners(spec, problems);
        foreach (var annotation in spec.Annotations)
        {
            if (FindAnnotation(annotation.Type) is not { } info)
            {
                problems.Add($"The annotation {annotation.Type} is unknown.");
            }
            else
            {
                CheckValues(info.Name, info.Parameters, annotation.Parameters, problems);
            }
        }

        foreach (var legend in new[] { spec.Legend, spec.SecondLegend })
        {
            if (legend is not null && !Enum.TryParse<Core.Input.LegendPlacement>(legend.Placement, out _))
            {
                problems.Add($"The place {legend.Placement} of a legend is unknown.");
            }
        }

        foreach (var axis in spec.XAxes.Concat(spec.YAxes))
        {
            if (axis.ReadBreaks() is null)
            {
                problems.Add($"The scale breaks of the axis {axis.Id} must read like 10..90, 120..150.");
            }

            if (axis.Kind == AxisKind.Category && axis.ReadCategories().Length == 0)
            {
                problems.Add($"The category axis {axis.Id} needs names for its places.");
            }
        }
        if (spec.Series.Count(series => series.Type == "candles" && series.Parameters.GetFlag("volume", true)) > 1)
        {
            problems.Add("Only one candles series per chart can show its volume below; switch \"Volume below\" off for the others.");
        }
        if (SceneOf(spec) is { } scene)
        {
            CheckValues("The scene", scene.Parameters, spec.Scene, problems);
        }

        foreach (var modifier in spec.Modifiers)
        {
            if (FindModifier(modifier.Type) is not { } info)
            {
                problems.Add($"The interaction {modifier.Type} is unknown.");
                continue;
            }

            CheckValues(info.Name, info.Parameters, modifier.Parameters, problems);
        }

        return problems;
    }

    private void CheckOwners(ChartSpec spec, List<string> problems)
    {
        var owners = spec.Series.Select(series => FindType(series.Type)).Where(type => type?.OwnsAxes == true).ToList();
        if (owners.Count == 0 || spec.Series.Count < 2)
        {
            return;
        }

        if (owners.Find(type => type!.SceneApply is null) is { } alone)
        {
            problems.Add($"{alone.Name} brings its own axes and fills the chart alone; it cannot share it with other series.");
        }
        else if (owners.Count != spec.Series.Count || owners.Select(type => type!.Category).Distinct().Count() > 1)
        {
            problems.Add($"A {owners[0]!.Category} chart takes only series of its own kind.");
        }
        else if (SceneOf(spec) is null)
        {
            problems.Add($"The scene of {owners[0]!.Category} is missing.");
        }
    }

    private static void CheckValues(string owner, IReadOnlyList<ParameterInfo> parameters, SpecValues values, List<string> problems)
    {
        foreach (var name in values.Names)
        {
            if (parameters.FirstOrDefault(parameter => parameter.Name == name) is not { } parameter)
            {
                problems.Add($"{owner} has no parameter {name}.");
            }
            else if (parameter.Check(values.Get(name)) is { } problem)
            {
                problems.Add(problem);
            }
        }
    }

    private void CheckSeries(ChartSpec spec, SeriesSpec series, List<string> problems)
    {
        if (FindType(series.Type) is not { } type)
        {
            problems.Add($"The type {series.Type} is unknown.");
            return;
        }

        if (series.XAxis is { } x && spec.XAxes.All(axis => axis.Id != x))
        {
            problems.Add($"The series {series.Name} uses the unknown axis {x}.");
        }

        if (series.YAxis is { } y && spec.YAxes.All(axis => axis.Id != y))
        {
            problems.Add($"The series {series.Name} uses the unknown axis {y}.");
        }

        if (!DataSources.Kinds(type, this).Contains(series.Data.Kind))
        {
            problems.Add($"{type.Name} cannot take data from {series.Data.Kind}.");
        }
        else if (DataSources.Loader(series, type, this) is { } loader)
        {
            if (loader.Problem(series) is { } unread)
            {
                problems.Add(unread);
            }
        }
        else if (series.Data.Kind == DataKind.Csv && DataSources.CsvProblem(series, type) is { } problem)
        {
            problems.Add(problem);
        }
        else if (series.Data.Kind == DataKind.Hdf5 && DataSources.Hdf5Problem(series) is { } missing)
        {
            problems.Add(missing);
        }
        else if (series.Data.Kind == DataKind.Live && (type.Shape == DataShape.Candles ? FindSample(series.Data.Sample)?.Shape != DataShape.Candles : FindSample(series.Data.Sample)?.Curve is null))
        {
            problems.Add(type.Shape == DataShape.Candles ? $"Live prices of {series.Name} need prices as sample data." : $"Live data of {series.Name} need a curve as sample data.");
        }

        if (series.Data.Kind == DataKind.Sample && FindSample(series.Data.Sample) is { } sample && sample.Shape != type.Shape)
        {
            problems.Add($"The sample {sample.Name} does not fit {type.Name}.");
        }
        else if (series.Data.Kind is DataKind.Sample or DataKind.Live && FindSample(series.Data.Sample) is null)
        {
            problems.Add($"The sample {series.Data.Sample} is unknown.");
        }

        CheckValues(type.Name, type.Parameters, series.Parameters, problems);
    }
}
