using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Themes;

/// <summary>
/// A named set of design tokens. Null tokens inherit from <see cref="Base"/> or act as local
/// overrides when passed to <see cref="WithOverrides"/>.
/// </summary>
public sealed record Theme
{
    /// <summary>Gets the theme name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the name of the base theme, or <see langword="null"/>.</summary>
    public string? Base { get; init; }
    /// <summary>Gets surface tokens.</summary>
    public SurfaceTokens? Surface { get; init; }
    /// <summary>Gets grid tokens.</summary>
    public GridTokens? Grid { get; init; }
    /// <summary>Gets axis tokens.</summary>
    public AxisTokens? Axis { get; init; }
    /// <summary>Gets text tokens.</summary>
    public TextTokens? Text { get; init; }
    /// <summary>Gets the series palette.</summary>
    public ThemeColor[]? Palette { get; init; }
    /// <summary>Gets crosshair tokens.</summary>
    public CrosshairTokens? Crosshair { get; init; }
    /// <summary>Gets effect tokens.</summary>
    public EffectTokens? Effects { get; init; }
    /// <summary>Gets the colormap name.</summary>
    public string? Colormap { get; init; }
    /// <summary>Gets animation tokens.</summary>
    public AnimationTokens? Animation { get; init; }

    /// <summary>
    /// Resolves the base chain into one theme.
    /// </summary>
    /// <param name="catalog">Additional themes by name, or <see langword="null"/> for built-ins only.</param>
    /// <returns>A theme without a base reference.</returns>
    /// <exception cref="InvalidOperationException">A base is unknown or the chain contains a cycle.</exception>
    public Theme Resolve(IReadOnlyDictionary<string, Theme>? catalog = null) =>
        ResolveCore(catalog, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Applies non-null local overrides to this theme.
    /// </summary>
    /// <param name="overrides">The local values.</param>
    /// <returns>The merged theme.</returns>
    public Theme WithOverrides(Theme overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new Theme
        {
            Name = string.IsNullOrWhiteSpace(overrides.Name) ? Name : overrides.Name,
            Surface = Merge(Surface, overrides.Surface),
            Grid = Merge(Grid, overrides.Grid),
            Axis = Merge(Axis, overrides.Axis),
            Text = Merge(Text, overrides.Text),
            Palette = overrides.Palette ?? Palette,
            Crosshair = Merge(Crosshair, overrides.Crosshair),
            Effects = Merge(Effects, overrides.Effects),
            Colormap = overrides.Colormap ?? Colormap,
            Animation = Merge(Animation, overrides.Animation),
        };
    }

    /// <summary>
    /// Converts resolved surface and grid tokens to the frame-pipeline style.
    /// </summary>
    /// <returns>The surface style.</returns>
    public ChartSurfaceStyle ToSurfaceStyle()
    {
        var fallback = ChartSurfaceStyle.Default;
        return new ChartSurfaceStyle
        {
            BackgroundColor = Surface?.Background?.Value ?? fallback.BackgroundColor,
            PlotGradientStart = Surface?.PlotArea?.Start.Value ?? fallback.PlotGradientStart,
            PlotGradientEnd = Surface?.PlotArea?.End.Value ?? fallback.PlotGradientEnd,
            BorderColor = Surface?.Border?.Value ?? fallback.BorderColor,
            MajorGridColor = Grid?.Major?.Value ?? fallback.MajorGridColor,
            MinorGridColor = Grid?.Minor?.Value ?? fallback.MinorGridColor,
            BandColor = Grid?.Band?.Value ?? fallback.BandColor,
        };
    }

    private Theme ResolveCore(IReadOnlyDictionary<string, Theme>? catalog, HashSet<string> visiting)
    {
        if (Base is null)
        {
            return this with { Base = null };
        }

        if (!visiting.Add(Base))
        {
            throw new InvalidOperationException($"Theme inheritance contains a cycle at '{Base}'.");
        }

        Theme? parent = null;
        if (catalog is not null)
        {
            catalog.TryGetValue(Base, out parent);
        }

        parent ??= BuiltInThemes.Find(Base);
        if (parent is null)
        {
            throw new InvalidOperationException($"Base theme '{Base}' is unknown.");
        }

        var resolved = parent.ResolveCore(catalog, visiting).WithOverrides(this with { Base = null });
        visiting.Remove(Base);
        return resolved;
    }

    private static SurfaceTokens? Merge(SurfaceTokens? parent, SurfaceTokens? value) =>
        parent is null && value is null ? null : new SurfaceTokens
        {
            Background = value?.Background ?? parent?.Background,
            PlotArea = value?.PlotArea ?? parent?.PlotArea,
            Border = value?.Border ?? parent?.Border,
        };

    private static GridTokens? Merge(GridTokens? parent, GridTokens? value) =>
        parent is null && value is null ? null : new GridTokens
        {
            Major = value?.Major ?? parent?.Major,
            Minor = value?.Minor ?? parent?.Minor,
            Band = value?.Band ?? parent?.Band,
        };

    private static AxisTokens? Merge(AxisTokens? parent, AxisTokens? value) =>
        parent is null && value is null ? null : new AxisTokens
        {
            Line = value?.Line ?? parent?.Line,
            Tick = value?.Tick ?? parent?.Tick,
            Label = value?.Label ?? parent?.Label,
            Title = value?.Title ?? parent?.Title,
        };

    private static TextTokens? Merge(TextTokens? parent, TextTokens? value) =>
        parent is null && value is null ? null : new TextTokens
        {
            FontFamily = value?.FontFamily ?? parent?.FontFamily,
            Size = value?.Size ?? parent?.Size,
        };

    private static CrosshairTokens? Merge(CrosshairTokens? parent, CrosshairTokens? value) =>
        parent is null && value is null ? null : new CrosshairTokens
        {
            Line = value?.Line ?? parent?.Line,
            LabelBackground = value?.LabelBackground ?? parent?.LabelBackground,
            LabelText = value?.LabelText ?? parent?.LabelText,
        };

    private static EffectTokens? Merge(EffectTokens? parent, EffectTokens? value) =>
        parent is null && value is null ? null : new EffectTokens
        {
            Glow = Merge(parent?.Glow, value?.Glow),
            SeriesOpacity = value?.SeriesOpacity ?? parent?.SeriesOpacity,
        };

    private static GlowTokens? Merge(GlowTokens? parent, GlowTokens? value) =>
        parent is null && value is null ? null : new GlowTokens
        {
            Enabled = value?.Enabled ?? parent?.Enabled,
            Radius = value?.Radius ?? parent?.Radius,
            Intensity = value?.Intensity ?? parent?.Intensity,
        };

    private static AnimationTokens? Merge(AnimationTokens? parent, AnimationTokens? value) =>
        parent is null && value is null ? null : new AnimationTokens
        {
            DurationMs = value?.DurationMs ?? parent?.DurationMs,
            Easing = value?.Easing ?? parent?.Easing,
        };
}
