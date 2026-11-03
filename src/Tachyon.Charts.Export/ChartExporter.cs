using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Export;

/// <summary>
/// Turns a chart into a file, with everything it shows at the moment. The members use the chart on the
/// calling thread, which has to be the one that renders it. The display does not change.
/// </summary>
public static class ChartExporter
{
    private const double InchesPerMeter = 39.3700787;
    private const int SignatureLength = 8;
    private const int ImageHeaderChunkLength = 25;
    private const int ResolutionChunkLength = 21;
    private const double MinimumVectorDpi = 288;
    private const float PointsPerUnit = 72f / 96;

    /// <summary>Renders a PNG file with the device the chart uses.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="device">The device.</param>
    /// <param name="options">The size, the resolution and the theme.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive or more than the device can render.</exception>
    public static byte[] ToPng(ChartSurface chart, IRenderDevice device, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);
        var size = GetPixelSize(options, device.Capabilities.MaxTextureDimension);
        using var target = device.CreateOffscreenTarget(size, options.Dpi / 96);
        using (var scope = new ExportScope(chart, options))
        {
            scope.Render(device, target);
        }

        device.WaitForIdle();
        var pixels = new Rgba8[size.PixelCount];
        target.ReadPixels(pixels);
        return Encode(pixels, size, options.Dpi);
    }

    /// <summary>
    /// Writes an SVG file. Lines and shapes stay vectors, while text and rasters are embedded as pictures
    /// of at least 288 dpi. Long series are thinned out to the resolution, like on the screen.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="options">The size, the resolution of the embedded pictures and the theme.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive or too large.</exception>
    public static byte[] ToSvg(ChartSurface chart, ExportOptions options)
    {
        using var device = new SkiaRenderDevice();
        using var target = RenderVector(chart, device, options, out var scale);
        using var stream = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, (float)options.Width, (float)options.Height), stream))
        {
            canvas.Scale(1 / scale);
            device.DrawLastFrame(target, canvas);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Writes a PDF file with one page of the size of the chart. Lines and shapes stay vectors, while text
    /// and rasters are embedded as pictures of at least 288 dpi. Long series are thinned out to the
    /// resolution, like on the screen.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="options">The size, the resolution of the embedded pictures and the theme.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive or too large.</exception>
    public static byte[] ToPdf(ChartSurface chart, ExportOptions options)
    {
        using var device = new SkiaRenderDevice();
        using var target = RenderVector(chart, device, options, out var scale);
        using var stream = new MemoryStream();
        using (var document = SKDocument.CreatePdf(stream))
        {
            var canvas = document.BeginPage((float)options.Width * PointsPerUnit, (float)options.Height * PointsPerUnit);
            canvas.Scale(PointsPerUnit / scale);
            device.DrawLastFrame(target, canvas);
            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Writes the visible or the given points as CSV: a header, then the series, X and Y of each point.
    /// Numbers use a dot, so that every program reads them alike. Series without points of their own, such
    /// as bars and rasters, have no rows. Safe from any thread: it reads the state of the last frame.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="selection">The points, or <see langword="null"/> for all in the visible area.</param>
    /// <returns>The file as UTF-8.</returns>
    public static byte[] ToCsv(ChartSurface chart, IReadOnlyList<SeriesSelection>? selection = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        selection ??= SelectVisible(chart);
        var text = new StringBuilder("series,x,y\r\n");
        foreach (var (series, x, y) in chart.ReadPoints(selection))
        {
            text.Append(Quote(series.Name ?? string.Empty)).Append(',')
                .Append(Quote(series.XAxis is DateTimeAxis time ? time.FormatCursorValue(x) : Number(x))).Append(',')
                .Append(Number(y)).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static IReadOnlyList<SeriesSelection> SelectVisible(ChartSurface chart)
    {
        if (chart.CurrentLayout is not { } layout)
        {
            return [];
        }

        var area = layout.PlotArea;
        return chart.SelectPoints(
        [
            new Vector2((float)area.X, (float)area.Y),
            new Vector2((float)area.Right, (float)area.Y),
            new Vector2((float)area.Right, (float)area.Bottom),
            new Vector2((float)area.X, (float)area.Bottom),
        ]);
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") < 0 ? text : '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private static IOffscreenRenderTarget RenderVector(ChartSurface chart, SkiaRenderDevice device, ExportOptions options, out float scale)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(options);
        var dpi = Math.Max(options.Dpi, MinimumVectorDpi);
        scale = (float)(dpi / 96);
        var size = GetPixelSize(options with { Dpi = dpi }, device.Capabilities.MaxTextureDimension);
        var target = device.CreateVectorTarget(size, scale);
        using (var scope = new ExportScope(chart, options))
        {
            scope.Render(device, target);
        }

        SkiaTextEngine.Default.ReleaseAtlas(device);
        return target;
    }

    private static PixelSize GetPixelSize(ExportOptions options, int maximum)
    {
        if (!(options.Dpi > 0) || !double.IsFinite(options.Dpi))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Dpi, "The resolution must be positive.");
        }

        var width = Math.Round(options.Width * options.Dpi / 96);
        var height = Math.Round(options.Height * options.Dpi / 96);
        if (!(width >= 1) || !(height >= 1) || width > maximum || height > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"{width} x {height} pixels are outside of 1 to {maximum}.");
        }

        return new PixelSize((int)width, (int)height);
    }

    private static byte[] Encode(Rgba8[] pixels, PixelSize size, double dpi)
    {
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(bitmap.GetPixelSpan());
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return WithResolution(data.Span, dpi);
    }

    // Skia writes no resolution, and without one a 300 dpi picture comes out huge in print.
    private static byte[] WithResolution(ReadOnlySpan<byte> png, double dpi)
    {
        var pixelsPerMeter = (uint)Math.Round(dpi * InchesPerMeter);
        var header = SignatureLength + ImageHeaderChunkLength;
        var result = new byte[png.Length + ResolutionChunkLength];
        png[..header].CopyTo(result);
        var chunk = result.AsSpan(header, ResolutionChunkLength);
        BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
        "pHYs"u8.CopyTo(chunk[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[8..], pixelsPerMeter);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[12..], pixelsPerMeter);
        chunk[16] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(chunk[17..], Crc32(chunk[4..17]));
        png[header..].CopyTo(result.AsSpan(header + ResolutionChunkLength));
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
