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
    private const string Indent = "    ";

    // Names the generated code uses for itself, which data must not take.
    private static readonly string[] _reserved = ["chart", "x", "y", "a", "s", "h", "m", "l", "data", "values", "rows", "count", "side"];
    private readonly ChartCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>Writes <c>ChartSetup.cs</c>: the chart with the fluent API and the methods that make its sample data.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The C# file.</returns>
    /// <exception cref="InvalidOperationException">The description has problems, or a data source the generator does not write yet.</exception>
    public string Setup(ChartSpec spec, CodeOptions options)
    {
        var plan = Plan(spec, options);
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
            code.Append(", ").Append(data.Method).Append('(').Append(data.Count).Append(')');
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

        code.AppendLine(")").AppendLine("    {").Append("        chart");
        foreach (var call in plan.Calls)
        {
            code.AppendLine().Append("            ").Append(call);
        }

        code.AppendLine(";").AppendLine("    }");
        foreach (var method in plan.Data.DistinctBy(data => data.Method))
        {
            code.AppendLine();
            WriteSample(code, method);
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
        var code = new StringBuilder();
        WriteUsings(code, ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Fluent"]);
        code.Append("namespace ").Append(options.Namespace).AppendLine(";").AppendLine();
        code.AppendLine("/// <summary>The data of the chart. Append to them from any thread; the chart follows.</summary>");
        code.AppendLine("public sealed class ChartViewModel").AppendLine("{");
        foreach (var data in plan.Data)
        {
            code.Append("    /// <summary>Gets the data of ").Append(data.SeriesName).AppendLine(".</summary>");
            code.Append("    public ").Append(data.Type).Append(' ').Append(data.Property).Append(" { get; } = ChartSetup.").Append(data.Method).Append('(').Append(data.Count).AppendLine(");").AppendLine();
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

    /// <summary>Writes the window that holds the chart: <c>MainWindow.xaml</c> for WPF, <c>MainWindow.axaml</c> for Avalonia.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="options">The options.</param>
    /// <returns>The markup.</returns>
    public string WindowMarkup(ChartSpec spec, CodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var title = System.Security.SecurityElement.Escape(spec.Title.Length > 0 ? spec.Title : options.ProjectName);
        return options.Framework == UiFramework.Wpf
            ? $"""
               <Window x:Class="{options.Namespace}.MainWindow"
                       xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                       xmlns:t="urn:tachyon-charts"
                       Title="{title}" Width="1000" Height="650">
                   <t:Chart x:Name="Chart" />
               </Window>

               """
            : $"""
               <Window xmlns="https://github.com/avaloniaui"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                       xmlns:t="using:Tachyon.Charts.Avalonia"
                       x:Class="{options.Namespace}.MainWindow"
                       Title="{title}" Width="1000" Height="650">
                   <t:Chart x:Name="Chart" />
               </Window>

               """;
    }

    /// <summary>Writes the code behind the window: it creates the view model and describes the chart with it.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The C# file.</returns>
    public static string WindowCode(CodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var usings = options.Framework == UiFramework.Wpf ? "using System.Windows;\n" : "using Avalonia.Controls;\n";
        return $$"""
                 {{usings}}
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
            ["MainWindow.xaml.cs".Replace(".xaml", options.Framework == UiFramework.Wpf ? ".xaml" : ".axaml", StringComparison.Ordinal)] = WindowCode(options),
            [options.Framework == UiFramework.Wpf ? "MainWindow.xaml" : "MainWindow.axaml"] = WindowMarkup(spec, options),
            [options.ProjectName + ".csproj"] = ProjectFile(options),
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

    private static string ProjectFile(CodeOptions options)
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

        references.Append(options.LocalSource is { } source
            ? $"    <ProjectReference Include=\"{Path.Combine(source, adapter, adapter + ".csproj")}\" />\n"
            : $"    <PackageReference Include=\"{adapter}\" Version=\"{options.PackageVersion}\" />\n");
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

        if (spec.Theme is { } theme)
        {
            usings.Add("Tachyon.Charts.Core.Themes");
            calls.Add($".Theme(BuiltInThemes.{theme})");
        }

        if (spec.XDown)
        {
            calls.Add(".XDown()");
        }

        calls.AddRange(spec.XAxes.Select(axis => ".XAxis(a => a" + AxisCode(axis, "Top", usings) + ")"));
        calls.AddRange(spec.YAxes.Select(axis => ".YAxis(a => a" + AxisCode(axis, "Right", usings) + ")"));
        var data = new List<DataPlan>();
        foreach (var series in spec.Series)
        {
            if (series.Data.Kind != DataKind.Sample)
            {
                throw new InvalidOperationException($"The data of {series.Name} come from {series.Data.Kind}; the code for such sources is not written yet.");
            }

            var type = _catalog.FindType(series.Type)!;
            var sample = _catalog.FindSample(series.Data.Sample)!;
            usings.AddRange(type.Usings);
            var variable = Unique(CodeText.Identifier(series.Name, upper: false, "series"), data.Select(item => item.Variable).Concat(_reserved));
            data.Add(new DataPlan(series.Name, variable, CodeText.Identifier(variable, upper: true, "Series"), sample, Count(sample, series.Data.Count)));
            calls.Add(type.Code(series, variable, new SeriesAxes(series.XAxis ?? spec.XAxes[0].Id, series.YAxis ?? spec.YAxes[0].Id)));
        }

        if (spec.Modifiers.Count > 0)
        {
            calls.Add(".Modifiers(m => m" + string.Concat(spec.Modifiers.Select(modifier => _catalog.FindModifier(modifier.Type)!.Code(modifier.Parameters))) + ")");
        }

        if (spec.Legend is { } legend)
        {
            calls.Add(legend.Title.Length > 0 ? $".Legend(l => l.Title({CodeText.String(legend.Title)}))" : ".Legend()");
        }

        if (spec.Group is { Length: > 0 } group)
        {
            calls.Add($".Group({CodeText.String(group)})");
        }

        if (calls.Any(call => call.Contains("new Rgba8(", StringComparison.Ordinal)))
        {
            usings.Add("Tachyon.Charts.Rendering");
        }

        return new CodePlan(usings, calls, data);
    }

    private static string AxisCode(AxisSpec axis, string opposite, List<string> usings)
    {
        var code = new StringBuilder(axis.Kind switch
        {
            AxisKind.Logarithmic => $".Log({CodeText.String(axis.Id)})",
            AxisKind.DateTime => $".DateTime({CodeText.String(axis.Id)})",
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

        return code.ToString();
    }

    // The same lower bounds the generators of the preview keep.
    private static int Count(SampleInfo sample, int count) => sample.Shape == DataShape.Rows ? Math.Max(1, count) : Math.Max(2, count);

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

    private static void WriteSample(StringBuilder code, DataPlan data)
    {
        var sample = data.Sample;
        code.Append(Indent).Append("/// <summary>Makes the sample data: ").Append(System.Security.SecurityElement.Escape(sample.Name).ToLowerInvariant()).AppendLine(".</summary>");
        code.Append(Indent).AppendLine(sample.Shape == DataShape.Grid ? "/// <param name=\"side\">The columns and rows of the grid.</param>" : "/// <param name=\"count\">The number of values.</param>");
        code.Append(Indent).AppendLine("/// <returns>The data.</returns>");
        switch (sample.Shape)
        {
            case DataShape.Points:
                code.Append(Indent).Append("public static DataSource<double, double> ").Append(data.Method).AppendLine("(int count)");
                code.AppendLine("""
                        {
                            var data = new DataSource<double, double>(count);
                            for (var i = 0; i < count; i++)
                            {
                                var x = i * 10.0 / (count - 1);
                    """.TrimEnd('\n', '\r')).Append("            data.Append(x, ").Append(sample.Formula).AppendLine(");");
                code.AppendLine("        }").AppendLine().AppendLine("        return data;").AppendLine("    }");
                break;
            case DataShape.Rows:
                var rows = sample.Formula.Split(" | ");
                code.Append(Indent).Append("public static double[][] ").Append(data.Method).AppendLine("(int count)").AppendLine("    {");
                code.AppendLine("        var rows = new double[" + rows.Length + "][];");
                for (var row = 0; row < rows.Length; row++)
                {
                    code.Append("        rows[").Append(row).AppendLine("] = new double[count];");
                }

                code.AppendLine("        for (var i = 0; i < count; i++)").AppendLine("        {");
                for (var row = 0; row < rows.Length; row++)
                {
                    code.Append("            rows[").Append(row).Append("][i] = ").Append(rows[row]).AppendLine(";");
                }

                code.AppendLine("        }").AppendLine().AppendLine("        return rows;").AppendLine("    }");
                break;
            default:
                code.Append(Indent).Append("public static HeatmapData ").Append(data.Method).AppendLine("(int side)");
                code.AppendLine("""
                        {
                            var values = new float[side * side];
                            for (var row = 0; row < side; row++)
                            {
                                var v = row / (double)(side - 1);
                                for (var column = 0; column < side; column++)
                                {
                                    var u = column / (double)(side - 1);
                    """.TrimEnd('\n', '\r')).Append("                values[(row * side) + column] = (float)(").Append(sample.Formula).AppendLine(");");
                code.AppendLine("            }").AppendLine("        }").AppendLine();
                code.AppendLine("        var data = new HeatmapData(side, side);").AppendLine("        data.SetValues(values);").AppendLine("        return data;").AppendLine("    }");
                break;
        }
    }

    private sealed record CodePlan(List<string> Usings, List<string> Calls, List<DataPlan> Data);

    private sealed record DataPlan(string SeriesName, string Variable, string Property, SampleInfo Sample, int Count)
    {
        public string Method => CodeText.Identifier(Sample.Id, upper: true, "Sample");

        public string Type => Sample.Shape switch
        {
            DataShape.Points => "DataSource<double, double>",
            DataShape.Rows => "double[][]",
            _ => "HeatmapData",
        };
    }
}
