using System.Globalization;

namespace Tachyon.Charts.Core.Themes;

/// <summary>
/// Turns named values, such as the entries of a XAML resource dictionary, into a theme and back. It is the same
/// theme object as from JSON, and values that are not given come from the base theme.
/// </summary>
public static class ThemeResources
{
    /// <summary>The prefix of all keys, for example <c>Tachyon.surface.background</c>.</summary>
    public const string KeyPrefix = "Tachyon.";

    // Each key with what it colors or sets, how it is read and how it is taken from a theme. The order is that of the
    // documentation: surface, grid, axes, text, series, crosshair, effects, animation.
    private static readonly (string Key, string Meaning, Func<Theme, string?> Get, Func<Theme, string, Theme> Set)[] _keys =
    [
        ("surface.background", "Background of the whole chart.", t => Text(t.Surface?.Background), (t, v) => t with { Surface = (t.Surface ?? new()) with { Background = ThemeColor.Parse(v) } }),
        ("surface.plotArea", "Fill of the plot area: one color, or linear(top, bottom) for a vertical gradient.", t => t.Surface?.PlotArea?.ToString(), (t, v) => t with { Surface = (t.Surface ?? new()) with { PlotArea = ThemeGradient.Parse(v) } }),
        ("surface.border", "Border around the plot area.", t => Text(t.Surface?.Border), (t, v) => t with { Surface = (t.Surface ?? new()) with { Border = ThemeColor.Parse(v) } }),
        ("grid.major", "Major grid lines.", t => Text(t.Grid?.Major), (t, v) => t with { Grid = (t.Grid ?? new()) with { Major = ThemeColor.Parse(v) } }),
        ("grid.minor", "Minor grid lines.", t => Text(t.Grid?.Minor), (t, v) => t with { Grid = (t.Grid ?? new()) with { Minor = ThemeColor.Parse(v) } }),
        ("grid.band", "Every second band between major grid lines; fully transparent for none.", t => Text(t.Grid?.Band), (t, v) => t with { Grid = (t.Grid ?? new()) with { Band = ThemeColor.Parse(v) } }),
        ("axis.line", "Lines of the 3D box; 2D charts draw their axes with the tick color and the border.", t => Text(t.Axis?.Line), (t, v) => t with { Axis = (t.Axis ?? new()) with { Line = ThemeColor.Parse(v) } }),
        ("axis.tick", "Tick marks.", t => Text(t.Axis?.Tick), (t, v) => t with { Axis = (t.Axis ?? new()) with { Tick = ThemeColor.Parse(v) } }),
        ("axis.label", "Tick labels.", t => Text(t.Axis?.Label), (t, v) => t with { Axis = (t.Axis ?? new()) with { Label = ThemeColor.Parse(v) } }),
        ("axis.title", "Axis titles and the chart title.", t => Text(t.Axis?.Title), (t, v) => t with { Axis = (t.Axis ?? new()) with { Title = ThemeColor.Parse(v) } }),
        ("text.fontFamily", "Preferred fonts, separated by commas; not used for drawing yet.", t => t.Text?.FontFamily, (t, v) => t with { Text = (t.Text ?? new()) with { FontFamily = v } }),
        ("text.size", "Size of tick labels in device-independent pixels; titles are 3 larger.", t => Number(t.Text?.Size), (t, v) => t with { Text = (t.Text ?? new()) with { Size = ReadNumber(v) } }),
        ("palette", "Colors of instrument traces, of the series of parallel coordinates, ternary and Smith charts and of 3D series without a color of their own, separated by commas.", t => t.Palette is { } palette ? string.Join(", ", palette) : null, (t, v) => t with { Palette = [.. v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ThemeColor.Parse)] }),
        ("colormap", "Name of a colormap; not used for drawing yet, each series sets its own.", t => t.Colormap, (t, v) => t with { Colormap = v }),
        ("crosshair.line", "Crosshair lines; not used for drawing yet, the crosshair has its own colors.", t => Text(t.Crosshair?.Line), (t, v) => t with { Crosshair = (t.Crosshair ?? new()) with { Line = ThemeColor.Parse(v) } }),
        ("crosshair.labelBackground", "Background of the crosshair labels; not used for drawing yet.", t => Text(t.Crosshair?.LabelBackground), (t, v) => t with { Crosshair = (t.Crosshair ?? new()) with { LabelBackground = ThemeColor.Parse(v) } }),
        ("crosshair.labelText", "Text of the crosshair labels; not used for drawing yet.", t => Text(t.Crosshair?.LabelText), (t, v) => t with { Crosshair = (t.Crosshair ?? new()) with { LabelText = ThemeColor.Parse(v) } }),
        ("glow.enabled", "Whether lines glow: true or false.", t => t.Effects?.Glow?.Enabled is { } on ? (on ? "true" : "false") : null, (t, v) => WithGlow(t, glow => glow with { Enabled = bool.Parse(v) })),
        ("glow.radius", "Radius of the glow in pixels.", t => Number(t.Effects?.Glow?.Radius), (t, v) => WithGlow(t, glow => glow with { Radius = ReadNumber(v) })),
        ("glow.intensity", "Strength of the glow, 0 to 1.", t => Number(t.Effects?.Glow?.Intensity), (t, v) => WithGlow(t, glow => glow with { Intensity = ReadNumber(v) })),
        ("effects.seriesOpacity", "Opacity of all series, 0 to 1; not used for drawing yet.", t => Number(t.Effects?.SeriesOpacity), (t, v) => t with { Effects = (t.Effects ?? new()) with { SeriesOpacity = ReadNumber(v) } }),
        ("animation.durationMs", "Duration of animations in milliseconds; not used yet, each animation sets its own.", t => t.Animation?.DurationMs?.ToString(CultureInfo.InvariantCulture), (t, v) => t with { Animation = (t.Animation ?? new()) with { DurationMs = int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) } }),
        ("animation.easing", "Easing of animations, such as cubicOut or linear; not used yet.", t => t.Animation?.Easing, (t, v) => t with { Animation = (t.Animation ?? new()) with { Easing = v } }),
    ];

    /// <summary>Gets every key without the prefix, such as <c>surface.background</c>, with what it colors or sets.</summary>
    public static IReadOnlyDictionary<string, string> Keys { get; } = _keys.ToDictionary(item => item.Key, item => item.Meaning, StringComparer.Ordinal);

    /// <summary>Creates a theme.</summary>
    /// <param name="name">The name.</param>
    /// <param name="baseName">The name of the base theme, such as <c>dark</c>, <c>light</c> or the name of a built-in theme.</param>
    /// <param name="values">The values as text by key with the prefix, colors as <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</param>
    /// <returns>The resolved theme.</returns>
    /// <exception cref="FormatException">A key is unknown or a value cannot be read; the message names the key.</exception>
    /// <exception cref="InvalidOperationException">The base theme is unknown.</exception>
    public static Theme Create(string name, string baseName, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var theme = new Theme { Name = name, Base = baseName };
        foreach (var (key, value) in values)
        {
            var known = key.StartsWith(KeyPrefix, StringComparison.Ordinal) ? Array.FindIndex(_keys, item => item.Key == key[KeyPrefix.Length..]) : -1;
            if (known < 0)
            {
                throw new FormatException($"'{key}' is no theme key. Known keys: {string.Join(", ", _keys.Select(item => KeyPrefix + item.Key))}.");
            }

            try
            {
                theme = _keys[known].Set(theme, value.Trim());
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new FormatException($"The value '{value}' of '{key}' cannot be read: {exception.Message}", exception);
            }
        }

        return theme.Resolve();
    }

    /// <summary>Writes a theme as values by key, the counterpart of <see cref="Create"/>, for example to start a XAML theme from a built-in one.</summary>
    /// <param name="theme">The theme; values it does not set are left out.</param>
    /// <returns>The values as text by key with the prefix, in the order of <see cref="Keys"/>.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> ToValues(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var values = new List<KeyValuePair<string, string>>();
        foreach (var item in _keys)
        {
            if (item.Get(theme) is { } value)
            {
                values.Add(new(KeyPrefix + item.Key, value));
            }
        }

        return values;
    }

    private static Theme WithGlow(Theme theme, Func<GlowTokens, GlowTokens> change)
    {
        var effects = theme.Effects ?? new EffectTokens();
        return theme with { Effects = effects with { Glow = change(effects.Glow ?? new GlowTokens()) } };
    }

    private static string? Text(ThemeColor? color) => color?.ToString();

    private static string? Number(double? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static double ReadNumber(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
