using System.Buffers.Binary;
using System.IO.Compression;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Geo;

/// <summary>A picture of a tile: its pixels row by row from the top.</summary>
/// <param name="Pixels">The pixels.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public sealed record TileImage(Rgba8[] Pixels, int Width, int Height);

/// <summary>
/// Reads PNG pictures as tile servers send them: 8 bits per channel, gray, gray with alpha, RGB, RGBA or a palette with
/// transparency, not interlaced. Other pictures, such as JPEG, need a decoder of the application.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] _signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>Reads a PNG picture.</summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The picture, or <see langword="null"/> when it is no PNG of a kind this reader knows.</returns>
    public static TileImage? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(_signature))
        {
            return null;
        }

        using var data = new MemoryStream();
        if (ReadChunks(bytes, data) is not { } header)
        {
            return null;
        }

        var channels = header.ColorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => 0,
        };
        if (channels == 0 || header.Width == 0)
        {
            return null;
        }

        var stride = header.Width * channels;
        var raw = new byte[(stride + 1) * header.Height];
        data.Position = 0;
        try
        {
            using var inflate = new ZLibStream(data, CompressionMode.Decompress);
            inflate.ReadExactly(raw);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return null;
        }

        return new TileImage(ToPixels(raw, header, channels), header.Width, header.Height);
    }

    // The header, the palette with its transparency, and the compressed data of all IDAT chunks; null for a file this
    // reader does not know.
    private static Header? ReadChunks(ReadOnlySpan<byte> bytes, MemoryStream data)
    {
        var header = new Header(0, 0, -1, []);
        for (var at = 8; at + 12 <= bytes.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes[at..]);
            if (length < 0 || at + 12 + length > bytes.Length)
            {
                return null;
            }

            var type = bytes.Slice(at + 4, 4);
            var chunk = bytes.Slice(at + 8, length);
            if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                data.Write(chunk);
            }
            else if (ReadChunk(type, chunk, header) is { } read)
            {
                header = read;
            }
            else
            {
                return null;
            }

            at += 12 + length;
        }

        return header;
    }

    // A chunk before the data: the header, the palette or its transparency; others pass. Null for a header this
    // reader cannot read.
    private static Header? ReadChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> chunk, Header header)
    {
        if (type.SequenceEqual("IHDR"u8))
        {
            var (width, height) = (BinaryPrimitives.ReadInt32BigEndian(chunk), BinaryPrimitives.ReadInt32BigEndian(chunk[4..]));
            var known = chunk[8] == 8 && chunk[12] == 0 && width > 0 && height > 0 && (long)width * height <= 1 << 26;
            return known ? header with { Width = width, Height = height, ColorType = chunk[9] } : null;
        }

        if (type.SequenceEqual("PLTE"u8))
        {
            var palette = new Rgba8[chunk.Length / 3];
            for (var i = 0; i < palette.Length; i++)
            {
                palette[i] = new Rgba8(chunk[i * 3], chunk[(i * 3) + 1], chunk[(i * 3) + 2], 255);
            }

            return header with { Palette = palette };
        }

        if (type.SequenceEqual("tRNS"u8) && header.ColorType == 3)
        {
            var palette = header.Palette;
            for (var i = 0; i < Math.Min(chunk.Length, palette.Length); i++)
            {
                palette[i] = palette[i] with { A = chunk[i] };
            }
        }

        return header;
    }

    private static Rgba8[] ToPixels(byte[] raw, Header header, int channels)
    {
        var (width, height) = (header.Width, header.Height);
        var stride = width * channels;
        var pixels = new Rgba8[width * height];
        var previous = new byte[stride];
        var current = new byte[stride];
        for (var row = 0; row < height; row++)
        {
            var line = raw.AsSpan((row * (stride + 1)) + 1, stride);
            Unfilter(raw[row * (stride + 1)], line, previous, current, channels);
            for (var x = 0; x < width; x++)
            {
                pixels[(row * width) + x] = PixelOf(current.AsSpan(x * channels, channels), header);
            }

            (previous, current) = (current, previous);
        }

        return pixels;
    }

    private static Rgba8 PixelOf(ReadOnlySpan<byte> p, Header header) => header.ColorType switch
    {
        0 => new Rgba8(p[0], p[0], p[0], 255),
        2 => new Rgba8(p[0], p[1], p[2], 255),
        3 => p[0] < header.Palette.Length ? header.Palette[p[0]] : default,
        4 => new Rgba8(p[0], p[0], p[0], p[1]),
        _ => new Rgba8(p[0], p[1], p[2], p[3]),
    };

    // The five filters of PNG, each against the byte to the left, the one above and the one above left.
    private static void Unfilter(byte filter, ReadOnlySpan<byte> line, byte[] above, byte[] target, int bytesPerPixel)
    {
        for (var i = 0; i < line.Length; i++)
        {
            int left = i >= bytesPerPixel ? target[i - bytesPerPixel] : 0;
            int up = above[i];
            int upLeft = i >= bytesPerPixel ? above[i - bytesPerPixel] : 0;
            target[i] = (byte)(line[i] + filter switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => 0,
            });
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private sealed record Header(int Width, int Height, int ColorType, Rgba8[] Palette);
}
