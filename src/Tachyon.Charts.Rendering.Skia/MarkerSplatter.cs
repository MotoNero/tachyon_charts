using System.Numerics;
using System.Runtime.InteropServices;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Draws a very large number of round or square markers without Skia: one call per marker would take
/// seconds. Each thread owns a band of rows and goes through all markers, which keeps their order within
/// a pixel. There is no anti-aliasing, at this density it cannot be seen.
/// </summary>
internal static class MarkerSplatter
{
    /// <summary>The number of markers from which on this way is taken.</summary>
    public const int MinimumCount = 200_000;

    public static bool Supports(in MarkerStyle style) =>
        style.BorderWidthInPixels <= 0 && style.Shape is MarkerShape.Circle or MarkerShape.Square;

    /// <summary>Draws into premultiplied pixels.</summary>
    /// <param name="data">The buffer of the markers.</param>
    /// <param name="first">The first marker.</param>
    /// <param name="count">The number of markers.</param>
    /// <param name="style">The style.</param>
    /// <param name="pixels">The pixels, transparent at first.</param>
    /// <param name="size">The size of the picture.</param>
    public static void Splat(byte[] data, int first, int count, MarkerStyle style, Rgba8[] pixels, PixelSize size)
    {
        var clipLeft = 0;
        var clipRight = size.Width;
        var clipTop = 0;
        var clipBottom = size.Height;
        if (style.Clip is { } clip)
        {
            clipLeft = Math.Max((int)MathF.Ceiling(clip.X - 0.5f), 0);
            clipRight = Math.Min((int)MathF.Ceiling(clip.X + clip.Width - 0.5f), size.Width);
            clipTop = Math.Max((int)MathF.Ceiling(clip.Y - 0.5f), 0);
            clipBottom = Math.Min((int)MathF.Ceiling(clip.Y + clip.Height - 0.5f), size.Height);
        }

        var bands = Math.Max(Environment.ProcessorCount, 1);
        var bandHeight = Math.Max((clipBottom - clipTop + bands - 1) / bands, 1);
        Parallel.For(0, bands, band =>
        {
            var top = clipTop + (band * bandHeight);
            var bottom = Math.Min(top + bandHeight, clipBottom);
            if (top < bottom)
            {
                var markers = MemoryMarshal.Cast<byte, MarkerInstance>(data.AsSpan()).Slice(first, count);
                SplatBand(markers, style, pixels, size.Width, clipLeft, clipRight, top, bottom);
            }
        });
    }

    private static void SplatBand(ReadOnlySpan<MarkerInstance> markers, in MarkerStyle style, Rgba8[] pixels, int width, int left, int right, int top, int bottom)
    {
        var isCircle = style.Shape == MarkerShape.Circle;
        foreach (ref readonly var marker in markers)
        {
            var center = (marker.Position * style.Scale) + style.Offset;
            var half = marker.Size * style.SizeScale * 0.5f;
            if (!(center.Y + half >= top) || !(center.Y - half < bottom) || !(center.X + half >= left) || !(center.X - half < right) || !(half > 0))
            {
                continue;
            }

            var firstRow = Math.Max((int)MathF.Round(center.Y - half), top);
            var lastRow = Math.Min(Math.Max((int)MathF.Round(center.Y + half), firstRow + 1), bottom);
            var firstColumn = Math.Max((int)MathF.Round(center.X - half), left);
            var lastColumn = Math.Min(Math.Max((int)MathF.Round(center.X + half), firstColumn + 1), right);
            var color = Premultiply(marker.Color);
            for (var row = firstRow; row < lastRow; row++)
            {
                for (var column = firstColumn; column < lastColumn; column++)
                {
                    if (isCircle && half > 1.5f && Vector2.DistanceSquared(new Vector2(column + 0.5f, row + 0.5f), center) > half * half)
                    {
                        continue;
                    }

                    ref var pixel = ref pixels[(row * width) + column];
                    pixel = Over(color, pixel);
                }
            }
        }
    }

    private static Rgba8 Premultiply(Rgba8 color) =>
        new((byte)(color.R * color.A / 255), (byte)(color.G * color.A / 255), (byte)(color.B * color.A / 255), color.A);

    private static Rgba8 Over(Rgba8 source, Rgba8 target)
    {
        var rest = 255 - source.A;
        return new Rgba8(
            (byte)(source.R + (target.R * rest / 255)),
            (byte)(source.G + (target.G * rest / 255)),
            (byte)(source.B + (target.B * rest / 255)),
            (byte)(source.A + (target.A * rest / 255)));
    }
}
