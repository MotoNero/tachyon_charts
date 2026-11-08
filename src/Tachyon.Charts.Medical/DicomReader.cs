using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>Reads uncompressed, single-frame grayscale DICOM Part 10 data sets.</summary>
public static class DicomReader
{
    private const string ImplicitLittleUid = "1.2.840.10008.1.2";
    private const string ExplicitLittleUid = "1.2.840.10008.1.2.1";
    private const string ExplicitBigUid = "1.2.840.10008.1.2.2";
    private const uint UndefinedLength = uint.MaxValue;

    private const uint TransferSyntaxTag = 0x00020010;
    private const uint ModalityTag = 0x00080060;
    private const uint SliceThicknessTag = 0x00180050;
    private const uint SpacingBetweenSlicesTag = 0x00180088;
    private const uint SeriesInstanceUidTag = 0x0020000E;
    private const uint ImagePositionTag = 0x00200032;
    private const uint ImageOrientationTag = 0x00200037;
    private const uint SamplesPerPixelTag = 0x00280002;
    private const uint PhotometricTag = 0x00280004;
    private const uint NumberOfFramesTag = 0x00280008;
    private const uint RowsTag = 0x00280010;
    private const uint ColumnsTag = 0x00280011;
    private const uint PixelSpacingTag = 0x00280030;
    private const uint WindowCenterTag = 0x00281050;
    private const uint WindowWidthTag = 0x00281051;
    private const uint RescaleInterceptTag = 0x00281052;
    private const uint RescaleSlopeTag = 0x00281053;
    private const uint BitsAllocatedTag = 0x00280100;
    private const uint BitsStoredTag = 0x00280101;
    private const uint HighBitTag = 0x00280102;
    private const uint PixelRepresentationTag = 0x00280103;
    private const uint PixelDataTag = 0x7FE00010;

    /// <summary>
    /// Reads one image. The stream must be readable and seekable and remains owned by the caller.
    /// </summary>
    /// <param name="stream">A DICOM Part 10 stream positioned at its start.</param>
    /// <returns>The image and its non-identifying image metadata.</returns>
    /// <exception cref="ArgumentException">The stream cannot be read and sought.</exception>
    /// <exception cref="DicomReadException">The data set is malformed or uses an unsupported encoding.</exception>
    public static MedicalImage Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The DICOM stream must be readable and seekable.", nameof(stream));
        }

        try
        {
            return ReadCore(stream);
        }
        catch (DicomReadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException or OverflowException or FormatException)
        {
            throw new DicomReadException("The DICOM data set is truncated or malformed.");
        }
    }

    private static MedicalImage ReadCore(Stream stream)
    {
        stream.Position = 0;
        Span<byte> prefix = stackalloc byte[132];
        ReadExactly(stream, prefix);
        if (!prefix[128..].SequenceEqual("DICM"u8))
        {
            throw new DicomReadException("The DICOM Part 10 marker is missing.");
        }

        string? transferUid = null;
        while (true)
        {
            var at = stream.Position;
            var header = ReadHeader(stream, DicomTransferSyntax.ExplicitVrLittleEndian, allowEnd: false);
            if (header.Group != 0x0002)
            {
                stream.Position = at;
                break;
            }

            if (header.Tag == TransferSyntaxTag)
            {
                transferUid = ReadText(stream, header.Length, maximum: 128);
            }
            else
            {
                SkipValue(stream, header, DicomTransferSyntax.ExplicitVrLittleEndian);
            }
        }

        var syntax = ParseTransferSyntax(transferUid);
        var metadata = new DicomImageMetadata { TransferSyntax = syntax };
        PixelBuffer? pixels = null;

        while (stream.Position < stream.Length)
        {
            var header = ReadHeader(stream, syntax, allowEnd: true);
            if (header.IsEnd)
            {
                break;
            }

            switch (header.Tag)
            {
                case ModalityTag:
                    metadata.Modality = ReadText(stream, header.Length, 64);
                    break;
                case SliceThicknessTag:
                    metadata.SliceThickness = ReadPositiveDecimal(stream, header.Length, "slice thickness");
                    break;
                case SpacingBetweenSlicesTag:
                    metadata.SpacingBetweenSlices = Math.Abs(ReadDecimal(stream, header.Length, "slice spacing"));
                    break;
                case SeriesInstanceUidTag:
                    metadata.SeriesInstanceUid = ReadText(stream, header.Length, 128);
                    break;
                case ImagePositionTag:
                    metadata.Position = ReadPoint(stream, header.Length);
                    break;
                case ImageOrientationTag:
                    metadata.Orientation = ReadOrientation(stream, header.Length);
                    break;
                case SamplesPerPixelTag:
                    metadata.SamplesPerPixel = ReadUnsignedShort(stream, header, syntax);
                    break;
                case PhotometricTag:
                    metadata.PhotometricInterpretation = ReadPhotometric(stream, header.Length);
                    break;
                case NumberOfFramesTag:
                    metadata.NumberOfFrames = ReadPositiveIntegerText(stream, header.Length, "frame count");
                    break;
                case RowsTag:
                    metadata.Height = ReadUnsignedShort(stream, header, syntax);
                    break;
                case ColumnsTag:
                    metadata.Width = ReadUnsignedShort(stream, header, syntax);
                    break;
                case PixelSpacingTag:
                    metadata.Spacing = ReadSpacing(stream, header.Length);
                    break;
                case WindowCenterTag:
                    metadata.WindowCenter = ReadDecimal(stream, header.Length, "window centre");
                    break;
                case WindowWidthTag:
                    metadata.WindowWidth = ReadPositiveDecimal(stream, header.Length, "window width");
                    break;
                case RescaleInterceptTag:
                    metadata.RescaleIntercept = ReadDecimal(stream, header.Length, "rescale intercept");
                    break;
                case RescaleSlopeTag:
                    metadata.RescaleSlope = ReadNonZeroDecimal(stream, header.Length, "rescale slope");
                    break;
                case BitsAllocatedTag:
                    metadata.BitsAllocated = ReadUnsignedShort(stream, header, syntax);
                    break;
                case BitsStoredTag:
                    metadata.BitsStored = ReadUnsignedShort(stream, header, syntax);
                    break;
                case HighBitTag:
                    metadata.HighBit = ReadUnsignedShort(stream, header, syntax);
                    break;
                case PixelRepresentationTag:
                    metadata.IsSigned = ReadUnsignedShort(stream, header, syntax) switch
                    {
                        0 => false,
                        1 => true,
                        _ => throw new DicomReadException("The pixel representation must be unsigned or signed."),
                    };
                    break;
                case PixelDataTag:
                    pixels = ReadPixels(stream, header, syntax, metadata);
                    break;
                default:
                    SkipValue(stream, header, syntax);
                    break;
            }
        }

        ValidateMetadata(metadata, pixels);
        return new MedicalImage(metadata, pixels!);
    }

    private static DicomTransferSyntax ParseTransferSyntax(string? uid) => uid switch
    {
        ImplicitLittleUid => DicomTransferSyntax.ImplicitVrLittleEndian,
        ExplicitLittleUid => DicomTransferSyntax.ExplicitVrLittleEndian,
        ExplicitBigUid => DicomTransferSyntax.ExplicitVrBigEndian,
        null or "" => throw new DicomReadException("The transfer syntax is missing."),
        _ => throw new DicomReadException(
            "The transfer syntax is compressed or unsupported. Only uncompressed implicit or explicit VR data is supported."),
    };

    private static PixelBuffer ReadPixels(
        Stream stream,
        ElementHeader header,
        DicomTransferSyntax syntax,
        DicomImageMetadata metadata)
    {
        if (header.Length == UndefinedLength)
        {
            throw new DicomReadException("Encapsulated or compressed pixel data is not supported.");
        }

        ValidatePixelShape(metadata);
        var count = checked(metadata.Width * metadata.Height);
        var expected = checked(count * (metadata.BitsAllocated / 8));
        if (header.Length != expected && header.Length != expected + (expected & 1))
        {
            throw new DicomReadException("The pixel-data length does not match the image dimensions.");
        }

        var raw = new byte[expected];
        ReadExactly(stream, raw);
        if (header.Length > expected)
        {
            SkipExactly(stream, header.Length - (uint)expected);
        }

        var shift = metadata.HighBit - metadata.BitsStored + 1;
        var fullUnsigned = !metadata.IsSigned
            && metadata.RescaleSlope == 1
            && metadata.RescaleIntercept == 0
            && shift == 0
            && metadata.BitsStored == metadata.BitsAllocated;
        if (fullUnsigned && metadata.BitsAllocated == 8)
        {
            return new PixelBuffer(raw);
        }

        var little = syntax != DicomTransferSyntax.ExplicitVrBigEndian;
        if (fullUnsigned)
        {
            var words = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                words[i] = ReadWord(raw.AsSpan(i * 2, 2), little);
            }

            return new PixelBuffer(words);
        }

        var mask = metadata.BitsStored == 16 ? 0xFFFFu : (1u << metadata.BitsStored) - 1;
        var sign = 1u << (metadata.BitsStored - 1);

        // 16-bit images keep two bytes per value with the scale beside them; signed values move up by 32768.
        if (metadata.BitsAllocated == 16 && metadata.RescaleSlope != 0)
        {
            var words = new ushort[count];
            var lift = metadata.IsSigned ? 32768 : 0;
            for (var i = 0; i < count; i++)
            {
                var bits = ((uint)ReadWord(raw.AsSpan(i * 2, 2), little) >> shift) & mask;
                var decoded = metadata.IsSigned && (bits & sign) != 0 ? (int)bits - (1 << metadata.BitsStored) : (int)bits;
                words[i] = (ushort)(decoded + lift);
            }

            return new PixelBuffer(words, metadata.RescaleSlope, metadata.RescaleIntercept - (lift * metadata.RescaleSlope));
        }

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            var stored = metadata.BitsAllocated == 8 ? raw[i] : ReadWord(raw.AsSpan(i * 2, 2), little);
            var bits = ((uint)stored >> shift) & mask;
            var decoded = metadata.IsSigned && (bits & sign) != 0 ? (int)bits - (1 << metadata.BitsStored) : (int)bits;
            values[i] = (float)((decoded * metadata.RescaleSlope) + metadata.RescaleIntercept);
        }

        return new PixelBuffer(values);
    }

    private static void ValidatePixelShape(DicomImageMetadata metadata)
    {
        if (metadata.Width <= 0 || metadata.Height <= 0)
        {
            throw new DicomReadException("The image dimensions are missing or invalid.");
        }

        if (metadata.SamplesPerPixel != 1)
        {
            throw new DicomReadException("Only single-sample grayscale images are supported.");
        }

        if (metadata.NumberOfFrames != 1)
        {
            throw new DicomReadException("Only single-frame images are supported.");
        }

        if (metadata.BitsAllocated is not (8 or 16)
            || metadata.BitsStored <= 0
            || metadata.BitsStored > metadata.BitsAllocated
            || metadata.HighBit < metadata.BitsStored - 1
            || metadata.HighBit >= metadata.BitsAllocated)
        {
            throw new DicomReadException("The pixel bit layout is missing or invalid.");
        }
    }

    private static void ValidateMetadata(DicomImageMetadata metadata, PixelBuffer? pixels)
    {
        ValidatePixelShape(metadata);
        if (pixels is null)
        {
            throw new DicomReadException("The pixel data is missing.");
        }

        if (pixels.Length != checked(metadata.Width * metadata.Height))
        {
            throw new DicomReadException("The decoded pixel count is invalid.");
        }
    }

    private static int ReadUnsignedShort(Stream stream, ElementHeader header, DicomTransferSyntax syntax)
    {
        if (header.Length != 2)
        {
            throw new DicomReadException("A required numeric image tag has an invalid length.");
        }

        Span<byte> bytes = stackalloc byte[2];
        ReadExactly(stream, bytes);
        return ReadWord(bytes, syntax != DicomTransferSyntax.ExplicitVrBigEndian);
    }

    private static ushort ReadWord(ReadOnlySpan<byte> bytes, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static ImageSpacing ReadSpacing(Stream stream, uint length)
    {
        var values = ReadDecimals(stream, length, 2, "pixel spacing");
        if (values[0] <= 0 || values[1] <= 0)
        {
            throw new DicomReadException("The pixel spacing must be positive.");
        }

        return new ImageSpacing(values[0], values[1]);
    }

    private static Point3D ReadPoint(Stream stream, uint length)
    {
        var values = ReadDecimals(stream, length, 3, "image position");
        return new Point3D(values[0], values[1], values[2]);
    }

    private static PatientOrientation ReadOrientation(Stream stream, uint length)
    {
        var values = ReadDecimals(stream, length, 6, "image orientation");
        var orientation = new PatientOrientation(
            new Point3D(values[0], values[1], values[2]),
            new Point3D(values[3], values[4], values[5]));
        if (orientation.Across.Length < 0.5 || orientation.Down.Length < 0.5 || orientation.Normal.Length < 0.5)
        {
            throw new DicomReadException("The image orientation is invalid.");
        }

        return new PatientOrientation(orientation.Across.Normalized(), orientation.Down.Normalized());
    }

    private static DicomPhotometricInterpretation ReadPhotometric(Stream stream, uint length) => ReadText(stream, length, 32) switch
    {
        "MONOCHROME1" => DicomPhotometricInterpretation.Monochrome1,
        "MONOCHROME2" => DicomPhotometricInterpretation.Monochrome2,
        _ => throw new DicomReadException("Only monochrome images are supported."),
    };

    private static double ReadPositiveDecimal(Stream stream, uint length, string name)
    {
        var value = ReadDecimal(stream, length, name);
        return value > 0 ? value : throw new DicomReadException($"The {name} must be positive.");
    }

    private static double ReadNonZeroDecimal(Stream stream, uint length, string name)
    {
        var value = ReadDecimal(stream, length, name);
        return value != 0 ? value : throw new DicomReadException($"The {name} must not be zero.");
    }

    private static double ReadDecimal(Stream stream, uint length, string name) => ReadDecimals(stream, length, 1, name)[0];

    private static double[] ReadDecimals(Stream stream, uint length, int count, string name)
    {
        var text = ReadText(stream, length, 256);
        var parts = text.Split('\\');
        if (parts.Length < count)
        {
            throw new DicomReadException($"The {name} is incomplete.");
        }

        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                || !double.IsFinite(values[i]))
            {
                throw new DicomReadException($"The {name} is invalid.");
            }
        }

        return values;
    }

    private static int ReadPositiveIntegerText(Stream stream, uint length, string name)
    {
        var text = ReadText(stream, length, 32);
        return int.TryParse(text.Split('\\')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new DicomReadException($"The {name} is invalid.");
    }

    private static string ReadText(Stream stream, uint length, int maximum)
    {
        if (length == UndefinedLength || length > maximum)
        {
            throw new DicomReadException("A required image tag has an invalid length.");
        }

        var bytes = new byte[length];
        ReadExactly(stream, bytes);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }

    private static ElementHeader ReadHeader(Stream stream, DicomTransferSyntax syntax, bool allowEnd)
    {
        Span<byte> first = stackalloc byte[8];
        var read = stream.ReadAtLeast(first, 8, throwOnEndOfStream: false);
        if (read == 0 && allowEnd)
        {
            return ElementHeader.End;
        }

        if (read != 8)
        {
            throw new EndOfStreamException();
        }

        var little = syntax != DicomTransferSyntax.ExplicitVrBigEndian;
        var group = ReadWord(first, little);
        var element = ReadWord(first[2..], little);
        if (group == 0xFFFE || syntax == DicomTransferSyntax.ImplicitVrLittleEndian)
        {
            return new ElementHeader(group, element, 0, ReadUInt32(first[4..], little));
        }

        var vr = (ushort)((first[4] << 8) | first[5]);
        if (!UsesLongLength(vr))
        {
            return new ElementHeader(group, element, vr, ReadWord(first[6..], little));
        }

        Span<byte> length = stackalloc byte[4];
        ReadExactly(stream, length);
        return new ElementHeader(group, element, vr, ReadUInt32(length, little));
    }

    private static bool UsesLongLength(ushort vr) => vr is
        0x4F42 or 0x4F44 or 0x4F46 or 0x4F4C or 0x4F56 or 0x4F57
        or 0x5351 or 0x5543 or 0x554E or 0x5552 or 0x5554;

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static void SkipValue(Stream stream, ElementHeader header, DicomTransferSyntax syntax)
    {
        if (header.Length != UndefinedLength)
        {
            SkipExactly(stream, header.Length);
            return;
        }

        SkipUndefinedContainer(stream, syntax, 0xE0DD);
    }

    private static void SkipUndefinedContainer(Stream stream, DicomTransferSyntax syntax, ushort delimiter)
    {
        while (true)
        {
            var header = ReadHeader(stream, syntax, allowEnd: false);
            if (header.Group == 0xFFFE && header.Element == delimiter)
            {
                SkipExactly(stream, header.Length);
                return;
            }

            if (header.Group == 0xFFFE && header.Element == 0xE000 && header.Length == UndefinedLength)
            {
                SkipUndefinedContainer(stream, syntax, 0xE00D);
            }
            else if (header.Length == UndefinedLength)
            {
                SkipUndefinedContainer(stream, syntax, 0xE0DD);
            }
            else
            {
                SkipExactly(stream, header.Length);
            }
        }
    }

    private static void SkipExactly(Stream stream, uint count)
    {
        var target = checked(stream.Position + count);
        if (target > stream.Length)
        {
            throw new EndOfStreamException();
        }

        stream.Position = target;
    }

    private static void ReadExactly(Stream stream, Span<byte> target) => stream.ReadExactly(target);

    private readonly record struct ElementHeader(ushort Group, ushort Element, ushort Vr, uint Length)
    {
        public static ElementHeader End => new(0, 0, 0, 0);

        public uint Tag => ((uint)Group << 16) | Element;

        public bool IsEnd => Group == 0 && Element == 0 && Length == 0;
    }
}
