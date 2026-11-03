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

    /// <summary>
    /// Brings the atlas up to date on a device. Call it after <see cref="Layout"/> and before drawing.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>The atlas, owned by the engine.</returns>
    IGpuTexture GetAtlas(IRenderDevice device);
}
