using System.Buffers.Binary;
using System.IO.Compression;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>Reads integer label maps and patient-space geometry from NIfTI-1 streams.</summary>
public static class NiftiSegmentationReader
{
    private const int HeaderSize = 348;

    /// <summary>Reads an uncompressed or gzip-compressed single-file NIfTI-1 label map.</summary>
    /// <param name="stream">The readable stream, which remains owned by the caller.</param>
    /// <returns>The segmentation.</returns>
    /// <exception cref="InvalidDataException">The stream is malformed, unsupported or does not contain integer labels.</exception>
    public static MedicalSegmentation Read(Stream stream) => Guarded(stream, ReadCore);

    /// <summary>Reads the image values of a NIfTI-1 file, for <see cref="NiftiImageReader.Read"/>.</summary>
    /// <param name="stream">The readable stream, which remains owned by the caller.</param>
    /// <returns>The volume.</returns>
    internal static MedicalVolume ReadVolume(Stream stream) => Guarded(stream, ReadVolumeCore);

    private static T Guarded<T>(Stream stream, Func<Stream, T> read)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("The NIfTI stream must be readable.", nameof(stream));

        try
        {
            using var source = OpenSource(stream);
            return read(source);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException or OverflowException)
        {
            throw new InvalidDataException("The NIfTI file is truncated or malformed.", exception);
        }
    }

    private static Stream OpenSource(Stream input)
    {
        Span<byte> prefix = stackalloc byte[2];
        var read = input.Read(prefix);
        Stream body;
        if (input.CanSeek)
        {
            input.Position -= read;
            body = input;
        }
        else
        {
            var copy = new MemoryStream();
            copy.Write(prefix[..read]);
            input.CopyTo(copy);
            copy.Position = 0;
            body = copy;
        }

        if (read == 2 && prefix[0] == 0x1f && prefix[1] == 0x8b)
        {
            var expanded = new MemoryStream();
            using (body == input ? null : body)
            using (var gzip = new GZipStream(body, CompressionMode.Decompress, leaveOpen: body == input))
            {
                gzip.CopyTo(expanded);
            }

            expanded.Position = 0;
            return expanded;
        }

        return body == input ? new NonOwningStream(body) : body;
    }

    private static MedicalSegmentation ReadCore(Stream stream)
    {
        var header = ReadHeader(stream);
        var labels = ReadLabels(stream, checked(header.Width * header.Height * header.Depth), header);
        var geometry = GeometryFromAffine(header.Affine, labels, header.Width, header.Height, header.Depth);
        return new MedicalSegmentation(
            new VolumeData(labels, header.Width, header.Height, header.Depth),
            new ImageSpacing(geometry.RowSpacing, geometry.ColumnSpacing),
            geometry.SliceSpacing,
            geometry.Origin,
            geometry.Orientation);
    }

    // Integers of up to 16 bits stay two bytes per value with the scale beside them; everything else becomes floats.
    private static MedicalVolume ReadVolumeCore(Stream stream)
    {
        var header = ReadHeader(stream);
        var (width, height, depth) = (header.Width, header.Height, header.Depth);
        var count = checked(width * height * depth);
        var raw = new byte[checked(count * header.Bytes)];
        stream.ReadExactly(raw);
        VolumeData data;
        GeometryResult geometry;
        if (header.Type is 2 or 256 or 4 or 512)
        {
            var words = new ushort[count];
            var lift = header.Type is 256 or 4 ? 32768 : 0;
            for (var i = 0; i < count; i++)
            {
                words[i] = (ushort)(Stored(raw.AsSpan(i * header.Bytes, header.Bytes), header.Type, header.Little) + lift);
            }

            geometry = GeometryFromAffine(header.Affine, words, width, height, depth);
            data = new VolumeData(words, width, height, depth, header.Slope, header.Intercept - (lift * header.Slope));
        }
        else
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = (float)((Stored(raw.AsSpan(i * header.Bytes, header.Bytes), header.Type, header.Little) * header.Slope) + header.Intercept);
            }

            geometry = GeometryFromAffine(header.Affine, values, width, height, depth);
            data = new VolumeData(values, width, height, depth);
        }

        return new MedicalVolume(data, new ImageSpacing(geometry.RowSpacing, geometry.ColumnSpacing), geometry.SliceSpacing, geometry.Origin, geometry.Orientation, null, null, null);
    }

    private static Header ReadHeader(Stream stream)
    {
        var header = new byte[HeaderSize];
        stream.ReadExactly(header);
        var little = Int32(header, 0, true) == HeaderSize;
        if (!little && Int32(header, 0, false) != HeaderSize)
            throw new InvalidDataException("The stream is not a NIfTI-1 file.");
        if (!header.AsSpan(344, 3).SequenceEqual("n+1"u8))
            throw new InvalidDataException("Only single-file NIfTI-1 files are supported.");

        var dimensions = Int16(header, 40, little);
        if (dimensions < 3 || dimensions > 7)
            throw new InvalidDataException("A NIfTI volume needs three dimensions.");
        var width = Positive(Int16(header, 42, little), "width");
        var height = Positive(Int16(header, 44, little), "height");
        var depth = Positive(Int16(header, 46, little), "depth");
        for (var axis = 4; axis <= dimensions; axis++)
        {
            if (Int16(header, 40 + (axis * 2), little) != 1)
                throw new InvalidDataException("A NIfTI file must contain one three-dimensional volume.");
        }

        var datatype = Int16(header, 70, little);
        var bytes = BytesPerValue(datatype);
        var offset = (long)Math.Ceiling(Single(header, 108, little));
        if (offset < HeaderSize) throw new InvalidDataException("The NIfTI voxel offset is invalid.");
        var slope = Single(header, 112, little);
        var intercept = Single(header, 116, little);
        if (slope == 0) slope = 1;
        if (!float.IsFinite(slope) || !float.IsFinite(intercept))
            throw new InvalidDataException("The NIfTI scaling is not finite.");

        var affine = ReadAffine(header, little);
        Skip(stream, offset - HeaderSize);
        return new Header(little, width, height, depth, datatype, bytes, slope, intercept, affine);
    }

    private static Affine ReadAffine(byte[] header, bool little)
    {
        if (Int16(header, 254, little) > 0)
        {
            return new Affine(
                Vector(header, 280, little),
                Vector(header, 296, little),
                Vector(header, 312, little));
        }

        if (Int16(header, 252, little) <= 0)
            throw new InvalidDataException("The NIfTI file has neither an sform nor a qform transform.");

        var b = (double)Single(header, 256, little);
        var c = (double)Single(header, 260, little);
        var d = (double)Single(header, 264, little);
        var square = 1 - (b * b) - (c * c) - (d * d);
        if (square < -1e-6) throw new InvalidDataException("The NIfTI qform quaternion is invalid.");
        var a = Math.Sqrt(Math.Max(0, square));
        var dx = Positive(Math.Abs(Single(header, 80, little)), "column spacing");
        var dy = Positive(Math.Abs(Single(header, 84, little)), "row spacing");
        var dz = Positive(Math.Abs(Single(header, 88, little)), "slice spacing");
        var qfac = Single(header, 76, little) < 0 ? -1 : 1;
        return new Affine(
            new Row((a * a) + (b * b) - (c * c) - (d * d), 2 * ((b * c) + (a * d)), 2 * ((b * d) - (a * c)), Single(header, 268, little)),
            new Row(2 * ((b * c) - (a * d)), (a * a) + (c * c) - (b * b) - (d * d), 2 * ((c * d) + (a * b)), Single(header, 272, little)),
            new Row(2 * ((b * d) + (a * c)), 2 * ((c * d) - (a * b)), (a * a) + (d * d) - (c * c) - (b * b), Single(header, 276, little)),
            dx, dy, dz * qfac);
    }

    private static GeometryResult GeometryFromAffine<T>(Affine affine, T[] values, int width, int height, int depth)
    {
        var across = ToLps(new Point3D(affine.X.X, affine.Y.X, affine.Z.X));
        var down = ToLps(new Point3D(affine.X.Y, affine.Y.Y, affine.Z.Y));
        var slices = ToLps(new Point3D(affine.X.Z, affine.Y.Z, affine.Z.Z));
        var origin = ToLps(new Point3D(affine.X.W, affine.Y.W, affine.Z.W));
        var columnSpacing = across.Length;
        var rowSpacing = down.Length;
        var sliceSpacing = slices.Length;
        if (columnSpacing <= 0 || rowSpacing <= 0 || sliceSpacing <= 0)
            throw new InvalidDataException("The NIfTI affine has a zero-length voxel axis.");
        across = across.Normalized();
        down = down.Normalized();
        slices = slices.Normalized();
        if (Math.Abs(Point3D.Dot(across, down)) > 1e-5
            || Math.Abs(Point3D.Dot(across, slices)) > 1e-5
            || Math.Abs(Point3D.Dot(down, slices)) > 1e-5)
            throw new InvalidDataException("Sheared NIfTI volumes are not supported.");

        var normal = Point3D.Cross(across, down).Normalized();
        if (Point3D.Dot(normal, slices) < 0)
        {
            origin += slices * ((depth - 1) * sliceSpacing);
            ReverseSlices(values, width, height, depth);
        }

        return new GeometryResult(columnSpacing, rowSpacing, sliceSpacing, origin, new PatientOrientation(across, down));
    }

    private static ushort[] ReadLabels(Stream stream, int count, Header header)
    {
        if (header.Type is 16 or 64)
            throw new InvalidDataException("The NIfTI datatype is not an integer label type.");

        var raw = new byte[checked(count * header.Bytes)];
        stream.ReadExactly(raw);
        var labels = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            var value = (Stored(raw.AsSpan(i * header.Bytes, header.Bytes), header.Type, header.Little) * header.Slope) + header.Intercept;
            if (!double.IsFinite(value) || value < 0 || value > ushort.MaxValue || value != Math.Truncate(value))
                throw new InvalidDataException("The scaled NIfTI values are not integer labels from zero through 65,535.");
            labels[i] = (ushort)value;
        }

        return labels;
    }

    private static double Stored(ReadOnlySpan<byte> at, short type, bool little) => type switch
    {
        2 => at[0],
        256 => (sbyte)at[0],
        4 => little ? BinaryPrimitives.ReadInt16LittleEndian(at) : BinaryPrimitives.ReadInt16BigEndian(at),
        512 => little ? BinaryPrimitives.ReadUInt16LittleEndian(at) : BinaryPrimitives.ReadUInt16BigEndian(at),
        8 => little ? BinaryPrimitives.ReadInt32LittleEndian(at) : BinaryPrimitives.ReadInt32BigEndian(at),
        768 => little ? BinaryPrimitives.ReadUInt32LittleEndian(at) : BinaryPrimitives.ReadUInt32BigEndian(at),
        16 => little ? BinaryPrimitives.ReadSingleLittleEndian(at) : BinaryPrimitives.ReadSingleBigEndian(at),
        64 => little ? BinaryPrimitives.ReadDoubleLittleEndian(at) : BinaryPrimitives.ReadDoubleBigEndian(at),
        _ => throw new InvalidDataException("The NIfTI datatype is not supported."),
    };

    private static void ReverseSlices<T>(T[] values, int width, int height, int depth)
    {
        var slice = width * height;
        for (var first = 0; first < depth / 2; first++)
        {
            var last = depth - 1 - first;
            for (var i = 0; i < slice; i++)
                (values[(first * slice) + i], values[(last * slice) + i]) = (values[(last * slice) + i], values[(first * slice) + i]);
        }
    }

    private static int BytesPerValue(short type) => type switch
    {
        2 or 256 => 1,
        4 or 512 => 2,
        8 or 768 or 16 => 4,
        64 => 8,
        _ => throw new InvalidDataException("Only 8-, 16- and 32-bit integers and 32- and 64-bit floats are supported in NIfTI files."),
    };

    private static Row Vector(byte[] source, int offset, bool little) =>
        new(Single(source, offset, little), Single(source, offset + 4, little), Single(source, offset + 8, little), Single(source, offset + 12, little));

    private static Point3D ToLps(Point3D ras) => new(-ras.X, -ras.Y, ras.Z);
    private static short Int16(byte[] source, int offset, bool little) => little ? BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(offset)) : BinaryPrimitives.ReadInt16BigEndian(source.AsSpan(offset));
    private static int Int32(byte[] source, int offset, bool little) => little ? BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset)) : BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(offset));
    private static float Single(byte[] source, int offset, bool little) => BitConverter.Int32BitsToSingle(Int32(source, offset, little));
    private static int Positive(int value, string name) => value > 0 ? value : throw new InvalidDataException($"The NIfTI {name} must be positive.");
    private static double Positive(double value, string name) => double.IsFinite(value) && value > 0 ? value : throw new InvalidDataException($"The NIfTI {name} must be finite and positive.");

    private static void Skip(Stream stream, long count)
    {
        if (stream.CanSeek) stream.Seek(count, SeekOrigin.Current);
        else
        {
            Span<byte> buffer = stackalloc byte[4096];
            while (count > 0)
            {
                var part = (int)Math.Min(count, buffer.Length);
                var read = stream.ReadAtLeast(buffer[..part], part, false);
                if (read == 0) throw new EndOfStreamException();
                count -= read;
            }
        }
    }

    private readonly record struct Row(double X, double Y, double Z, double W);

    private readonly record struct Affine(Row X, Row Y, Row Z)
    {
        public Affine(Row x, Row y, Row z, double dx, double dy, double dz)
            : this(x with { X = x.X * dx, Y = x.Y * dy, Z = x.Z * dz },
                   y with { X = y.X * dx, Y = y.Y * dy, Z = y.Z * dz },
                   z with { X = z.X * dx, Y = z.Y * dy, Z = z.Z * dz })
        { }
    }

    private readonly record struct Header(bool Little, int Width, int Height, int Depth, short Type, int Bytes, double Slope, double Intercept, Affine Affine);

    private readonly record struct GeometryResult(double ColumnSpacing, double RowSpacing, double SliceSpacing, Point3D Origin, PatientOrientation Orientation);

    private sealed class NonOwningStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
