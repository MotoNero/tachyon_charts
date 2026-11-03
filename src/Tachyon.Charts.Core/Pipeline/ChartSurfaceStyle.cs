using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// Colors used by the background and grid passes. Theme objects map their surface and grid tokens to
/// this backend-neutral style.
/// </summary>
public sealed record ChartSurfaceStyle
{
    /// <summary>Blends two styles, for a change of the theme.</summary>
    /// <param name="first">The style at 0.</param>
    /// <param name="second">The style at 1, which also gives all colors that do not blend.</param>
    /// <param name="amount">The position from 0 to 1.</param>
    /// <returns>The style.</returns>
    public static ChartSurfaceStyle Blend(ChartSurfaceStyle first, ChartSurfaceStyle second, double amount)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return second with
        {
            BackgroundColor = Series.RgbaMath.Mix(first.BackgroundColor, second.BackgroundColor, amount),
            PlotGradientStart = Series.RgbaMath.Mix(first.PlotGradientStart, second.PlotGradientStart, amount),
            PlotGradientEnd = Series.RgbaMath.Mix(first.PlotGradientEnd, second.PlotGradientEnd, amount),
            BorderColor = Series.RgbaMath.Mix(first.BorderColor, second.BorderColor, amount),
            MajorGridColor = Series.RgbaMath.Mix(first.MajorGridColor, second.MajorGridColor, amount),
            MinorGridColor = Series.RgbaMath.Mix(first.MinorGridColor, second.MinorGridColor, amount),
        };
    }

    /// <summary>Gets the default dark style.</summary>
    public static ChartSurfaceStyle Default { get; } = new();

    /// <summary>Gets the color outside plot panes.</summary>
    public Rgba8 BackgroundColor { get; init; } = new(24, 28, 36, 255);

    /// <summary>Gets the plot color at its top edge.</summary>
    public Rgba8 PlotGradientStart { get; init; } = new(32, 38, 50, 255);

    /// <summary>Gets the plot color at its bottom edge.</summary>
    public Rgba8 PlotGradientEnd { get; init; } = new(32, 38, 50, 255);

    /// <summary>Gets the plot border color. A transparent color disables the border.</summary>
    public Rgba8 BorderColor { get; init; } = new(255, 255, 255, 0);

    /// <summary>Gets the major grid-line color.</summary>
    public Rgba8 MajorGridColor { get; init; } = new(58, 66, 82, 255);

    /// <summary>Gets the minor grid-line color. A transparent color disables minor lines.</summary>
    public Rgba8 MinorGridColor { get; init; } = new(255, 255, 255, 0);

    /// <summary>Gets the alternating band color. A transparent color disables bands.</summary>
    public Rgba8 BandColor { get; init; } = new(255, 255, 255, 0);

    /// <summary>Gets the fill of the zoom preview.</summary>
    public Rgba8 ZoomRectangleColor { get; init; } = new(120, 170, 255, 48);

    /// <summary>Gets the border of the zoom preview.</summary>
    public Rgba8 ZoomRectangleBorderColor { get; init; } = new(120, 170, 255, 220);
}
