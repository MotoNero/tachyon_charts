using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Evaluates distance field glyphs on the CPU with the same formula as the WebGPU text shader, so that
/// text looks the same on both backends.
/// </summary>
internal static class GlyphRasterizer
{
    /// <summary>
    /// Draws glyphs into a transparent image.
    /// </summary>
    /// <returns>The image with its position, or <see langword="null"/> if no glyph is visible.</returns>
    public static (SKImage Image, float Left, float Top)? Rasterize(SkiaTexture atlas, ReadOnlySpan<GlyphInstance> glyphs, PixelSize target)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        foreach (ref readonly var glyph in glyphs)
        {
            var (minimum, maximum) = Extent(glyph);
            left = Math.Min(left, Math.Max((int)MathF.Floor(minimum.X), 0));
            top = Math.Min(top, Math.Max((int)MathF.Floor(minimum.Y), 0));
            right = Math.Max(right, Math.Min((int)MathF.Ceiling(maximum.X), target.Width));
            bottom = Math.Max(bottom, Math.Min((int)MathF.Ceiling(maximum.Y), target.Height));
        }

        if (right <= left || bottom <= top)
        {
            return null;
        }

        var width = right - left;
        var height = bottom - top;
        var length = width * height;
        var rented = ArrayPool<Rgba8>.Shared.Rent(length);
        var pixels = rented.AsSpan(0, length);
        pixels.Clear();
        foreach (ref readonly var glyph in glyphs)
        {
            DrawGlyph(atlas, glyph, pixels, left, top, width, height);
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var data = SKData.CreateCopy(MemoryMarshal.AsBytes(pixels));
        ArrayPool<Rgba8>.Shared.Return(rented);
        return (SKImage.FromPixels(info, data, info.RowBytes), left, top);
    }

    private static void DrawGlyph(SkiaTexture atlas, in GlyphInstance glyph, Span<Rgba8> pixels, int left, int top, int width, int height)
    {
        var (minimum, maximum) = Extent(glyph);
        var firstX = Math.Max((int)MathF.Floor(minimum.X), left);
        var firstY = Math.Max((int)MathF.Floor(minimum.Y), top);
        var lastX = Math.Min((int)MathF.Ceiling(maximum.X), left + width);
        var lastY = Math.Min((int)MathF.Ceiling(maximum.Y), top + height);
        var cosine = MathF.Cos(glyph.Rotation);
        var sine = MathF.Sin(glyph.Rotation);

        for (var y = firstY; y < lastY; y++)
        {
            for (var x = firstX; x < lastX; x++)
            {
                // The pixel, turned back into the unrotated glyph.
                var pixelX = x + 0.5f - glyph.Pivot.X;
                var pixelY = y + 0.5f - glyph.Pivot.Y;
                var localX = glyph.Pivot.X + (pixelX * cosine) + (pixelY * sine);
                var localY = glyph.Pivot.Y - (pixelX * sine) + (pixelY * cosine);
                var u = (localX - glyph.Position.X) / glyph.Size.X;
                var v = (localY - glyph.Position.Y) / glyph.Size.Y;
                if (u is < 0 or > 1 || v is < 0 or > 1)
                {
                    continue;
                }

                var uv = Vector2.Lerp(glyph.UvMin, glyph.UvMax, new Vector2(u, v));
                var coverage = Math.Clamp(((Sample(atlas, uv) - 0.5f) * glyph.DistanceScale) + 0.5f, 0, 1);
                var alpha = (byte)((coverage * glyph.Color.A) + 0.5f);

                ref var pixel = ref pixels[((y - top) * width) + (x - left)];
                if (alpha > pixel.A)
                {
                    pixel = glyph.Color with { A = alpha };
                }
            }
        }
    }

    private static (Vector2 Minimum, Vector2 Maximum) Extent(in GlyphInstance glyph)
    {
        if (glyph.Rotation == 0)
        {
            return (glyph.Position, glyph.Position + glyph.Size);
        }

        var cosine = MathF.Cos(glyph.Rotation);
        var sine = MathF.Sin(glyph.Rotation);
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        for (var corner = 0; corner < 4; corner++)
        {
            var offset = glyph.Position + (glyph.Size * new Vector2(corner & 1, corner >> 1)) - glyph.Pivot;
            var turned = glyph.Pivot + new Vector2((offset.X * cosine) - (offset.Y * sine), (offset.X * sine) + (offset.Y * cosine));
            minimum = Vector2.Min(minimum, turned);
            maximum = Vector2.Max(maximum, turned);
        }

        return (minimum, maximum);
    }

    // Bilinear, like the sampler of the shader, before the median.
    private static float Sample(SkiaTexture atlas, Vector2 uv)
    {
        var size = atlas.Size;
        var data = atlas.Data;
        var x = (uv.X * size.Width) - 0.5f;
        var y = (uv.Y * size.Height) - 0.5f;
        var x0 = Math.Clamp((int)MathF.Floor(x), 0, size.Width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(y), 0, size.Height - 1);
        var x1 = Math.Min(x0 + 1, size.Width - 1);
        var y1 = Math.Min(y0 + 1, size.Height - 1);
        var fx = Math.Clamp(x - x0, 0, 1);
        var fy = Math.Clamp(y - y0, 0, 1);

        Span<float> channels = stackalloc float[3];
        for (var channel = 0; channel < 3; channel++)
        {
            float Texel(int column, int row) => data[(((row * size.Width) + column) * 4) + channel];

            var upper = float.Lerp(Texel(x0, y0), Texel(x1, y0), fx);
            var lower = float.Lerp(Texel(x0, y1), Texel(x1, y1), fx);
            channels[channel] = float.Lerp(upper, lower, fy) / 255f;
        }

        return MathF.Max(MathF.Min(channels[0], channels[1]), MathF.Min(MathF.Max(channels[0], channels[1]), channels[2]));
    }
}
