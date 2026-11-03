using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Avalonia;

/// <summary>
/// Reads themes from XAML. Keys look like <c>Tachyon.surface.background</c>, see <see cref="ThemeResources"/>.
/// </summary>
public static class ChartThemes
{
    /// <summary>Creates a theme from a resource dictionary.</summary>
    /// <param name="resources">The resources: colors, brushes, numbers or text.</param>
    /// <param name="name">The name of the theme.</param>
    /// <param name="baseName">The name of the base theme.</param>
    /// <returns>The theme.</returns>
    public static Theme FromResources(IResourceDictionary resources, string name, string baseName = "dark")
    {
        ArgumentNullException.ThrowIfNull(resources);
        var values = new Dictionary<string, string>();
        foreach (var key in resources.Keys.OfType<string>().Where(key => key.StartsWith(ThemeResources.KeyPrefix, StringComparison.Ordinal)))
        {
            values[key] = ToText(resources[key]);
        }

        return ThemeResources.Create(name, baseName, values);
    }

    private static string ToText(object? value) => value switch
    {
        Color color => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}",
        ISolidColorBrush brush => ToText(brush.Color),
        double number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
