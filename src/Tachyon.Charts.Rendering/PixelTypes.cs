using System.Runtime.InteropServices;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// A size in physical device pixels.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary>
    /// Gets the number of pixels covered by this size.
    /// </summary>
    public long PixelCount => (long)Width * Height;

    /// <summary>
    /// Converts this size to device-independent units.
    /// </summary>
    /// <param name="dpiScale">Physical pixels per device-independent unit.</param>
    /// <returns>The size in device-independent units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpiScale"/> is not positive and finite.</exception>
    public DipSize ToDips(double dpiScale)
    {
        RenderGuard.DpiScale(dpiScale);
        return new DipSize(Width / dpiScale, Height / dpiScale);
    }
}

/// <summary>
/// A size in device-independent units, the unit of all layout sizes.
/// </summary>
/// <param name="Width">The width in device-independent units.</param>
/// <param name="Height">The height in device-independent units.</param>
public readonly record struct DipSize(double Width, double Height)
{
    /// <summary>
    /// Converts this size to physical pixels, rounding to the nearest pixel.
    /// </summary>
    /// <param name="dpiScale">Physical pixels per device-independent unit.</param>
    /// <returns>The size in physical pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dpiScale"/> is not positive and finite, or a dimension is negative, not finite
    /// or too large for a <see cref="PixelSize"/>.
    /// </exception>
    public PixelSize ToPixels(double dpiScale)
    {
        RenderGuard.DpiScale(dpiScale);
        return new PixelSize(ToPixelCount(Width, dpiScale), ToPixelCount(Height, dpiScale));
    }

    private static int ToPixelCount(double dips, double dpiScale)
    {
        var pixels = Math.Round(dips * dpiScale, MidpointRounding.AwayFromZero);

        // The negated form also rejects NaN.
        if (!(dips >= 0 && pixels <= int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(dips), dips, "The size cannot be represented in pixels.");
        }

        return (int)pixels;
    }
}

/// <summary>
/// A rectangle in physical pixels with subpixel precision, origin at the top left of the render target.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct PixelRect(float X, float Y, float Width, float Height);

/// <summary>
/// An 8-bit-per-channel color, not premultiplied. The memory layout is R, G, B, A.
/// </summary>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
/// <param name="A">The alpha channel, 255 is opaque.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rgba8(byte R, byte G, byte B, byte A);
