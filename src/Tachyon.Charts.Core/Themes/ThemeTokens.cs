namespace Tachyon.Charts.Core.Themes;

/// <summary>Surface design tokens.</summary>
public sealed record SurfaceTokens
{
    /// <summary>Gets the chart background.</summary>
    public ThemeColor? Background { get; init; }
    /// <summary>Gets the plot-area fill.</summary>
    public ThemeGradient? PlotArea { get; init; }
    /// <summary>Gets the plot border.</summary>
    public ThemeColor? Border { get; init; }
}

/// <summary>Grid design tokens.</summary>
public sealed record GridTokens
{
    /// <summary>Gets the major grid color.</summary>
    public ThemeColor? Major { get; init; }
    /// <summary>Gets the minor grid color.</summary>
    public ThemeColor? Minor { get; init; }
    /// <summary>Gets the alternating band color.</summary>
    public ThemeColor? Band { get; init; }
}

/// <summary>Axis design tokens.</summary>
public sealed record AxisTokens
{
    /// <summary>Gets the axis-line color.</summary>
    public ThemeColor? Line { get; init; }
    /// <summary>Gets the tick color.</summary>
    public ThemeColor? Tick { get; init; }
    /// <summary>Gets the label color.</summary>
    public ThemeColor? Label { get; init; }
    /// <summary>Gets the title color.</summary>
    public ThemeColor? Title { get; init; }
}

/// <summary>Text design tokens.</summary>
public sealed record TextTokens
{
    /// <summary>Gets the preferred font-family list.</summary>
    public string? FontFamily { get; init; }
    /// <summary>Gets the base font size in device-independent units.</summary>
    public double? Size { get; init; }
}

/// <summary>Crosshair design tokens.</summary>
public sealed record CrosshairTokens
{
    /// <summary>Gets the crosshair-line color.</summary>
    public ThemeColor? Line { get; init; }
    /// <summary>Gets the label background.</summary>
    public ThemeColor? LabelBackground { get; init; }
    /// <summary>Gets the label text color.</summary>
    public ThemeColor? LabelText { get; init; }
}

/// <summary>Glow design tokens.</summary>
public sealed record GlowTokens
{
    /// <summary>Gets whether glow is enabled.</summary>
    public bool? Enabled { get; init; }
    /// <summary>Gets the glow radius.</summary>
    public double? Radius { get; init; }
    /// <summary>Gets the glow intensity.</summary>
    public double? Intensity { get; init; }
}

/// <summary>Effect design tokens.</summary>
public sealed record EffectTokens
{
    /// <summary>Gets the glow settings.</summary>
    public GlowTokens? Glow { get; init; }
    /// <summary>Gets the opacity applied to series.</summary>
    public double? SeriesOpacity { get; init; }
}

/// <summary>Animation design tokens.</summary>
public sealed record AnimationTokens
{
    /// <summary>Gets the default duration in milliseconds.</summary>
    public int? DurationMs { get; init; }
    /// <summary>Gets the easing name.</summary>
    public string? Easing { get; init; }
}
