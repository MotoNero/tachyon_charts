using System.IO.Compression;
using System.Text;

namespace Tachyon.Charts.Configurator;

/// <summary>The UI framework of a generated project.</summary>
public enum UiFramework
{
    /// <summary>WPF on Windows.</summary>
    Wpf,

    /// <summary>Avalonia on Windows, Linux and macOS.</summary>
    Avalonia,
}

/// <summary>How code is generated.</summary>
/// <param name="Namespace">The namespace of the generated classes.</param>
/// <param name="Framework">The UI framework of the project.</param>
/// <param name="PackageVersion">The version of the Tachyon Charts packages the project refers to.</param>
public sealed record CodeOptions(string Namespace = "MyCharts", UiFramework Framework = UiFramework.Wpf, string PackageVersion = "0.15.0")
{
    /// <summary>
    /// Gets the <c>src</c> folder of a Tachyon Charts checkout to refer to as projects instead of packages, or
    /// <see langword="null"/> for packages.
    /// </summary>
    public string? LocalSource { get; init; }

    /// <summary>Gets the name of the project and its folder.</summary>
    public string ProjectName { get; init; } = "MyChart";
}

/// <summary>
/// Writes the code of a <see cref="ChartSpec"/>: the chart with the fluent API, a view model that holds its data, the
/// window in XAML and a complete project. Every step is written by the same metadata that builds the preview.
/// </summary>
/// <param name="catalog">The types, samples and interactions.</param>
public sealed class CodeGenerator(ChartCatalog catalog)
{
    // Names the generated code uses for itself, which data must not take.
    private static readonly string[] _reserved = ["chart", "x", "y", "a", "s", "h", "m", "l", "data", "values", "rows", "count", "side", "c"];
    private readonly ChartCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>Writes <c>ChartSetup.cs</c>: the chart with the fluent API and the methods that make its sample data.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The C# file.</returns>
    /// <exception cref="InvalidOperationException">The description has problems, or a data source the generator does not write yet.</exception>
    public string Setup(ChartSpec spec, CodeOptions options)
    {
        var plan = Plan(spec, options);
        if (_catalog.ViewsOf(spec) is { } linked)
        {
            return ViewsSetup(spec, options, plan, linked.Views);
        }

        var code = new StringBuilder();
        WriteUsings(code, plan.Usings);
        code.Append("namespace ").Append(options.Namespace).AppendLine(";").AppendLine();
        code.AppendLine("/// <summary>").Append("/// ").Append(Describe(spec)).AppendLine(", made with the Tachyon Charts configurator.").AppendLine("/// </summary>");
        code.AppendLine("public static class ChartSetup").AppendLine("{");
        code.AppendLine("    /// <summary>Describes the chart with its sample data.</summary>");
        code.AppendLine("    /// <param name=\"chart\">The chart description.</param>");
        code.Append("    public static void Configure(ChartBuilder chart) => Configure(chart");
        foreach (var data in plan.Data)
        {
            code.Append(", ").Append(data.Call);
        }

        code.AppendLine(");").AppendLine();
        code.AppendLine("    /// <summary>Describes the chart with the given data.</summary>");
        code.AppendLine("    /// <param name=\"chart\">The chart description.</param>");
        foreach (var data in plan.Data)
        {
            code.Append("    /// <param name=\"").Append(data.Variable).Append("\">The data of ").Append(data.SeriesName).AppendLine(".</param>");
        }

        code.Append("    public static void Configure(ChartBuilder chart");
        foreach (var data in plan.Data)
        {
            code.Append(", ").Append(data.Type).Append(' ').Append(data.Variable);
        }

        code.AppendLine(")").AppendLine("    {");
        foreach (var statement in plan.Setup)
        {
            code.Append("        ").AppendLine(statement);
        }

        if (plan.Setup.Count > 0)
        {
            code.AppendLine();
        }

        code.Append("        chart");
        foreach (var call in plan.Calls)
        {
            code.AppendLine().Append("            ").Append(call);
        }

        code.AppendLine(";").AppendLine("    }");
        if (spec.Annotations.Exists(annotation => annotation.Type is "band" or "box" or "text" or "line"))
        {
            code.Append(AnnotationCatalog.Helpers());
        }
        foreach (var method in plan.Data.DistinctBy(data => data.Method))
        {
            code.AppendLine();
            code.Append(method.Source.Code.TrimStart('\r', '\n'));
        }

        // Helpers of the types, each once.
        foreach (var helpers in spec.Series.Select(series => _catalog.FindType(series.Type)!.Helpers).Where(helpers => helpers.Length > 0).Distinct())
        {
            code.Append(helpers);
        }

        // Shared parts of the data: the timers of live data, the numbers of HDF5 datasets and the points of columns.
        if (spec.Series.Exists(series => series.Data.Kind == DataKind.Hdf5))
        {
            code.Append(DataSources.NumbersHelper());
        }

        if (spec.Series.Exists(series => series.Data.Kind == DataKind.Live))
        {
            code.Append(DataSources.LiveField());
        }

        if (plan.Data.Exists(data => data.Source.Code.Contains("Points(", StringComparison.Ordinal) && data.Source.Usings.Count > 0 && data.Source.Call.EndsWith("()", StringComparison.Ordinal)))
        {
            code.Append(DataSources.PointsHelper());
        }

        return code.AppendLine("}").ToString();
    }

    /// <summary>Writes <c>ChartViewModel.cs</c>: the data of the chart as properties, ready for live updates.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The C# file.</returns>
    public string ViewModel(ChartSpec spec, CodeOptions options)
    {
        var plan = Plan(spec, options);
        if (_catalog.ViewsOf(spec) is { } linked)
        {
            return ViewsModel(options, plan, linked.Views);
        }

        var code = new StringBuilder();
        WriteUsings(code, plan.Usings);
        code.Append("namespace ").Append(options.Namespace).AppendLine(";").AppendLine();
        code.AppendLine("/// <summary>The data of the chart. Append to them from any thread; the chart follows.</summary>");
        code.AppendLine("public sealed class ChartViewModel").AppendLine("{");
        foreach (var data in plan.Data)
        {
            code.Append("    /// <summary>Gets the data of ").Append(data.SeriesName).AppendLine(".</summary>");
            code.Append("    public ").Append(data.Type).Append(' ').Append(data.Property).Append(" { get; } = ChartSetup.").Append(data.Call).AppendLine(";").AppendLine();
        }

        code.AppendLine("    /// <summary>Describes the chart with the data of this view model.</summary>");
        code.AppendLine("    /// <param name=\"chart\">The chart description.</param>");
        code.Append("    public void Configure(ChartBuilder chart) => ChartSetup.Configure(chart");
        foreach (var data in plan.Data)
        {
            code.Append(", ").Append(data.Property);
        }

        return code.AppendLine(");").AppendLine("}").ToString();
    }

    /// <summary>Gets the names of the linked charts of a description, or empty for one chart.</summary>
    /// <param name="spec">The description.</param>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> ViewNames(ChartSpec spec) => _catalog.ViewsOf(spec) is { } linked ? [.. linked.Views.Select(view => view.Name)] : [];

    // The chart setup of linked charts: the data are set up once, then each chart is described from the same data.
    private string ViewsSetup(ChartSpec spec, CodeOptions options, CodePlan plan, IReadOnlyList<ViewInfo> views)
    {
        var (series, data) = (spec.Series[0], plan.Data[0]);
        var code = new StringBuilder();
        WriteUsings(code, spec.Theme is null ? plan.Usings : [.. plan.Usings, "Tachyon.Charts.Core.Themes"]);
        code.Append("namespace ").Append(options.Namespace).AppendLine(";").AppendLine();
        code.AppendLine("/// <summary>").Append("/// ").Append(Describe(spec)).AppendLine(", linked charts made with the Tachyon Charts configurator.").AppendLine("/// </summary>");
        code.AppendLine("public static class ChartSetup").AppendLine("{");
        code.AppendLine("    /// <summary>Sets the data up once for all charts.</summary>");
        code.Append("    /// <param name=\"").Append(data.Variable).AppendLine("\">The data.</param>");
        code.Append("    public static void Prepare(").Append(data.Type).Append(' ').Append(data.Variable).AppendLine(")").AppendLine("    {");
        foreach (var statement in plan.Setup)
        {
            code.Append("        ").AppendLine(statement);
        }

        code.AppendLine("    }");
        foreach (var view in views)
        {
            code.AppendLine();
            code.Append("    /// <summary>Describes the chart ").Append(view.Name).AppendLine(".</summary>");
            code.AppendLine("    /// <param name=\"chart\">The chart description.</param>");
            code.Append("    /// <param name=\"").Append(data.Variable).AppendLine("\">The data, the same for all charts.</param>");
            code.Append("    public static void Configure").Append(view.Name).Append("(ChartBuilder chart, ").Append(data.Type).Append(' ').Append(data.Variable).AppendLine(")").AppendLine("    {");
            if (spec.Theme is { } theme)
            {
                code.Append("        chart.Theme(BuiltInThemes.").Append(theme).AppendLine(");");
            }

            foreach (var line in view.Code(series, data.Variable).Split('\n'))
            {
                code.Append("        ").AppendLine(line.TrimEnd('\r'));
            }

            code.AppendLine("    }");
        }

        code.AppendLine();
        code.Append(data.Source.Code.TrimStart('\r', '\n'));
        if (_catalog.FindType(series.Type)!.Helpers is { Length: > 0 } helpers)
        {
            code.Append(helpers);
        }

        return code.AppendLine("}").ToString();
    }

    // The view model of linked charts: the shared data, set up once, and a method per chart.
    private static string ViewsModel(CodeOptions options, CodePlan plan, IReadOnlyList<ViewInfo> views)
    {
        var data = plan.Data[0];
        var code = new StringBuilder();
        WriteUsings(code, plan.Usings);
        code.Append("namespace ").Append(options.Namespace).AppendLine(";").AppendLine();
        code.AppendLine("/// <summary>The data the linked charts share; a change in one chart shows in the others.</summary>");
        code.AppendLine("public sealed class ChartViewModel").AppendLine("{");
        code.AppendLine("    /// <summary>Initializes the view model and sets the data up.</summary>");
        code.Append("    public ChartViewModel() => ChartSetup.Prepare(").Append(data.Property).AppendLine(");").AppendLine();
        code.Append("    /// <summary>Gets the data of ").Append(data.SeriesName).AppendLine(".</summary>");
        code.Append("    public ").Append(data.Type).Append(' ').Append(data.Property).Append(" { get; } = ChartSetup.").Append(data.Call).AppendLine(";");
        foreach (var view in views)
        {
            code.AppendLine();
            code.Append("    /// <summary>Describes the chart ").Append(view.Name).AppendLine(".</summary>");
            code.AppendLine("    /// <param name=\"chart\">The chart description.</param>");
            code.Append("    public void Configure").Append(view.Name).Append("(ChartBuilder chart) => ChartSetup.Configure").Append(view.Name).Append("(chart, ").Append(data.Property).AppendLine(");");
        }

        return code.AppendLine("}").ToString();
    }

    /// <summary>Writes the window that holds the chart: <c>MainWindow.xaml</c> for WPF, <c>MainWindow.axaml</c> for Avalonia.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The markup.</returns>
    public string WindowMarkup(ChartSpec spec, CodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var title = System.Security.SecurityElement.Escape(spec.Title.Length > 0 ? spec.Title : options.ProjectName);
        var charts = "<t:Chart x:Name=\"Chart\" />";
        if (_catalog.ViewsOf(spec) is { } linked)
        {
            // The linked charts side by side, named after their views.
            charts = $"<UniformGrid Columns=\"{linked.Columns}\">\n" + string.Concat(linked.Views.Select(view => $"        <t:Chart x:Name=\"{view.Name}Chart\" />\n")) + "    </UniformGrid>";
        }

        return options.Framework == UiFramework.Wpf
            ? $"""
               <Window x:Class="{options.Namespace}.MainWindow"
                       xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                       xmlns:t="urn:tachyon-charts"
                       Title="{title}" Width="1000" Height="650">
                   {charts}
               </Window>

               """
            : $"""
               <Window xmlns="https://github.com/avaloniaui"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                       xmlns:t="using:Tachyon.Charts.Avalonia"
                       x:Class="{options.Namespace}.MainWindow"
                       Title="{title}" Width="1000" Height="650">
                   {charts}
               </Window>

               """;
    }

    /// <summary>Writes the code behind the window: it creates the view model and describes the chart with it.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The C# file.</returns>
    public static string WindowCode(CodeOptions options) => WindowCode(new ChartSpec(), options);

    /// <summary>
    /// Writes the code behind the window: it creates the view model and describes the chart with it, and with
    /// <see cref="ChartSpec.ExportMenu"/> it gives the chart a context menu that saves it.
    /// </summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The C# file.</returns>
    public static string WindowCode(ChartSpec spec, CodeOptions options) => WindowCode(spec, options, []);

    /// <summary>Writes the code behind the window of linked charts: each chart is described by its method of the view model.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <param name="views">The names of the linked charts, or empty for one chart.</param>
    /// <returns>The C# file.</returns>
    public static string WindowCode(ChartSpec spec, CodeOptions options, IReadOnlyList<string> views)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(views);
        var wpf = options.Framework == UiFramework.Wpf;
        if (views.Count > 0)
        {
            return $$"""
                     {{(wpf ? "using System.Windows;" : "using Avalonia.Controls;")}}

                     namespace {{options.Namespace}};

                     /// <summary>The window with the linked charts.</summary>
                     public partial class MainWindow : Window
                     {
                         /// <summary>Initializes the window and its charts.</summary>
                         public MainWindow()
                         {
                             InitializeComponent();
                             var model = new ChartViewModel();
                             DataContext = model;
                     {{string.Concat(views.Select(view => $"        {view}Chart.Configure(model.Configure{view});\n"))}}    }
                     }

                     """;
        }

        if (!spec.ExportMenu)
        {
            return $$"""
                     {{(wpf ? "using System.Windows;" : "using Avalonia.Controls;")}}

                     namespace {{options.Namespace}};

                     /// <summary>The window with the chart.</summary>
                     public partial class MainWindow : Window
                     {
                         /// <summary>Initializes the window and its chart.</summary>
                         public MainWindow()
                         {
                             InitializeComponent();
                             var model = new ChartViewModel();
                             DataContext = model;
                             Chart.Configure(model.Configure);
                         }
                     }

                     """;
        }

        return wpf ? $$"""
            using System.IO;
            using System.Windows;
            using System.Windows.Controls;
            using Microsoft.Win32;

            namespace {{options.Namespace}};

            /// <summary>The window with the chart; a right click on the chart saves it.</summary>
            public partial class MainWindow : Window
            {
                /// <summary>Initializes the window and its chart.</summary>
                public MainWindow()
                {
                    InitializeComponent();
                    var model = new ChartViewModel();
                    DataContext = model;
                    Chart.Configure(model.Configure);
                    Chart.ContextMenu = ExportMenu();
                }

                // Saves the chart as an image or a document, its data as a table, or records a video.
                private ContextMenu ExportMenu()
                {
                    var menu = new ContextMenu();
                    Add(menu, "Save as PNG …", "PNG image|*.png", () => Chart.ExportPngAsync());
                    Add(menu, "Save as SVG …", "SVG drawing|*.svg", () => Chart.ExportSvgAsync());
                    Add(menu, "Save as PDF …", "PDF document|*.pdf", () => Chart.ExportPdfAsync());
                    Add(menu, "Save data as CSV …", "CSV table|*.csv", () => Chart.ExportCsvAsync());
                    var video = new MenuItem { Header = "Record video …" };
                    video.Click += async (_, _) =>
                    {
                        if (Chart.IsRecording)
                        {
                            await Chart.StopRecordingAsync();
                            video.Header = "Record video …";
                            return;
                        }

                        var dialog = new SaveFileDialog { Filter = "MP4 video|*.mp4" };
                        if (dialog.ShowDialog(this) == true)
                        {
                            Chart.StartRecording(dialog.FileName);
                            video.Header = "Stop recording";
                        }
                    };
                    menu.Items.Add(video);
                    return menu;
                }

                private void Add(ContextMenu menu, string header, string filter, Func<Task<byte[]>> export)
                {
                    var item = new MenuItem { Header = header };
                    item.Click += async (_, _) =>
                    {
                        var dialog = new SaveFileDialog { Filter = filter };
                        if (dialog.ShowDialog(this) == true)
                        {
                            await File.WriteAllBytesAsync(dialog.FileName, await export());
                        }
                    };
                    menu.Items.Add(item);
                }
            }

            """ : $$"""
            using Avalonia.Controls;
            using Avalonia.Platform.Storage;

            namespace {{options.Namespace}};

            /// <summary>The window with the chart; a right click on the chart saves it.</summary>
            public partial class MainWindow : Window
            {
                /// <summary>Initializes the window and its chart.</summary>
                public MainWindow()
                {
                    InitializeComponent();
                    var model = new ChartViewModel();
                    DataContext = model;
                    Chart.Configure(model.Configure);
                    Chart.ContextMenu = ExportMenu();
                }

                // Saves the chart as an image or a document, its data as a table, or records a video.
                private ContextMenu ExportMenu()
                {
                    var items = new List<MenuItem>
                    {
                        Item("Save as PNG …", "chart.png", () => Chart.ExportPngAsync()),
                        Item("Save as SVG …", "chart.svg", () => Chart.ExportSvgAsync()),
                        Item("Save as PDF …", "chart.pdf", () => Chart.ExportPdfAsync()),
                        Item("Save data as CSV …", "chart.csv", () => Chart.ExportCsvAsync()),
                    };
                    var video = new MenuItem { Header = "Record video …" };
                    video.Click += async (_, _) =>
                    {
                        if (Chart.IsRecording)
                        {
                            await Chart.StopRecordingAsync();
                            video.Header = "Record video …";
                            return;
                        }

                        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "chart.mp4" });
                        if (file?.TryGetLocalPath() is { } path)
                        {
                            Chart.StartRecording(path);
                            video.Header = "Stop recording";
                        }
                    };
                    items.Add(video);
                    return new ContextMenu { ItemsSource = items };
                }

                private MenuItem Item(string header, string name, Func<Task<byte[]>> export)
                {
                    var item = new MenuItem { Header = header };
                    item.Click += async (_, _) =>
                    {
                        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = name });
                        if (file is not null)
                        {
                            await using var stream = await file.OpenWriteAsync();
                            await stream.WriteAsync(await export());
                        }
                    };
                    return item;
                }
            }

            """;
    }

    /// <summary>Writes a complete project: project file, application, window, view model and chart.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The files by their path inside the project folder.</returns>
    public IReadOnlyDictionary<string, string> Project(ChartSpec spec, CodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["ChartSetup.cs"] = Setup(spec, options),
            ["ChartViewModel.cs"] = ViewModel(spec, options),
            ["MainWindow.xaml.cs".Replace(".xaml", options.Framework == UiFramework.Wpf ? ".xaml" : ".axaml", StringComparison.Ordinal)] = WindowCode(spec, options, ViewNames(spec)),
            [options.Framework == UiFramework.Wpf ? "MainWindow.xaml" : "MainWindow.axaml"] = WindowMarkup(spec, options),
            [options.ProjectName + ".csproj"] = ProjectFile(options, _catalog.SceneOf(spec), spec.Series.Exists(series => series.Data.Kind == DataKind.Hdf5), Packages(spec), [.. spec.Series.SelectMany(series => _catalog.FindType(series.Type)!.Packages).Distinct()]),
        };
        foreach (var (path, content) in ApplicationFiles(options))
        {
            files[path] = content;
        }

        return files;
    }

    /// <summary>Writes the project into a folder.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <param name="folder">The folder; created when missing.</param>
    public void WriteFolder(ChartSpec spec, CodeOptions options, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Directory.CreateDirectory(folder);
        foreach (var (path, content) in Project(spec, options))
        {
            File.WriteAllText(Path.Combine(folder, path), content, new UTF8Encoding(false));
        }
    }

    /// <summary>Writes the project as a ZIP archive with a folder named after the project.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <param name="target">The stream to write to.</param>
    public void WriteZip(ChartSpec spec, CodeOptions options, Stream target)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (path, content) in Project(spec, options))
        {
            using var writer = new StreamWriter(archive.CreateEntry(options.ProjectName + "/" + path).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }

    private static string Describe(ChartSpec spec) => spec.Title.Length > 0 ? "The chart \"" + System.Security.SecurityElement.Escape(spec.Title) + "\"" : "A chart";

    private static void WriteUsings(StringBuilder code, IEnumerable<string> usings)
    {
        foreach (var name in usings.Distinct().Order(StringComparer.Ordinal))
        {
            code.Append("using ").Append(name).AppendLine(";");
        }

        code.AppendLine();
    }

    // The packages the readers of the data need, each once.
    private IReadOnlyList<string> Packages(ChartSpec spec) =>
        [.. spec.Series.Select(series => DataSources.Loader(series, _catalog.FindType(series.Type)!, _catalog)).OfType<DataLoader>().SelectMany(loader => loader.Packages).Where(package => package.IsOwn).Select(package => package.Name).Distinct()];

    private static string ProjectFile(CodeOptions options, SceneInfo? scene, bool readsHdf5, IReadOnlyList<string> packages, IReadOnlyList<(string Name, string Version)> typePackages)
    {
        var wpf = options.Framework == UiFramework.Wpf;
        var adapter = wpf ? "Tachyon.Charts.Wpf" : "Tachyon.Charts.Avalonia";
        var references = new StringBuilder();
        if (!wpf)
        {
            foreach (var package in new[] { "Avalonia", "Avalonia.Desktop", "Avalonia.Themes.Fluent" })
            {
                references.Append("    <PackageReference Include=\"").Append(package).AppendLine("\" Version=\"12.1.4\" />");
            }
        }

        foreach (var (package, version) in scene is null ? [(adapter, options.PackageVersion)] : new[] { (adapter, options.PackageVersion), (scene.Package, scene.PackageVersion) })
        {
            references.Append(options.LocalSource is { } source
                ? $"    <ProjectReference Include=\"{Path.Combine(source, package, package + ".csproj")}\" />\n"
                : $"    <PackageReference Include=\"{package}\" Version=\"{version}\" />\n");
        }

        // Packages of Tachyon Charts the readers need, in the version of the scene (the 3D packages) or of the release.
        foreach (var package in packages)
        {
            references.Append(options.LocalSource is { } local
                ? $"    <ProjectReference Include=\"{Path.Combine(local, package, package + ".csproj")}\" />\n"
                : $"    <PackageReference Include=\"{package}\" Version=\"{scene?.PackageVersion ?? options.PackageVersion}\" />\n");
        }

        // Packages of the types themselves, such as the medical add-on, in their own versions.
        foreach (var (package, version) in typePackages)
        {
            references.Append(options.LocalSource is { } own
                ? $"    <ProjectReference Include=\"{Path.Combine(own, package, package + ".csproj")}\" />\n"
                : $"    <PackageReference Include=\"{package}\" Version=\"{version}\" />\n");
        }

        if (readsHdf5)
        {
            // The reader of HDF5 files, a package without native parts.
            references.Append("    <PackageReference Include=\"PureHDF\" Version=\"2.2.0\" />\n");
        }

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <OutputType>WinExe</OutputType>
                    <TargetFramework>{(wpf ? "net10.0-windows" : "net10.0")}</TargetFramework>
                {(wpf ? "    <UseWPF>true</UseWPF>\n" : string.Empty)}    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <RootNamespace>{options.Namespace}</RootNamespace>
                {(wpf ? string.Empty : "    <ApplicationManifest>app.manifest</ApplicationManifest>\n")}  </PropertyGroup>

                  <ItemGroup>
                {references}  </ItemGroup>

                </Project>

                """;
    }

    private static IEnumerable<(string Path, string Content)> ApplicationFiles(CodeOptions options)
    {
        if (options.Framework == UiFramework.Wpf)
        {
            yield return ("App.xaml", $"""
                <Application x:Class="{options.Namespace}.App"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             StartupUri="MainWindow.xaml" />

                """);
            yield return ("App.xaml.cs", $$"""
                using System.Windows;

                namespace {{options.Namespace}};

                /// <summary>The application.</summary>
                public partial class App : Application
                {
                }

                """);
            yield break;
        }

        // On Windows the native host of the chart needs a declared version of Windows for its child windows.
        yield return ("app.manifest", """
            <?xml version="1.0" encoding="utf-8"?>
            <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
              <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
                <application>
                  <!-- Windows 10 and 11 -->
                  <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
                </application>
              </compatibility>
              <application xmlns="urn:schemas-microsoft-com:asm.v3">
                <windowsSettings>
                  <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
                  <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
                </windowsSettings>
              </application>
            </assembly>

            """);
        yield return ("App.axaml", $"""
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="{options.Namespace}.App">
                <Application.Styles>
                    <FluentTheme />
                </Application.Styles>
            </Application>

            """);
        yield return ("App.axaml.cs", $$"""
            using Avalonia;
            using Avalonia.Controls.ApplicationLifetimes;
            using Avalonia.Markup.Xaml;

            namespace {{options.Namespace}};

            /// <summary>The application.</summary>
            public partial class App : Application
            {
                /// <inheritdoc />
                public override void Initialize() => AvaloniaXamlLoader.Load(this);

                /// <inheritdoc />
                public override void OnFrameworkInitializationCompleted()
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.MainWindow = new MainWindow();
                    }

                    base.OnFrameworkInitializationCompleted();
                }
            }

            """);
        yield return ("Program.cs", $$"""
            using Avalonia;

            namespace {{options.Namespace}};

            /// <summary>Starts the application.</summary>
            public static class Program
            {
                /// <summary>Starts the application.</summary>
                /// <param name="args">The arguments of the command line.</param>
                [STAThread]
                public static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
            }

            """);
    }

    // The order of the calls follows SpecChartBuilder.Apply, so preview and code take the same steps.
    private CodePlan Plan(ChartSpec spec, CodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        if (_catalog.Check(spec) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        var usings = new List<string> { "Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Fluent" };
        var calls = new List<string>();
        if (spec.Title.Length > 0)
        {
            calls.Add($".Title({CodeText.String(spec.Title)})");
        }

        var scene = _catalog.SceneOf(spec);
        if (spec.Theme is { } theme && scene is null)
        {
            usings.Add("Tachyon.Charts.Core.Themes");
            calls.Add($".Theme(BuiltInThemes.{theme})");
        }

        if (spec.XDown)
        {
            calls.Add(".XDown()");
        }

        if (!_catalog.OwnsAxes(spec))
        {
            calls.AddRange(spec.XAxes.Select(axis => ".XAxis(a => a" + AxisCode(axis, "Top", usings) + ")"));
            calls.AddRange(spec.YAxes.Select(axis => ".YAxis(a => a" + AxisCode(axis, "Right", usings) + ")"));
        }

        var data = new List<DataPlan>();
        var inScene = new List<string>();
        var setup = new List<string>();
        foreach (var series in DataSources.Expanded(spec.Series, _catalog))
        {
            var type = _catalog.FindType(series.Type)!;
            var sample = _catalog.FindSample(series.Data.Sample) ?? _catalog.FindSample(type.DefaultSample)!;
            usings.AddRange(type.Usings);
            var variable = Unique(CodeText.Identifier(series.Name, upper: false, "series"), data.Select(item => item.Variable).Concat(_reserved));
            DataCode source;
            if (series.Data.Kind == DataKind.Sample)
            {
                var method = CodeText.Identifier(sample.Id, upper: true, "Sample");
                source = new DataCode(sample.ReturnType, method, $"{method}({Count(sample, series.Data.Count)})", sample.Method(method), []);
            }
            else
            {
                source = DataSources.Code(series, type, _catalog, Unique(CodeText.Identifier(variable, upper: true, "Series") + series.Data.Kind, data.Select(item => item.Source.Method)));
            }

            usings.AddRange(source.Usings);
            data.Add(new DataPlan(series.Name, variable, CodeText.Identifier(variable, upper: true, "Series"), source));
            if (type.PrepareCode is { } prepare)
            {
                setup.AddRange(prepare(series, variable));
            }

            (scene is null ? calls : inScene).Add(type.Code(series, variable, new SeriesAxes(series.XAxis ?? spec.XAxes[0].Id, series.YAxis ?? spec.YAxes[0].Id)));
        }

        if (scene is not null)
        {
            usings.AddRange(scene.Usings);
            var (start, end) = scene.Code(spec);
            calls.Add(start + string.Concat(inScene) + end);
            if (spec.Theme is { } chosen)
            {
                usings.Add("Tachyon.Charts.Core.Themes");
                calls.Add($".Theme(BuiltInThemes.{chosen})");
            }
        }

        if (spec.Modifiers.Count > 0)
        {
            calls.Add(".Modifiers(m => m" + string.Concat(spec.Modifiers.Select(modifier => _catalog.FindModifier(modifier.Type)!.Code(modifier.Parameters))) + ")");
        }

        var isEditable = spec.Modifiers.Exists(modifier => modifier.Type == "annotationedit");
        foreach (var annotation in spec.Annotations)
        {
            usings.Add("Tachyon.Charts.Core.Annotations");
            calls.Add($".AddOverlay(c => {AnnotationCatalog.Expression(ChartCatalog.FindAnnotation(annotation.Type)!, annotation.Parameters, isEditable)})");
        }

        foreach (var legend in new[] { spec.Legend, spec.SecondLegend })
        {
            if (legend is null)
            {
                continue;
            }

            var parts = (legend.Title.Length > 0 ? $".Title({CodeText.String(legend.Title)})" : string.Empty)
                + (legend.Placement != "TopRight" ? $".Place(LegendPlacement.{legend.Placement})" : string.Empty)
                + (legend.ReadSeries() is { Length: > 0 } names ? $".Series([{string.Join(", ", names.Select(CodeText.String))}])" : string.Empty);
            if (legend.Placement != "TopRight")
            {
                usings.Add("Tachyon.Charts.Core.Input");
            }

            calls.Add(parts.Length > 0 ? $".Legend(l => l{parts})" : ".Legend()");
        }

        if (spec.Group is { Length: > 0 } group && scene is null)
        {
            calls.Add($".Group({CodeText.String(group)})");
        }

        if (calls.Any(call => call.Contains("new Rgba8(", StringComparison.Ordinal)) || spec.Annotations.Exists(annotation => annotation.Type is "band" or "box" or "text" or "line"))
        {
            usings.Add("Tachyon.Charts.Rendering");
        }

        return new CodePlan(usings, calls, data, setup);
    }

    private static string AxisCode(AxisSpec axis, string opposite, List<string> usings)
    {
        var code = new StringBuilder(axis.Kind switch
        {
            AxisKind.Logarithmic => $".Log({CodeText.String(axis.Id)})",
            AxisKind.DateTime => $".DateTime({CodeText.String(axis.Id)})",
            AxisKind.Category => $".Category([{string.Join(", ", axis.ReadCategories().Select(CodeText.String))}], {CodeText.String(axis.Id)})",
            _ => $".Numeric({CodeText.String(axis.Id)})",
        });
        if (axis.Title.Length > 0)
        {
            code.Append(".Title(").Append(CodeText.String(axis.Title)).Append(')');
        }

        code.Append(axis.Minimum is { } minimum && axis.Maximum is { } maximum
            ? $".Range({CodeText.Number(minimum)}, {CodeText.Number(maximum)})"
            : ".AutoRange()");
        if (axis.IsInverted)
        {
            code.Append(".Inverted()");
        }

        if (axis.IsOpposite)
        {
            usings.Add("Tachyon.Charts.Core.Axes");
            code.Append(".Position(AxisPosition.").Append(opposite).Append(')');
        }

        if (axis.SkipsWeekends && axis.Kind == AxisKind.DateTime)
        {
            usings.Add("Tachyon.Charts.Core.Axes");
            code.Append(".Gaps(TimeGapMap.Weekends(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc))");
        }

        if (axis.CrossesZero)
        {
            code.Append(".CrossesAt(0)");
        }

        if (axis.ReadBreaks() is { Count: > 0 } breaks)
        {
            usings.Add("Tachyon.Charts.Core.Numerics");
            code.Append(".Breaks(").Append(string.Join(", ", breaks.Select(range => $"new DoubleRange({CodeText.Number(range.From)}, {CodeText.Number(range.To)})"))).Append(')');
        }

        if (axis.Pane > 0 || axis.PaneWeight != 1)
        {
            code.Append(".Pane(").Append(axis.Pane.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(", ").Append(CodeText.Number(axis.PaneWeight)).Append(')');
        }

        if (axis.IsHidden)
        {
            code.Append(".Hidden()");
        }

        return code.ToString();
    }

    // The same lower bound the preview keeps.
    private static int Count(SampleInfo sample, int count) => Math.Max(sample.MinimumCount, count);

    private static string Unique(string name, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var unique = name;
        for (var i = 2; used.Contains(unique); i++)
        {
            unique = name + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return unique;
    }

    private sealed record CodePlan(List<string> Usings, List<string> Calls, List<DataPlan> Data, List<string> Setup);

    private sealed record DataPlan(string SeriesName, string Variable, string Property, DataCode Source)
    {
        public string Method => Source.Method;

        public string Type => Source.Type;

        public string Call => Source.Call;
    }
}
