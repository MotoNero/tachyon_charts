using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// What fills a shape of a series.
/// </summary>
public abstract record SeriesFill
{
    /// <summary>Finds the color of a point of a shape.</summary>
    /// <param name="position">The vertical position in the pane, from 0 at the top to 1 at the bottom.</param>
    /// <param name="value">The data value at the point.</param>
    /// <returns>The color.</returns>
    public abstract Rgba8 GetColor(double position, double value);
}

/// <summary>One color.</summary>
/// <param name="Color">The color.</param>
public sealed record SolidFill(Rgba8 Color) : SeriesFill
{
    /// <inheritdoc />
    public override Rgba8 GetColor(double position, double value) => Color;
}

/// <summary>A gradient over the height of the pane, for example fading out towards the baseline.</summary>
/// <param name="Top">The color at the top.</param>
/// <param name="Bottom">The color at the bottom.</param>
public sealed record PositionGradientFill(Rgba8 Top, Rgba8 Bottom) : SeriesFill
{
    /// <inheritdoc />
    public override Rgba8 GetColor(double position, double value) => RgbaMath.Mix(Top, Bottom, position);
}

/// <summary>A gradient by data value, for example a temperature.</summary>
/// <param name="Colormap">The colors.</param>
/// <param name="Range">The values of the first and the last color.</param>
public sealed record ValueGradientFill(Colormap Colormap, DoubleRange Range) : SeriesFill
{
    /// <inheritdoc />
    public override Rgba8 GetColor(double position, double value) =>
        Colormap.Sample((value - Range.Min) / (Range.Max - Range.Min));
}

/// <summary>Arithmetic on colors.</summary>
public static class RgbaMath
{
    /// <summary>Blends two colors.</summary>
    /// <param name="first">The color at 0.</param>
    /// <param name="second">The color at 1.</param>
    /// <param name="amount">The position, limited to 0 to 1.</param>
    /// <returns>The color.</returns>
    public static Rgba8 Mix(Rgba8 first, Rgba8 second, double amount)
    {
        var part = double.IsNaN(amount) ? 0 : Math.Clamp(amount, 0, 1);
        return new Rgba8(Channel(first.R, second.R), Channel(first.G, second.G), Channel(first.B, second.B), Channel(first.A, second.A));

        byte Channel(byte from, byte to) => (byte)Math.Round(from + ((to - from) * part));
    }
}
