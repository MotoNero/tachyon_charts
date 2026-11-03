using System.Globalization;

namespace Tachyon.Charts.Core.Themes;

/// <summary>
/// Turns named values, such as the entries of a XAML resource dictionary, into a theme. It is the same
/// theme object as from JSON, and values that are not given come from the base theme.
/// </summary>
public static class ThemeResources
{
    /// <summary>The prefix of all keys, for example <c>Tachyon.surface.background</c>.</summary>
    public const string KeyPrefix = "Tachyon.";

    /// <summary>Creates a theme.</summary>
    /// <param name="name">The name.</param>
    /// <param name="baseName">The name of the base theme.</param>
    /// <param name="values">The values as text by key, colors as <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</param>
    /// <returns>The resolved theme.</returns>
    /// <exception cref="FormatException">A value cannot be read.</exception>
    public static Theme Create(string name, string baseName, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ThemeColor? Color(string key) => values.TryGetValue(KeyPrefix + key, out var text) ? ThemeColor.Parse(text) : null;
        double? Number(string key) => values.TryGetValue(KeyPrefix + key, out var text) ? double.Parse(text, CultureInfo.InvariantCulture) : null;

        var hasGlow = values.TryGetValue(KeyPrefix + "glow.enabled", out var glowText);
        return new Theme
        {
            Name = name,
            Base = baseName,
            Surface = new SurfaceTokens
            {
                Background = Color("surface.background"),
                PlotArea = values.TryGetValue(KeyPrefix + "surface.plotArea", out var plot) ? ThemeGradient.Parse(plot) : null,
                Border = Color("surface.border"),
            },
            Grid = new GridTokens { Major = Color("grid.major"), Minor = Color("grid.minor"), Band = Color("grid.band") },
            Axis = new AxisTokens { Line = Color("axis.line"), Tick = Color("axis.tick"), Label = Color("axis.label"), Title = Color("axis.title") },
            Text = new TextTokens { Size = Number("text.size") },
            Effects = new EffectTokens
            {
                Glow = new GlowTokens
                {
                    Enabled = hasGlow ? bool.Parse(glowText!) : null,
                    Radius = Number("glow.radius"),
                    Intensity = Number("glow.intensity"),
                },
            },
        }.Resolve();
    }
}
