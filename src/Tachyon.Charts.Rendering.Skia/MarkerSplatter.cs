using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Draws a very large number of round or square markers without Skia: one call per marker would take
/// seconds. Each thread owns a band of rows and draws the markers that reach into it, in their order, which
/// keeps their order within a pixel. Which markers reach into which band is sorted out once beforehand, on all
/// cores, so that no thread goes through all markers. There is no anti-aliasing, at this density it cannot be seen.
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
        var area = new Area(clipLeft, clipRight, clipTop, clipBottom, bandHeight, bands);

        // Pieces of the markers in their order: each piece counts the markers per band, then writes their numbers
        // into the lists of the bands, piece after piece, so that each list keeps the order of the markers.
        var pieces = bands * 4;
        var counts = ArrayPool<int>.Shared.Rent(pieces * bands);
        Array.Clear(counts, 0, pieces * bands);
        Parallel.For(0, pieces, piece =>
        {
            var markers = MemoryMarshal.Cast<byte, MarkerInstance>(data.AsSpan()).Slice(first, count);
            var (from, to) = (count * (long)piece / pieces, count * (long)(piece + 1) / pieces);
            var row = counts.AsSpan(piece * bands, bands);
            for (var i = (int)from; i < to; i++)
            {
                if (area.TryBands(markers[i], style, out var firstBand, out var lastBand))
                {
                    for (var band = firstBand; band <= lastBand; band++)
                    {
                        row[band]++;
                    }
                }
            }
        });

        // Where each piece writes in each band.
        var starts = ArrayPool<int>.Shared.Rent((pieces * bands) + bands + 1);
        var bandStarts = starts.AsSpan(pieces * bands, bands + 1);
        var total = 0;
        for (var band = 0; band < bands; band++)
        {
            bandStarts[band] = total;
            for (var piece = 0; piece < pieces; piece++)
            {
                starts[(piece * bands) + band] = total;
                total += counts[(piece * bands) + band];
            }
        }

        bandStarts[bands] = total;
        var order = ArrayPool<int>.Shared.Rent(Math.Max(total, 1));
        Parallel.For(0, pieces, piece =>
        {
            var markers = MemoryMarshal.Cast<byte, MarkerInstance>(data.AsSpan()).Slice(first, count);
            var (from, to) = (count * (long)piece / pieces, count * (long)(piece + 1) / pieces);
            var next = starts.AsSpan(piece * bands, bands);
            for (var i = (int)from; i < to; i++)
            {
                if (area.TryBands(markers[i], style, out var firstBand, out var lastBand))
                {
                    for (var band = firstBand; band <= lastBand; band++)
                    {
                        order[next[band]++] = i;
                    }
                }
            }
        });

        Parallel.For(0, bands, band =>
        {
            var top = clipTop + (band * bandHeight);
            var bottom = Math.Min(top + bandHeight, clipBottom);
            if (top < bottom)
            {
                var markers = MemoryMarshal.Cast<byte, MarkerInstance>(data.AsSpan()).Slice(first, count);
                var (start, end) = (starts[(pieces * bands) + band], starts[(pieces * bands) + band + 1]);
                var indices = order.AsSpan(start, end - start);
                SplatBand(markers, indices, style, pixels, size.Width, clipLeft, clipRight, top, bottom);
            }
        });

        ArrayPool<int>.Shared.Return(order);
        ArrayPool<int>.Shared.Return(starts);
        ArrayPool<int>.Shared.Return(counts);
    }

    // The clip of the picture and its bands: which bands a marker reaches into, by the same rows as it is drawn with.
    private readonly record struct Area(int Left, int Right, int Top, int Bottom, int BandHeight, int Bands)
    {
        public bool TryBands(in MarkerInstance marker, in MarkerStyle style, out int firstBand, out int lastBand)
        {
            (firstBand, lastBand) = (0, -1);
            var center = (marker.Position * style.Scale) + style.Offset;
            var half = marker.Size * style.SizeScale * 0.5f;
            if (!(center.Y + half >= Top) || !(center.Y - half < Bottom) || !(center.X + half >= Left) || !(center.X - half < Right) || !(half > 0))
            {
                return false;
            }

            var firstRow = Math.Max((int)MathF.Round(center.Y - half), Top);
            var lastRow = Math.Min(Math.Max((int)MathF.Round(center.Y + half), firstRow + 1), Bottom);
            if (firstRow >= lastRow)
            {
                return false;
            }

            firstBand = Math.Min((firstRow - Top) / BandHeight, Bands - 1);
            lastBand = Math.Min((lastRow - 1 - Top) / BandHeight, Bands - 1);
            return true;
        }
    }

    private static void SplatBand(ReadOnlySpan<MarkerInstance> markers, ReadOnlySpan<int> indices, in MarkerStyle style, Rgba8[] pixels, int width, int left, int right, int top, int bottom)
    {
        var isCircle = style.Shape == MarkerShape.Circle;
        foreach (var index in indices)
        {
            ref readonly var marker = ref markers[index];
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
