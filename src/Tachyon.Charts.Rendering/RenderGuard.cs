using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// Argument checks shared by the render API types.
/// </summary>
internal static class RenderGuard
{
    /// <summary>
    /// The alignment of buffer writes, set by the WebGPU copy rules.
    /// </summary>
    /// <summary>Checks one row of a texture write.</summary>
    /// <param name="row">The row.</param>
    /// <param name="length">The length of the data in bytes.</param>
    /// <param name="size">The texture size.</param>
    /// <param name="rowBytes">The length of a row in bytes.</param>
    public static void TextureRow(int row, int length, PixelSize size, long rowBytes)
    {
        if (row < 0 || row >= size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "The row does not exist.");
        }

        if (length != rowBytes)
        {
            throw new ArgumentException("The length does not match a row.", nameof(length));
        }
    }

    /// <summary>Checks the textures of a raster.</summary>
    /// <param name="values">The texture of values.</param>
    /// <param name="colormap">The table of colors.</param>
    public static void RasterFormats(IGpuTexture values, IGpuTexture colormap)
    {
        if (values.Format != TextureFormat.R32Float)
        {
            throw new ArgumentException("The values must be R32Float.", nameof(values));
        }

        if (colormap.Format != TextureFormat.Rgba8Unorm)
        {
            throw new ArgumentException("The colormap must be RGBA.", nameof(colormap));
        }
    }

    public const int BufferWriteAlignment = 4;

    public static long AlignUp(long value, long alignment) => (value + alignment - 1) / alignment * alignment;

    public static void DpiScale(double dpiScale)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), dpiScale, "The DPI scale must be positive and finite.");
        }
    }

    public static void PositiveSize(PixelSize size, int maxDimension)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > maxDimension || size.Height > maxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"Both dimensions must be in the range 1 to {maxDimension}.");
        }
    }

    public static bool IsDrawable(PixelRect bounds) =>
        bounds.Width > 0 && bounds.Height > 0
        && float.IsFinite(bounds.X) && float.IsFinite(bounds.Y)
        && float.IsFinite(bounds.X + bounds.Width) && float.IsFinite(bounds.Y + bounds.Height);

    public static bool IsDrawable(Vector2 a, Vector2 b, Vector2 c) => IsFinite(a) && IsFinite(b) && IsFinite(c);

    public static void BufferWrite(long offsetInBytes, long byteCount, long bufferSize)
    {
        if (offsetInBytes < 0 || offsetInBytes > bufferSize - byteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetInBytes), offsetInBytes, "The data does not fit into the buffer.");
        }

        if (offsetInBytes % BufferWriteAlignment != 0 || byteCount % BufferWriteAlignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetInBytes), offsetInBytes, $"Offset and length must be multiples of {BufferWriteAlignment}.");
        }
    }

    public static void Polyline(int firstPoint, int pointCount, float widthInPixels, long bufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstPoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(pointCount, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            ((long)firstPoint + pointCount) * Unsafe.SizeOf<Vector2>(),
            bufferSize,
            nameof(pointCount));
        if (!float.IsFinite(widthInPixels) || widthInPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthInPixels), widthInPixels, "The width must be positive and finite.");
        }
    }

    public static void Markers(int firstMarker, int markerCount, in MarkerStyle style, long bufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstMarker);
        ArgumentOutOfRangeException.ThrowIfNegative(markerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            ((long)firstMarker + markerCount) * Unsafe.SizeOf<MarkerInstance>(),
            bufferSize,
            nameof(markerCount));
        if (!Enum.IsDefined(style.Shape))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style.Shape, "The marker shape is unknown.");
        }

        if (!float.IsFinite(style.BorderWidthInPixels) || style.BorderWidthInPixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(style), style.BorderWidthInPixels, "The border width must be finite and non-negative.");
        }

        if (!float.IsFinite(style.SizeScale) || style.SizeScale <= 0 || !IsFinite(style.Scale) || !IsFinite(style.Offset))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "The marker transform must be finite and the size scale positive.");
        }
    }

    private static bool IsFinite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);
}
