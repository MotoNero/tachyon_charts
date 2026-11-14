using System.Text.RegularExpressions;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>A source file that a showroom page is built from.</summary>
/// <param name="Name">The path of the file below <c>samples/</c>, with forward slashes.</param>
/// <param name="Text">The content.</param>
public sealed record SourceFile(string Name, string Text);

/// <summary>
/// Finds the code behind each page of the showroom. Each example lives in a file of its own under <c>Examples/</c> and
/// names its page in <c>Name</c>; the shared helpers under <c>Examples/Common/</c> that it uses follow as further files.
/// </summary>
public static class SampleSources
{
    private const string Prefix = "Source/";
    private const string ExampleFolder = "Tachyon.Charts.Samples.Shared/Examples/";
    private const string CommonFolder = ExampleFolder + "Common/";

    // The sources are short and the patterns simple; the limit only guards against a pattern gone wrong.
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromSeconds(1);
    private static readonly Lazy<Dictionary<string, string>> _files = new(Load);

    /// <summary>Gets all files by their path below <c>samples/</c>, such as <c>Tachyon.Charts.Samples.Shared/Examples/ThreeD/WaterfallLivePage.cs</c>.</summary>
    public static IReadOnlyDictionary<string, string> Files => _files.Value;

    /// <summary>Gets the files of the examples, one per page or gallery scene, without the shared helpers and the files they read.</summary>
    public static IEnumerable<string> Examples =>
        Files.Keys.Where(name => name.StartsWith(ExampleFolder, StringComparison.Ordinal) && !name.StartsWith(CommonFolder, StringComparison.Ordinal) && name.EndsWith(".cs", StringComparison.Ordinal));

    /// <summary>Finds the files of a page: its example first, then the shared helpers it uses.</summary>
    /// <param name="pageId">The page.</param>
    /// <returns>The files, empty for a heading or an unknown page.</returns>
    public static IReadOnlyList<SourceFile> Find(string pageId)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        var declaration = "public const string Name = \"" + pageId + "\";";
        var example = Examples.FirstOrDefault(name => Files[name].Contains(declaration, StringComparison.Ordinal));

        // A scene of the gallery is a class; a few classes draw several scenes with other arguments.
        if (example is null && SceneCatalog.Find(pageId) is { } scene)
        {
            example = Examples.FirstOrDefault(name => Regex.IsMatch(Files[name], @"(class|record) " + scene.GetType().Name + @"\b", RegexOptions.None, _matchTimeout));
        }

        if (example is null)
        {
            return [];
        }

        var found = new List<SourceFile> { new(example, Files[example]) };

        // The files an example reads, such as a theme as JSON or XAML, beside it in its folder.
        var folder = example[..(example.LastIndexOf('/') + 1)];
        foreach (var file in Files.Keys.Where(name => name.StartsWith(folder, StringComparison.Ordinal) && !name.EndsWith(".cs", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            if (Files[example].Contains(file[folder.Length..], StringComparison.Ordinal))
            {
                found.Add(new SourceFile(file, Files[file]));
            }
        }

        AddHelpers(found, Files[example]);
        return found;
    }

    // The helpers a file names or imports with using static, and the helpers those use.
    private static void AddHelpers(List<SourceFile> found, string text)
    {
        var code = string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("using ", StringComparison.Ordinal)));
        foreach (var file in Files.Keys.Where(name => name.StartsWith(CommonFolder, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var type = Path.GetFileNameWithoutExtension(file);
            if (found.TrueForAll(item => item.Name != file)
                && (Regex.IsMatch(code, @"\b" + type + @"\b", RegexOptions.None, _matchTimeout) || text.Contains("using static Tachyon.Charts.Samples.Shared.Examples.Common." + type + ";", StringComparison.Ordinal)))
            {
                found.Add(new SourceFile(file, Files[file]));
                AddHelpers(found, Files[file]);
            }
        }
    }

    private static Dictionary<string, string> Load()
    {
        var assembly = typeof(SampleSources).Assembly;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files["Tachyon.Charts.Samples." + name[Prefix.Length..].Replace('\\', '/')] = reader.ReadToEnd();
        }

        return files;
    }

}
