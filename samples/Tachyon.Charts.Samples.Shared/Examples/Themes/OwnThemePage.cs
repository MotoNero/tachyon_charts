using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.Themes;

/// <summary>
/// The page „Own theme“ of the showroom: a chart in a theme of its own, read from harbor.json beside this code. The
/// same kind of theme as XAML is meadow.xaml for WPF and meadow.axaml for Avalonia; the showroom loads it into its
/// list of themes, where it colors every page.
/// </summary>
public static class OwnThemePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Own theme";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Theme from harbor.json | List of themes: Meadow from XAML, Load from file … reads an own theme | F11: the files";

    /// <summary>The name of the theme in meadow.xaml and meadow.axaml.</summary>
    public const string Meadow = "Meadow";

    /// <summary>The theme the values missing in meadow.xaml and meadow.axaml come from.</summary>
    public const string MeadowBase = "light";

    /// <summary>Gets the theme of the page, read from harbor.json. Its base gives everything the file leaves out.</summary>
    public static Theme Harbor { get; } = ThemeJson.Deserialize(Read("harbor.json")).Resolve();

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        // Lines take the colors they are given; here they come from the palette of the theme.
        var palette = Harbor.Palette!;
        c.Title("Harbor, a theme from JSON")
            .Theme(Harbor)
            .XAxis(a => a.Numeric("x").Range(0, 10).Title("Time (s)"))
            .YAxis(a => a.Numeric("y").Range(-1.5, 1.5).Title("Level"))
            .AddLine(Sample(x => Math.Sin(x * 0.9)), s => s.Name("Tide").Stroke(palette[0]).Thickness(2))
            .AddLine(Sample(x => 0.6 * Math.Cos(x * 1.7)), s => s.Name("Swell").Stroke(palette[1]).Thickness(2))
            .AddLine(Sample(x => 0.35 * Math.Sin(x * 3.1) - 0.8), s => s.Name("Ripple").Stroke(palette[2]).Thickness(1.5))
            .Legend()
            .Modifiers(WithValues);
    }

    /// <summary>Reads a file of this folder that travels with the showroom.</summary>
    /// <param name="file">The name of the file, such as <c>harbor.json</c>.</param>
    /// <returns>The text.</returns>
    public static string Read(string file) => SampleSources.Files["Tachyon.Charts.Samples.Shared/Examples/Themes/" + file];
}
