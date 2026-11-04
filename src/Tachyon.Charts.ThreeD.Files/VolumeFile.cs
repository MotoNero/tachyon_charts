using System.Buffers.Binary;
using System.IO.Compression;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>The kind of values in a raw volume file.</summary>
public enum VolumeValueType
{
    /// <summary>Unsigned 8 bits.</summary>
    UInt8,

    /// <summary>Unsigned 16 bits.</summary>
    UInt16,

    /// <summary>Signed 16 bits, as many scanners store them.</summary>
    Int16,

    /// <summary>32-bit floating point.</summary>
    Float32,
}

/// <summary>How a raw volume file is laid out. Raw files carry no header, so the application says it.</summary>
/// <param name="Width">The number of values along the first axis.</param>
/// <param name="Height">The number along the second axis.</param>
/// <param name="Depth">The number along the third axis.</param>
/// <param name="Type">The kind of values.</param>
public readonly record struct RawVolumeLayout(int Width, int Height, int Depth, VolumeValueType Type)
{
    /// <summary>Gets a value indicating whether multi-byte values start with the high byte.</summary>
    public bool IsBigEndian { get; init; }

    /// <summary>Gets the bytes before the first value.</summary>
    public long Offset { get; init; }

    /// <summary>Gets the distance of two values along each axis, for example in millimetres.</summary>
    public (double X, double Y, double Z) Spacing { get; init; } = (1, 1, 1);
}

/// <summary>A volume from a file, with the distance of its values along each axis.</summary>
/// <param name="data">The values.</param>
/// <param name="spacing">The distance of two values along each axis.</param>
public sealed class VolumeFileData(VolumeData data, (double X, double Y, double Z) spacing)
{
    /// <summary>Gets the values.</summary>
    public VolumeData Data { get; } = data;

    /// <summary>Gets the distance of two values along each axis.</summary>
    public (double X, double Y, double Z) Spacing { get; } = spacing;

    /// <summary>Gets the size of the volume along each axis, in the unit of the spacing.</summary>
    public (double X, double Y, double Z) Extent => ((Data.Width - 1) * Spacing.X, (Data.Height - 1) * Spacing.Y, (Data.Depth - 1) * Spacing.Z);

    /// <summary>Creates a volume that spans its real size.</summary>
    /// <returns>The volume, starting at 0 on each axis.</returns>
    public Volume3D ToVolume() => new(Data, new DoubleRange(0, Extent.X), new DoubleRange(0, Extent.Y), new DoubleRange(0, Extent.Z));

    /// <summary>
    /// Adds the volume to a chart to scale: all three axes get the length of the longest side, so that uneven spacing
    /// keeps the true proportions in the cube of the chart.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="volume">The volume, or <see langword="null"/> for <see cref="ToVolume"/>.</param>
    /// <param name="unit">The unit of the spacing, for the titles of the axes.</param>
    /// <returns>The chart.</returns>
    public Chart3DBuilder AddToScale(Chart3DBuilder chart, Volume3D? volume = null, string unit = "mm")
    {
        ArgumentNullException.ThrowIfNull(chart);
        var side = Math.Max(Math.Max(Extent.X, Extent.Y), Extent.Z);
        return chart.XAxis(0, side, $"x in {unit}").YAxis(0, side, $"y in {unit}").ZAxis(0, side, $"z in {unit}").AddVolume(volume ?? ToVolume());
    }
}

/// <summary>Reads volumes from raw files and from NIfTI-1, also packed with gzip, with code of its own. DICOM is not supported.</summary>
public static class VolumeFile
{
    private const int NiftiHeader = 348;
    private const int NiftiTwoHeader = 540;
    private const int ChunkBytes = 1 << 20;

    /// <summary>Reads a NIfTI file by its path, `.nii` or `.nii.gz`.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The volume.</returns>
    /// <exception cref="PointFileException">The file is damaged or of an unknown kind.</exception>
    public static VolumeFileData Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = path.ToUpperInvariant();
        if (!name.EndsWith(".NII", StringComparison.Ordinal) && !name.EndsWith(".NII.GZ", StringComparison.Ordinal))
        {
            throw new PointFileException("Only NIfTI files (.nii, .nii.gz) are known by their name. Read raw files with ReadRaw and their layout.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return ReadNifti(stream);
    }

    /// <summary>Reads a NIfTI-1 file. Packed files are recognized by their content. Of several volumes in time, the first is read.</summary>
    /// <param name="stream">The file. It is read from front to back only.</param>
    /// <returns>The volume.</returns>
    /// <exception cref="PointFileException">The file is damaged or of an unsupported kind.</exception>
    public static VolumeFileData ReadNifti(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffered = new BufferedStream(stream, 1 << 16);
        Span<byte> start = stackalloc byte[2];
        var read = buffered.ReadAtLeast(start, 2, false);
        var isPacked = read == 2 && start[0] == 0x1F && start[1] == 0x8B;
        Stream source = new PrefixStream(start[..read].ToArray(), buffered);
        long? length = null;
        if (isPacked)
        {
            source = new GZipStream(source, CompressionMode.Decompress);
        }
        else if (stream.CanSeek)
        {
            length = stream.Length;
        }

        try
        {
            return ReadNiftiBody(source, length);
        }
        catch (InvalidDataException exception)
        {
            throw new PointFileException("The packed file is damaged.", exception);
        }
    }

    /// <summary>Reads a raw volume. The layout comes from the application, as the file has no header.</summary>
    /// <param name="stream">The file.</param>
    /// <param name="layout">The layout.</param>
    /// <returns>The volume.</returns>
    /// <exception cref="PointFileException">The file does not fit the layout.</exception>
    public static VolumeFileData ReadRaw(Stream stream, RawVolumeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = layout.Type switch
        {
            VolumeValueType.UInt8 => 1,
            VolumeValueType.UInt16 or VolumeValueType.Int16 => 2,
            _ => 4,
        };
        var count = CheckSize(layout.Width, layout.Height, layout.Depth);
        if (stream.CanSeek && layout.Offset + (count * bytes) > stream.Length)
        {
            throw new PointFileException($"The file holds {stream.Length} bytes, the layout needs {layout.Offset + (count * bytes)}.");
        }

        Skip(stream, layout.Offset);
        var kind = layout.Type switch
        {
            VolumeValueType.UInt8 => NiftiType.UInt8,
            VolumeValueType.UInt16 => NiftiType.UInt16,
            VolumeValueType.Int16 => NiftiType.Int16,
            _ => NiftiType.Float32,
        };
        var data = ReadValues(stream, (layout.Width, layout.Height, layout.Depth), kind, !layout.IsBigEndian, (0, 0));
        return new VolumeFileData(data, CheckSpacing(layout.Spacing));
    }

    private static VolumeFileData ReadNiftiBody(Stream source, long? length)
    {
        var header = new byte[NiftiHeader];
        if (source.ReadAtLeast(header, NiftiHeader, false) < NiftiHeader)
        {
            throw new PointFileException("The file is shorter than a NIfTI header.");
        }

        var isLittle = CheckNiftiStart(header);
        var dims = ReadDims(header, isLittle);
        var type = (NiftiType)Short(header, 70, isLittle);
        var bytes = BytesOf(type);
        var offset = (long)Single(header, 108, isLittle);
        var count = CheckSize(dims[1], dims[2], dims[3]);
        if (offset < NiftiHeader || (length is { } total && offset + (count * bytes) > total))
        {
            throw new PointFileException($"The file is cut off: the values start at byte {offset} and need {count * bytes} bytes.");
        }

        Skip(source, offset - NiftiHeader);
        var (slope, intercept) = (Single(header, 112, isLittle), Single(header, 116, isLittle));
        var scale = double.IsFinite(slope) && slope != 0 && (slope != 1 || intercept != 0) ? ((double)slope, (double)intercept) : (0, 0);
        var spacing = (Spacing(Single(header, 80, isLittle)), Spacing(Single(header, 84, isLittle)), Spacing(Single(header, 88, isLittle)));
        return new VolumeFileData(ReadValues(source, (dims[1], dims[2], dims[3]), type, isLittle, scale), spacing);
    }

    // Whether the header is little endian, after the checks that it is a single NIfTI-1 file.
    private static bool CheckNiftiStart(byte[] header)
    {
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        var isLittle = size == NiftiHeader;
        if (!isLittle && BinaryPrimitives.ReverseEndianness(size) != NiftiHeader)
        {
            throw size == NiftiTwoHeader || BinaryPrimitives.ReverseEndianness(size) == NiftiTwoHeader
                ? new PointFileException("NIfTI-2 files are not supported, only NIfTI-1.")
                : new PointFileException("This is no NIfTI file: the header does not start with 348.");
        }

        var magic = header.AsSpan(344, 3);
        if (magic.SequenceEqual("ni1"u8))
        {
            throw new PointFileException("A NIfTI pair of .hdr and .img is not supported. Join it into one .nii file.");
        }

        if (!magic.SequenceEqual("n+1"u8))
        {
            throw new PointFileException("This is no NIfTI-1 file: the mark 'n+1' is missing.");
        }

        return isLittle;
    }

    private static int[] ReadDims(byte[] header, bool isLittle)
    {
        var dims = new int[8];
        for (var i = 0; i < 8; i++)
        {
            dims[i] = Short(header, 40 + (2 * i), isLittle);
        }

        if (dims[0] is < 3 or > 7 || dims[1] < 2 || dims[2] < 2 || dims[3] < 2)
        {
            throw new PointFileException($"A volume needs three sizes of at least 2, the header says {dims[0]} sizes: {dims[1]} × {dims[2]} × {dims[3]}.");
        }

        return dims;
    }

    private static int BytesOf(NiftiType type) => type switch
    {
        NiftiType.UInt8 or NiftiType.Int8 => 1,
        NiftiType.Int16 or NiftiType.UInt16 => 2,
        NiftiType.Int32 or NiftiType.UInt32 or NiftiType.Float32 => 4,
        NiftiType.Float64 => 8,
        _ => throw new PointFileException($"The NIfTI type {(int)type} is not supported. Known are 8, 16 and 32 bits and floating point."),
    };

    private static double Spacing(float value) => double.IsFinite(value) && value != 0 ? Math.Abs(value) : 1;

    private static (double X, double Y, double Z) CheckSpacing((double X, double Y, double Z) spacing) =>
        double.IsFinite(spacing.X + spacing.Y + spacing.Z) && spacing.X > 0 && spacing.Y > 0 && spacing.Z > 0
            ? spacing
            : throw new PointFileException("The spacing must be positive.");

    private static long CheckSize(int width, int height, int depth)
    {
        if (width < 2 || height < 2 || depth < 2)
        {
            throw new PointFileException($"A volume needs at least 2 values along each axis, the layout says {width} × {height} × {depth}.");
        }

        var count = (long)width * height * depth;
        return count > Array.MaxLength ? throw new PointFileException($"{count} values are more than one list can take.") : count;
    }

    private static short Short(byte[] header, int at, bool isLittle) =>
        isLittle ? BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(at)) : BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(at));

    private static float Single(byte[] header, int at, bool isLittle) =>
        isLittle ? BinaryPrimitives.ReadSingleLittleEndian(header.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(header.AsSpan(at));

    private static void Skip(Stream stream, long bytes)
    {
        var buffer = new byte[Math.Min(bytes, ChunkBytes)];
        for (var left = bytes; left > 0;)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(left, buffer.Length));
            left -= read > 0 ? read : throw new PointFileException("The file ends before its values.");
        }
    }

    // Bytes and unsigned 16 bits stay as they are, everything else becomes floating point. A scale from the header turns
    // all of them into floating point, as the stored numbers are then no measured values yet.
    private static VolumeData ReadValues(Stream stream, (int X, int Y, int Z) size, NiftiType type, bool isLittle, (double Slope, double Intercept) scale)
    {
        var count = (int)((long)size.X * size.Y * size.Z);
        var isScaled = scale.Slope != 0;
        try
        {
            if (type == NiftiType.UInt8 && !isScaled)
            {
                var values = new byte[count];
                ReadExactly(stream, values);
                return new VolumeData(values, size.X, size.Y, size.Z);
            }

            if (type == NiftiType.UInt16 && !isScaled)
            {
                return new VolumeData(ReadWords(stream, count, isLittle), size.X, size.Y, size.Z);
            }

            return new VolumeData(ReadFloats(stream, count, type, isLittle, scale), size.X, size.Y, size.Z);
        }
        catch (OutOfMemoryException exception)
        {
            throw new PointFileException($"There is not enough memory for {count} values.", exception);
        }
    }

    private static ushort[] ReadWords(Stream stream, int count, bool isLittle)
    {
        var words = new ushort[count];
        ReadChunks(stream, count, 2, (chunk, first) =>
        {
            for (var i = 0; i < chunk.Length / 2; i++)
            {
                words[first + i] = isLittle ? BinaryPrimitives.ReadUInt16LittleEndian(chunk[(2 * i)..]) : BinaryPrimitives.ReadUInt16BigEndian(chunk[(2 * i)..]);
            }
        });
        return words;
    }

    private static float[] ReadFloats(Stream stream, int count, NiftiType type, bool isLittle, (double Slope, double Intercept) scale)
    {
        var bytes = BytesOf(type);
        var isScaled = scale.Slope != 0;
        var floats = new float[count];
        ReadChunks(stream, count, bytes, (chunk, first) =>
        {
            for (var i = 0; i < chunk.Length / bytes; i++)
            {
                var value = Decode(chunk[(i * bytes)..], type, isLittle);
                floats[first + i] = (float)(isScaled ? (value * scale.Slope) + scale.Intercept : value);
            }
        });
        return floats;
    }

    private static double Decode(ReadOnlySpan<byte> at, NiftiType type, bool isLittle) => (type, isLittle) switch
    {
        (NiftiType.UInt8, _) => at[0],
        (NiftiType.Int8, _) => (sbyte)at[0],
        (NiftiType.Int16, true) => BinaryPrimitives.ReadInt16LittleEndian(at),
        (NiftiType.Int16, false) => BinaryPrimitives.ReadInt16BigEndian(at),
        (NiftiType.UInt16, true) => BinaryPrimitives.ReadUInt16LittleEndian(at),
        (NiftiType.UInt16, false) => BinaryPrimitives.ReadUInt16BigEndian(at),
        (NiftiType.Int32, true) => BinaryPrimitives.ReadInt32LittleEndian(at),
        (NiftiType.Int32, false) => BinaryPrimitives.ReadInt32BigEndian(at),
        (NiftiType.UInt32, true) => BinaryPrimitives.ReadUInt32LittleEndian(at),
        (NiftiType.UInt32, false) => BinaryPrimitives.ReadUInt32BigEndian(at),
        (NiftiType.Float32, true) => BinaryPrimitives.ReadSingleLittleEndian(at),
        (NiftiType.Float32, false) => BinaryPrimitives.ReadSingleBigEndian(at),
        (_, true) => BinaryPrimitives.ReadDoubleLittleEndian(at),
        (_, false) => BinaryPrimitives.ReadDoubleBigEndian(at),
    };

    private static void ReadExactly(Stream stream, byte[] values)
    {
        if (stream.ReadAtLeast(values, values.Length, false) < values.Length)
        {
            throw new PointFileException($"The file is cut off: it holds fewer than {values.Length} values.");
        }
    }

    private static void ReadChunks(Stream stream, int count, int bytes, ChunkReader read)
    {
        var buffer = new byte[ChunkBytes - (ChunkBytes % bytes)];
        for (var done = 0; done < count;)
        {
            var values = Math.Min(count - done, buffer.Length / bytes);
            if (stream.ReadAtLeast(buffer.AsSpan(0, values * bytes), values * bytes, false) < values * bytes)
            {
                throw new PointFileException($"The file is cut off: it holds {done} of {count} values.");
            }

            read(buffer.AsSpan(0, values * bytes), done);
            done += values;
        }
    }

    private delegate void ChunkReader(ReadOnlySpan<byte> chunk, int first);

    private enum NiftiType : short
    {
        UInt8 = 2,
        Int16 = 4,
        Int32 = 8,
        Float32 = 16,
        Float64 = 64,
        Int8 = 256,
        UInt16 = 512,
        UInt32 = 768,
    }

    /// <summary>Puts the bytes that were read to look at the start back in front of the rest.</summary>
    private sealed class PrefixStream(byte[] prefix, Stream rest) : Stream
    {
        private int _taken;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count)); // NOSONAR: overrides Stream.Read, the name is given

        public override int Read(Span<byte> buffer) // NOSONAR: overrides Stream.Read, the name is given
        {
            if (_taken < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - _taken);
                prefix.AsSpan(_taken, count).CopyTo(buffer);
                _taken += count;
                return count;
            }

            return rest.Read(buffer);
        }

        public override void Flush()
        {
            // Reading only.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
