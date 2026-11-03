using System.Numerics;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Does on the CPU what raster.wgsl does on the GPU.
/// </summary>
internal static class RasterPainter
{
    // Fewer than the shader reads: this runs on the CPU for every pixel.
    private const int MaximumSamples = 2;

    private static float Reduce(ReadOnlySpan<float> values, PixelSize size, float x, float y, float footprintX, float footprintY, in RasterStyle style)
    {
        var samplesX = Math.Clamp((int)MathF.Ceiling(footprintX), 1, MaximumSamples);
        var samplesY = Math.Clamp((int)MathF.Ceiling(footprintY), 1, MaximumSamples);
        var sum = 0f;
        var highest = float.NegativeInfinity;
        var found = 0;
        for (var j = 0; j < samplesY; j++)
        {
            var row = (int)MathF.Floor(y - (footprintY / 2) + ((j + 0.5f) * footprintY / samplesY));
            for (var i = 0; i < samplesX; i++)
            {
                var value = Load(values, size, (int)MathF.Floor(x - (footprintX / 2) + ((i + 0.5f) * footprintX / samplesX)), row, style.RowShift);
                if (!float.IsNaN(value))
                {
                    sum += value;
                    highest = Math.Max(highest, value);
                    found++;
                }
            }
        }

        if (found == 0)
        {
            return float.NaN;
        }

        return style.Reduction == RasterReduction.Maximum ? highest : sum / found;
    }

    private static float Blend(ReadOnlySpan<float> values, PixelSize size, float x, float y, int rowShift)
    {
        var column = (int)MathF.Floor(x);
        var row = (int)MathF.Floor(y);
        var partX = x - MathF.Floor(x);
        var partY = y - MathF.Floor(y);
        var a = Load(values, size, column, row, rowShift);
        var b = Load(values, size, column + 1, row, rowShift);
        var c = Load(values, size, column, row + 1, rowShift);
        var d = Load(values, size, column + 1, row + 1, rowShift);
        var low = a + ((b - a) * partX);
        var high = c + ((d - c) * partX);

        // NaN carries through the sums, like a missing neighbor in the shader.
        return low + ((high - low) * partY);
    }

    private static float Load(ReadOnlySpan<float> values, PixelSize size, int column, int row, int rowShift)
    {
        var y = (int)(((uint)Math.Clamp(row, 0, size.Height - 1) + (uint)rowShift) % (uint)size.Height);
        return values[(y * size.Width) + Math.Clamp(column, 0, size.Width - 1)];
    }

    // Pointers, because rows run in parallel and a lambda cannot hold spans. All of them outlive the call.
    public static unsafe void Paint(
        ReadOnlySpan<float> values,
        PixelSize valueSize,
        ReadOnlySpan<Rgba8> table,
        Span<Rgba8> pixels,
        PixelSize size,
        int left,
        int top,
        PixelRect bounds,
        in RasterStyle style)
    {
        var scale = new Vector2(style.SectionSize.X / bounds.Width, style.SectionSize.Y / bounds.Height);
        var below = style.BelowColor ?? table[0];
        var above = style.AboveColor ?? table[^1];
        var last = table.Length - 1;
        var logMinimum = MathF.Log(style.Minimum);
        var logFactor = 1 / (MathF.Log(style.Maximum) - logMinimum);
        var factor = 1 / (style.Maximum - style.Minimum);

        var shared = new Shared(below, above, last, logMinimum, logFactor, factor, scale, left, top, bounds, valueSize, size, style);
        fixed (float* valuePointer = values)
        fixed (Rgba8* tablePointer = table)
        fixed (Rgba8* pixelPointer = pixels)
        {
            var (valueAddress, valueLength) = ((nint)valuePointer, values.Length);
            var (tableAddress, tableLength) = ((nint)tablePointer, table.Length);
            var (pixelAddress, pixelLength) = ((nint)pixelPointer, pixels.Length);
            Parallel.For(0, size.Height, y => PaintRow(
                new ReadOnlySpan<float>((void*)valueAddress, valueLength),
                new ReadOnlySpan<Rgba8>((void*)tableAddress, tableLength),
                new Span<Rgba8>((void*)pixelAddress, pixelLength),
                y,
                shared));
        }
    }

    private static void PaintRow(ReadOnlySpan<float> values, ReadOnlySpan<Rgba8> table, Span<Rgba8> pixels, int y, Shared shared)
    {
        var (below, above, last, logMinimum, logFactor, factor, scale, left, top, bounds, valueSize, size, style) = shared;
        var sectionY = style.SectionOrigin.Y + ((top + y + 0.5f - bounds.Y) * scale.Y);
        var row = Math.Clamp((int)MathF.Floor(sectionY * valueSize.Height), 0, valueSize.Height - 1);
        row = (int)(((uint)row + (uint)style.RowShift) % (uint)valueSize.Height);
        var line = values.Slice(row * valueSize.Width, valueSize.Width);
        var output = pixels.Slice(y * size.Width, size.Width);
        var blendY = (sectionY * valueSize.Height) - 0.5f;
        var footprintX = MathF.Abs(scale.X) * valueSize.Width;
        var footprintY = MathF.Abs(scale.Y) * valueSize.Height;
        for (var x = 0; x < output.Length; x++)
        {
            var sectionX = style.SectionOrigin.X + ((left + x + 0.5f - bounds.X) * scale.X);
            float value;
            if (style.IsInterpolated)
            {
                value = Blend(values, valueSize, (sectionX * valueSize.Width) - 0.5f, blendY, style.RowShift);
            }
            else if (style.Reduction != RasterReduction.Nearest)
            {
                value = Reduce(values, valueSize, sectionX * valueSize.Width, sectionY * valueSize.Height, footprintX, footprintY, style);
            }
            else
            {
                value = line[Math.Clamp((int)MathF.Floor(sectionX * valueSize.Width), 0, valueSize.Width - 1)];
            }

            if (float.IsNaN(value))
            {
                output[x] = style.MissingColor;
            }
            else if (value < style.Minimum || (style.IsLogarithmic && value <= 0))
            {
                output[x] = below;
            }
            else if (value > style.Maximum)
            {
                output[x] = above;
            }
            else
            {
                var position = style.IsLogarithmic ? (MathF.Log(value) - logMinimum) * logFactor : (value - style.Minimum) * factor;
                output[x] = table[Math.Clamp((int)MathF.Floor((position * last) + 0.5f), 0, last)];
            }
        }
    }

    private sealed record Shared(
        Rgba8 Below,
        Rgba8 Above,
        int Last,
        float LogMinimum,
        float LogFactor,
        float Factor,
        Vector2 Scale,
        int Left,
        int Top,
        PixelRect Bounds,
        PixelSize ValueSize,
        PixelSize Size,
        RasterStyle Style);
}
