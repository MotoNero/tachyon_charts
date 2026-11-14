using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// Reads themes from XAML. Keys look like <c>Tachyon.surface.background</c>, see <see cref="ThemeResources"/>.
/// </summary>
public static class ChartThemes
{
    /// <summary>Creates a theme from a resource dictionary.</summary>
    /// <param name="resources">The resources, with their merged dictionaries: colors, brushes, numbers or text. A linear gradient brush gives the plot area its first and last color.</param>
    /// <param name="name">The name of the theme.</param>
    /// <param name="baseName">The name of the base theme.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="FormatException">A key starting with <c>Tachyon.</c> is unknown or its value cannot be read.</exception>
    public static Theme FromResources(ResourceDictionary resources, string name, string baseName = "dark")
    {
        ArgumentNullException.ThrowIfNull(resources);
        var values = new Dictionary<string, string>();
        Collect(resources, values);
        return ThemeResources.Create(name, baseName, values);
    }

    // The merged dictionaries first, so that a value of the dictionary itself wins, as in a lookup of a resource.
    private static void Collect(ResourceDictionary resources, Dictionary<string, string> values)
    {
        foreach (var merged in resources.MergedDictionaries)
        {
            Collect(merged, values);
        }

        foreach (var key in resources.Keys.OfType<string>().Where(key => key.StartsWith(ThemeResources.KeyPrefix, StringComparison.Ordinal)))
        {
            values[key] = ToText(resources[key]);
        }
    }

    private static string ToText(object value) => value switch
    {
        Color color => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}",
        SolidColorBrush brush => ToText(brush.Color),
        LinearGradientBrush { GradientStops.Count: >= 2 } gradient => $"linear({ToText(gradient.GradientStops[0].Color)}, {ToText(gradient.GradientStops[^1].Color)})",
        double number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
