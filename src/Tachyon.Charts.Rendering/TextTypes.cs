using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// One glyph on screen, sampled from a multi-channel signed distance field in an atlas.
/// </summary>
/// <param name="Position">The top left corner in physical pixels.</param>
/// <param name="Size">The size in physical pixels.</param>
/// <param name="UvMin">The top left corner in the atlas, from 0 to 1.</param>
/// <param name="UvMax">The bottom right corner in the atlas, from 0 to 1.</param>
/// <param name="Color">The text color.</param>
/// <param name="DistanceScale">Physical pixels per unit of the stored distance, which sets the edge softness.</param>
/// <param name="Rotation">The rotation in radians, clockwise on the screen.</param>
/// <param name="Pivot">The point in physical pixels the glyph turns around.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct GlyphInstance(Vector2 Position, Vector2 Size, Vector2 UvMin, Vector2 UvMax, Rgba8 Color, float DistanceScale, float Rotation = 0, Vector2 Pivot = default);

/// <summary>
/// A font: a family and its cut. The default is the font of the engine, upright and regular.
/// </summary>
/// <param name="Family">The family, such as <c>Roboto Slab</c> or an installed one; <see langword="null"/> for the font of the engine.</param>
/// <param name="Bold">Whether the cut is bold.</param>
/// <param name="Italic">Whether the cut is italic.</param>
[TypeConverter(typeof(TextFontConverter))]
public readonly record struct TextFont(string? Family = null, bool Bold = false, bool Italic = false)
{
    /// <summary>Gets a value indicating whether this is the font of the engine, upright and regular.</summary>
    public bool IsDefault => Family is null && !Bold && !Italic;

    /// <summary>Reads a font from text: the family and the words <c>Bold</c> and <c>Italic</c>, separated by commas, such as <c>Roboto Slab, Bold</c>.</summary>
    /// <param name="text">The text; empty for the font of the engine.</param>
    /// <returns>The font.</returns>
    public static TextFont Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? family = null;
        var (bold, italic) = (false, false);
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Bold", StringComparison.OrdinalIgnoreCase))
            {
                bold = true;
            }
            else if (part.Equals("Italic", StringComparison.OrdinalIgnoreCase))
            {
                italic = true;
            }
            else
            {
                family = part;
            }
        }

        return new TextFont(family, bold, italic);
    }

    /// <summary>Writes the font as <see cref="Parse"/> reads it.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => string.Join(", ", new[] { Family, Bold ? "Bold" : null, Italic ? "Italic" : null }.Where(part => part is not null));
}

/// <summary>Converts text such as <c>Roboto Slab, Bold</c> into a <see cref="TextFont"/>, for XAML.</summary>
public sealed class TextFontConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? TextFont.Parse(text) : base.ConvertFrom(context, culture, value);
}

/// <summary>
/// Shapes text into glyphs of a font atlas.
/// </summary>
/// <remarks>Sizes are em sizes in the unit of the caller: device-independent units or pixels.</remarks>
public interface ITextEngine
{
    /// <summary>
    /// Gets the distance between two baselines.
    /// </summary>
    /// <param name="size">The em size.</param>
    float GetLineHeight(float size);

    /// <summary>
    /// Measures the advance of a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The em size.</param>
    float MeasureWidth(ReadOnlySpan<char> text, float size);

    /// <summary>
    /// Creates the glyphs of a text. Characters seen for the first time extend the atlas.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="baseline">The left end of the baseline in pixels.</param>
    /// <param name="size">The em size in pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="glyphs">Receives the glyphs, at least as long as <paramref name="text"/>.</param>
    /// <returns>The number of glyphs. Spaces produce none.</returns>
    /// <exception cref="ArgumentException">The destination is too short.</exception>
    int Layout(ReadOnlySpan<char> text, Vector2 baseline, float size, Rgba8 color, Span<GlyphInstance> glyphs);

    /// <summary>Gets the distance between two baselines in a font.</summary>
    /// <param name="size">The em size.</param>
    /// <param name="font">The font; an engine with one font ignores it.</param>
    /// <returns>The distance.</returns>
    float GetLineHeight(float size, TextFont font) => GetLineHeight(size);

    /// <summary>Measures the advance of a text in a font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The em size.</param>
    /// <param name="font">The font; an engine with one font ignores it.</param>
    /// <returns>The advance.</returns>
    float MeasureWidth(ReadOnlySpan<char> text, float size, TextFont font) => MeasureWidth(text, size);

    /// <summary>Creates the glyphs of a text in a font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="baseline">The left end of the baseline in pixels.</param>
    /// <param name="size">The em size in pixels.</param>
    /// <param name="color">The text color.</param>
    /// <param name="font">The font; an engine with one font ignores it.</param>
    /// <param name="glyphs">Receives the glyphs, at least as long as <paramref name="text"/>.</param>
    /// <returns>The number of glyphs.</returns>
    /// <exception cref="ArgumentException">The destination is too short.</exception>
    int Layout(ReadOnlySpan<char> text, Vector2 baseline, float size, Rgba8 color, TextFont font, Span<GlyphInstance> glyphs) => Layout(text, baseline, size, color, glyphs);

    /// <summary>
    /// Brings the atlas up to date on a device. Call it after a layout and before drawing.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>The atlas, owned by the engine.</returns>
    IGpuTexture GetAtlas(IRenderDevice device);
}
