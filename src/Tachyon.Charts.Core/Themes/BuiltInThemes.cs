namespace Tachyon.Charts.Core.Themes;

/// <summary>
/// Themes shipped with Tachyon Charts.
/// </summary>
public static class BuiltInThemes
{
    private const string Black = "#000000";
    private const string White = "#FFFFFF";
    private const string MajorLine = "#FFFFFF80";
    private const string MinorLine = "#FFFFFF30";
    private const string Clear = "#00000000";

    /// <summary>Gets the Tachyon Dark theme.</summary>
    public static Theme TachyonDark { get; } = CreateDark();

    /// <summary>Gets the Tachyon Light theme.</summary>
    public static Theme TachyonLight { get; } = CreateLight();

    /// <summary>Gets the theme for the highest legibility.</summary>
    public static Theme HighContrast { get; } = new Theme
    {
        Name = "High Contrast",
        Base = "dark",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse(Black),
            PlotArea = ThemeGradient.Parse("linear(#000000, #000000)"),
            Border = ThemeColor.Parse(White),
        },
        Grid = new GridTokens { Major = ThemeColor.Parse(MajorLine), Minor = ThemeColor.Parse(MinorLine), Band = ThemeColor.Parse(Clear) },
        Axis = new AxisTokens { Line = ThemeColor.Parse(White), Tick = ThemeColor.Parse(White), Label = ThemeColor.Parse(White), Title = ThemeColor.Parse("#FFFF00") },
        Palette = [ThemeColor.Parse("#FFFF00"), ThemeColor.Parse("#00FFFF"), ThemeColor.Parse("#FF00FF"), ThemeColor.Parse("#00FF00")],
    }.Resolve();

    /// <summary>Gets the theme with a strong glow.</summary>
    public static Theme Neon { get; } = new Theme
    {
        Name = "Neon",
        Base = "dark",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse("#07040F"),
            PlotArea = ThemeGradient.Parse("linear(#120A24, #07040F)"),
            Border = ThemeColor.Parse("#3A1E6E"),
        },
        Grid = new GridTokens { Major = ThemeColor.Parse("#B388FF26"), Minor = ThemeColor.Parse("#B388FF0D"), Band = ThemeColor.Parse(Clear) },
        Axis = new AxisTokens { Line = ThemeColor.Parse("#6A3FD0"), Tick = ThemeColor.Parse("#6A3FD0"), Label = ThemeColor.Parse("#C7B5F5"), Title = ThemeColor.Parse("#F72585") },
        Palette = [ThemeColor.Parse("#4CC9F0"), ThemeColor.Parse("#F72585"), ThemeColor.Parse("#39FF14"), ThemeColor.Parse("#FFE600")],
        Effects = new EffectTokens { Glow = new GlowTokens { Enabled = true, Radius = 9, Intensity = 0.8 } },
    }.Resolve();

    /// <summary>Gets the theme for measuring instruments: a dark screen, a quiet grid, no glow.</summary>
    public static Theme Instrument { get; } = new Theme
    {
        Name = "Instrument",
        Base = "dark",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse("#0B0E12"),
            PlotArea = ThemeGradient.Parse("linear(#05070A, #05070A)"),
            Border = ThemeColor.Parse("#2C3440"),
        },
        Grid = new GridTokens { Major = ThemeColor.Parse("#FFFFFF1F"), Minor = ThemeColor.Parse("#FFFFFF0C"), Band = ThemeColor.Parse(Clear) },
        Axis = new AxisTokens { Line = ThemeColor.Parse("#5A6472"), Tick = ThemeColor.Parse("#5A6472"), Label = ThemeColor.Parse("#C8D0DA"), Title = ThemeColor.Parse("#E6EBF0") },
        Palette = [ThemeColor.Parse("#F5D63C"), ThemeColor.Parse("#46D778"), ThemeColor.Parse("#5AAAFF"), ThemeColor.Parse("#F06EAA")],
        Effects = new EffectTokens { Glow = new GlowTokens { Enabled = false } },
    }.Resolve();

    /// <summary>Gets a bright, sober theme: white, gray and one strong red as the accent.</summary>
    public static Theme Signal { get; } = new Theme
    {
        Name = "Signal",
        Base = "light",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse(White),
            PlotArea = ThemeGradient.Parse("linear(#FFFFFF, #F7F7F7)"),
            Border = ThemeColor.Parse("#C8C9CA"),
        },
        Grid = new GridTokens { Major = ThemeColor.Parse("#58595B33"), Minor = ThemeColor.Parse("#58595B14"), Band = ThemeColor.Parse(Clear) },
        Axis = new AxisTokens { Line = ThemeColor.Parse("#58595B"), Tick = ThemeColor.Parse("#58595B"), Label = ThemeColor.Parse("#333333"), Title = ThemeColor.Parse(Black) },
        Palette = [ThemeColor.Parse("#E90029"), ThemeColor.Parse("#333333"), ThemeColor.Parse("#808285"), ThemeColor.Parse("#A7A9AC")],
        Effects = new EffectTokens { Glow = new GlowTokens { Enabled = false } },
    }.Resolve();

    /// <summary>Gets the theme for printing and export.</summary>
    public static Theme Paper { get; } = new Theme
    {
        Name = "Paper",
        Base = "light",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse(White),
            PlotArea = ThemeGradient.Parse("linear(#FFFFFF, #FFFFFF)"),
            Border = ThemeColor.Parse(Black),
        },
        Grid = new GridTokens { Major = ThemeColor.Parse("#00000026"), Minor = ThemeColor.Parse("#0000000D"), Band = ThemeColor.Parse(Clear) },
        Axis = new AxisTokens { Line = ThemeColor.Parse(Black), Tick = ThemeColor.Parse(Black), Label = ThemeColor.Parse(Black), Title = ThemeColor.Parse(Black) },
        Palette = [ThemeColor.Parse(Black), ThemeColor.Parse("#555555"), ThemeColor.Parse("#999999")],
    }.Resolve();

    /// <summary>Gets all built-in themes.</summary>
    public static IReadOnlyList<Theme> All { get; } = [TachyonDark, TachyonLight, HighContrast, Neon, Paper, Instrument, Signal];

    /// <summary>
    /// Finds a built-in by its name or short base alias.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The theme, or <see langword="null"/>.</returns>
    public static Theme? Find(string name)
    {
        if (name.Equals("dark", StringComparison.OrdinalIgnoreCase) || name.Equals(TachyonDark.Name, StringComparison.OrdinalIgnoreCase))
        {
            return TachyonDark;
        }

        if (name.Equals("light", StringComparison.OrdinalIgnoreCase) || name.Equals(TachyonLight.Name, StringComparison.OrdinalIgnoreCase))
        {
            return TachyonLight;
        }

        return All?.FirstOrDefault(theme => theme.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static Theme CreateDark() => new()
    {
        Name = "Tachyon Dark",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse("#0E1117"),
            PlotArea = ThemeGradient.Parse("linear(#141922, #0E1117)"),
            Border = ThemeColor.Parse("#232A36"),
        },
        Grid = new GridTokens
        {
            Major = ThemeColor.Parse("#FFFFFF14"),
            Minor = ThemeColor.Parse("#FFFFFF08"),
            Band = ThemeColor.Parse("#FFFFFF04"),
        },
        Axis = new AxisTokens
        {
            Line = ThemeColor.Parse("#3A4352"),
            Tick = ThemeColor.Parse("#3A4352"),
            Label = ThemeColor.Parse("#9AA4B2"),
            Title = ThemeColor.Parse("#C9D1D9"),
        },
        Text = new TextTokens { FontFamily = "Roboto, Segoe UI, SF Pro, sans-serif", Size = 12 },
        Palette =
        [
            ThemeColor.Parse("#4CC9F0"), ThemeColor.Parse("#F72585"), ThemeColor.Parse("#7CFC9A"),
            ThemeColor.Parse("#FFB703"), ThemeColor.Parse("#B388FF"), ThemeColor.Parse("#FF6B6B"),
        ],
        Crosshair = new CrosshairTokens
        {
            Line = ThemeColor.Parse("#FFFFFF66"),
            LabelBackground = ThemeColor.Parse("#4CC9F0"),
            LabelText = ThemeColor.Parse("#0E1117"),
        },
        Effects = new EffectTokens
        {
            Glow = new GlowTokens { Enabled = false, Radius = 6, Intensity = 0.6 },
            SeriesOpacity = 1,
        },
        Colormap = "turbo",
        Animation = new AnimationTokens { DurationMs = 250, Easing = "cubicOut" },
    };

    private static Theme CreateLight() => new()
    {
        Name = "Tachyon Light",
        Surface = new SurfaceTokens
        {
            Background = ThemeColor.Parse("#F4F6FA"),
            PlotArea = ThemeGradient.Parse("linear(#FFFFFF, #EDF1F7)"),
            Border = ThemeColor.Parse("#C6CDD8"),
        },
        Grid = new GridTokens
        {
            Major = ThemeColor.Parse("#26364A24"),
            Minor = ThemeColor.Parse("#26364A10"),
            Band = ThemeColor.Parse("#4C78A80A"),
        },
        Axis = new AxisTokens
        {
            Line = ThemeColor.Parse("#7B8798"),
            Tick = ThemeColor.Parse("#7B8798"),
            Label = ThemeColor.Parse("#526071"),
            Title = ThemeColor.Parse("#202936"),
        },
        Text = new TextTokens { FontFamily = "Roboto, Segoe UI, SF Pro, sans-serif", Size = 12 },
        Palette =
        [
            ThemeColor.Parse("#0077B6"), ThemeColor.Parse("#D81B60"), ThemeColor.Parse("#218739"),
            ThemeColor.Parse("#C77D00"), ThemeColor.Parse("#7048B8"), ThemeColor.Parse("#D64545"),
        ],
        Crosshair = new CrosshairTokens
        {
            Line = ThemeColor.Parse("#20293666"),
            LabelBackground = ThemeColor.Parse("#0077B6"),
            LabelText = ThemeColor.Parse(White),
        },
        Effects = new EffectTokens
        {
            Glow = new GlowTokens { Enabled = false, Radius = 6, Intensity = 0.4 },
            SeriesOpacity = 1,
        },
        Colormap = "turbo",
        Animation = new AnimationTokens { DurationMs = 250, Easing = "cubicOut" },
    };
}
