using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>Reads uncompressed binary DICOM Segmentation objects as integer label maps.</summary>
public static class DicomSegReader
{
    private const uint Undefined = uint.MaxValue;
    private const uint TransferSyntax = 0x00020010;
    private const uint Modality = 0x00080060;
    private const uint NumberOfFrames = 0x00280008;
    private const uint Rows = 0x00280010;
    private const uint Columns = 0x00280011;
    private const uint PixelSpacing = 0x00280030;
    private const uint BitsAllocated = 0x00280100;
    private const uint PixelData = 0x7FE00010;
    private const uint SegmentationType = 0x00620001;
    private const uint SegmentSequence = 0x00620002;
    private const uint SegmentNumber = 0x00620004;
    private const uint SegmentLabel = 0x00620005;
    private const uint SegmentIdentification = 0x0062000A;
    private const uint ReferencedSegmentNumber = 0x0062000B;
    private const uint RecommendedColor = 0x0062000D;
    private const uint SharedGroups = 0x52009229;
    private const uint PerFrameGroups = 0x52009230;
    private const uint PixelMeasures = 0x00289110;
    private const uint PlanePosition = 0x00209113;
    private const uint PlaneOrientation = 0x00209116;
    private const uint ImagePosition = 0x00200032;
    private const uint ImageOrientation = 0x00200037;
    private const uint SliceThickness = 0x00180050;
    private const uint SpacingBetweenSlices = 0x00180088;

    /// <summary>Reads an uncompressed binary DICOM-SEG Part 10 stream.</summary>
    /// <param name="stream">The readable, seekable stream, which remains owned by the caller.</param>
    /// <returns>The categorical segmentation.</returns>
    /// <exception cref="DicomReadException">The object is malformed, compressed, fractional or overlapping.</exception>
    public static MedicalSegmentation Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("The DICOM stream must be readable and seekable.", nameof(stream));
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
            throw new DicomReadException("The DICOM-SEG data set is truncated or malformed.");
        }
    }

    private static MedicalSegmentation ReadCore(Stream stream)
    {
        stream.Position = 0;
        Span<byte> prefix = stackalloc byte[132];
        stream.ReadExactly(prefix);
        if (!prefix[128..].SequenceEqual("DICM"u8)) throw new DicomReadException("The DICOM Part 10 marker is missing.");

        var syntax = ReadSyntax(stream);
        var root = Dataset.Parse(stream, syntax, stream.Length, delimiter: 0);
        ValidateRoot(root, syntax);

        var rows = root.Number(Rows, syntax);
        var columns = root.Number(Columns, syntax);
        var frameCount = Positive(root.Text(NumberOfFrames), "frame count");
        var frames = root.Items(PerFrameGroups);
        if (frames.Count != frameCount) throw new DicomReadException("The per-frame geometry does not match the frame count.");
        var shared = root.Items(SharedGroups).SingleOrDefault() ?? throw new DicomReadException("The shared functional groups are missing.");
        var measures = shared.Items(PixelMeasures).SingleOrDefault() ?? throw new DicomReadException("The pixel measures are missing.");
        var spacingValues = Decimals(measures.Text(PixelSpacing), 2, "pixel spacing");
        if (spacingValues[0] <= 0 || spacingValues[1] <= 0) throw new DicomReadException("The pixel spacing must be positive.");
        var orientation = Orientation(FirstNested(shared, frames[0], PlaneOrientation).Text(ImageOrientation));
        var normal = orientation.Normal;

        var definitions = ReadSegments(root, syntax);
        var planes = new List<(double Along, Point3D Position)>();
        var frameInfo = ReadFrames(shared, frames, definitions, normal, syntax, planes);

        planes.Sort((a, b) => a.Along.CompareTo(b.Along));
        var indexByAlong = planes.Select((plane, index) => (plane.Along, index)).ToArray();
        var sliceSpacing = SliceSpacing(measures, planes);
        var origin = planes[0].Position;
        var labels = new ushort[checked(rows * columns * planes.Count)];
        var pixels = root.Bytes(PixelData);
        var bytesPerFrame = checked(((rows * columns) + 7) / 8);
        if (pixels.Length < checked(bytesPerFrame * frameCount)) throw new DicomReadException("The binary pixel data is shorter than the declared frames.");
        for (var frame = 0; frame < frameCount; frame++)
        {
            var z = indexByAlong.Single(item => Near(item.Along, frameInfo[frame].Along)).index;
            Fill(labels, pixels.AsSpan(frame * bytesPerFrame, bytesPerFrame), z * rows * columns, rows * columns, frameInfo[frame].Segment);
        }

        return new MedicalSegmentation(
            new VolumeData(labels, columns, rows, planes.Count),
            new ImageSpacing(spacingValues[0], spacingValues[1]),
            sliceSpacing,
            origin,
            orientation,
            definitions.Values);
    }

    // The transfer syntax from the file meta information, the elements of group 2.
    private static DicomTransferSyntax ReadSyntax(Stream stream)
    {
        string? uid = null;
        while (true)
        {
            var position = stream.Position;
            var header = Header.Parse(stream, DicomTransferSyntax.ExplicitVrLittleEndian);
            if (header.Group != 2)
            {
                stream.Position = position;
                break;
            }

            if (header.Tag == TransferSyntax) uid = ReadText(stream, header.Length);
            else Skip(stream, header.Length);
        }

        return uid switch
        {
            "1.2.840.10008.1.2" => DicomTransferSyntax.ImplicitVrLittleEndian,
            "1.2.840.10008.1.2.1" => DicomTransferSyntax.ExplicitVrLittleEndian,
            "1.2.840.10008.1.2.2" => DicomTransferSyntax.ExplicitVrBigEndian,
            _ => throw new DicomReadException("The DICOM-SEG transfer syntax is compressed or unsupported."),
        };
    }

    private static FrameInfo[] ReadFrames(Dataset shared, IReadOnlyList<Dataset> frames, Dictionary<int, MedicalSegment> definitions, Point3D normal, DicomTransferSyntax syntax, List<(double Along, Point3D Position)> planes)
    {
        var frameInfo = new FrameInfo[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var position = Point(FirstNested(shared, frame, PlanePosition).Text(ImagePosition), "image position");
            var segment = FirstNested(shared, frame, SegmentIdentification).Number(ReferencedSegmentNumber, syntax);
            if (!definitions.ContainsKey(segment)) throw new DicomReadException("A frame references an undefined segment.");
            var along = Point3D.Dot(position, normal);
            FindPlane(planes, along, position);
            frameInfo[i] = new FrameInfo(segment, along);
        }

        return frameInfo;
    }

    // Sets the label of a segment where its frame has a bit, refusing a pixel that already belongs to another one.
    private static void Fill(ushort[] labels, ReadOnlySpan<byte> bits, int first, int count, int segment)
    {
        for (var pixel = 0; pixel < count; pixel++)
        {
            if ((bits[pixel >> 3] & (1 << (pixel & 7))) == 0) continue;
            var at = first + pixel;
            if (labels[at] != 0 && labels[at] != segment)
                throw new DicomReadException("Overlapping DICOM-SEG segments cannot be represented by one integer label map.");
            labels[at] = (ushort)segment;
        }
    }

    private static void ValidateRoot(Dataset root, DicomTransferSyntax syntax)
    {
        if (!string.Equals(root.Text(Modality), "SEG", StringComparison.Ordinal)) throw new DicomReadException("The DICOM object is not a segmentation.");
        if (!string.Equals(root.Text(SegmentationType), "BINARY", StringComparison.Ordinal)) throw new DicomReadException("Only binary DICOM-SEG objects are supported; fractional segmentation is not losslessly categorical.");
        if (root.Number(BitsAllocated, syntax) != 1) throw new DicomReadException("A binary DICOM-SEG object must allocate one bit per pixel.");
    }

    private static Dictionary<int, MedicalSegment> ReadSegments(Dataset root, DicomTransferSyntax syntax)
    {
        var result = new Dictionary<int, MedicalSegment>();
        foreach (var item in root.Items(SegmentSequence))
        {
            var number = item.Number(SegmentNumber, syntax);
            if (number == 0 || !result.TryAdd(number, new MedicalSegment(number, item.Text(SegmentLabel), ReadColor(item, syntax, number))))
                throw new DicomReadException("Segment numbers must be unique and positive.");
        }

        if (result.Count == 0) throw new DicomReadException("The segment sequence is empty.");
        return result;
    }

    private static Rgba8 ReadColor(Dataset item, DicomTransferSyntax syntax, int number)
    {
        var bytes = item.OptionalBytes(RecommendedColor);
        if (bytes is null || bytes.Length != 6)
        {
            var colors = new[] { new Rgba8(230, 75, 90, 255), new Rgba8(55, 180, 110, 255), new Rgba8(60, 130, 235, 255), new Rgba8(245, 175, 55, 255) };
            return colors[(number - 1) % colors.Length];
        }

        var little = syntax != DicomTransferSyntax.ExplicitVrBigEndian;
        var l = Word(bytes, 0, little) * 100.0 / 65535;
        var a = (Word(bytes, 2, little) - 32768) * 255.0 / 65535;
        var b = (Word(bytes, 4, little) - 32768) * 255.0 / 65535;
        return LabToRgb(l, a, b);
    }

    private static Rgba8 LabToRgb(double l, double a, double b)
    {
        var fy = (l + 16) / 116;
        var fx = fy + (a / 500);
        var fz = fy - (b / 200);
        static double Pivot(double value) => value * value * value > 0.008856 ? value * value * value : (value - (16.0 / 116)) / 7.787;
        var x = 0.95047 * Pivot(fx);
        var y = Pivot(fy);
        var z = 1.08883 * Pivot(fz);
        static double Gamma(double value) => value <= 0.0031308 ? 12.92 * value : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
        static byte Channel(double value) => (byte)Math.Round(Math.Clamp(Gamma(value), 0, 1) * 255);
        return new Rgba8(Channel((3.2406 * x) - (1.5372 * y) - (0.4986 * z)), Channel((-0.9689 * x) + (1.8758 * y) + (0.0415 * z)), Channel((0.0557 * x) - (0.2040 * y) + (1.0570 * z)), 255);
    }

    private static Dataset FirstNested(Dataset shared, Dataset frame, uint tag)
    {
        var items = frame.Items(tag);
        if (items.Count == 0) items = shared.Items(tag);
        return items.SingleOrDefault() ?? throw new DicomReadException("A required DICOM-SEG functional group is missing.");
    }

    private static void FindPlane(List<(double Along, Point3D Position)> planes, double along, Point3D position)
    {
        for (var i = 0; i < planes.Count; i++) if (Near(planes[i].Along, along)) return;
        planes.Add((along, position));
    }

    private static double SliceSpacing(Dataset measures, List<(double Along, Point3D Position)> planes)
    {
        if (planes.Count < 2)
        {
            var text = measures.OptionalText(SpacingBetweenSlices) ?? measures.OptionalText(SliceThickness);
            var value = text is null ? 1 : Decimals(text, 1, "slice spacing")[0];
            return value > 0 ? value : throw new DicomReadException("The slice spacing must be positive.");
        }

        var spacing = planes[1].Along - planes[0].Along;
        for (var i = 2; i < planes.Count; i++) if (!Near(planes[i].Along - planes[i - 1].Along, spacing)) throw new DicomReadException("The DICOM-SEG slices are not regularly spaced.");
        return spacing;
    }

    private static PatientOrientation Orientation(string text)
    {
        var values = Decimals(text, 6, "image orientation");
        var result = new PatientOrientation(new(values[0], values[1], values[2]), new(values[3], values[4], values[5]));
        if (result.Across.Length < 0.5 || result.Down.Length < 0.5 || result.Normal.Length < 0.5) throw new DicomReadException("The image orientation is invalid.");
        return new PatientOrientation(result.Across.Normalized(), result.Down.Normalized());
    }

    private static Point3D Point(string text, string name)
    {
        var values = Decimals(text, 3, name);
        return new(values[0], values[1], values[2]);
    }

    private static double[] Decimals(string text, int count, string name)
    {
        var parts = text.Split('\\');
        if (parts.Length < count) throw new DicomReadException($"The {name} is incomplete.");
        var values = new double[count];
        for (var i = 0; i < count; i++) if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i])) throw new DicomReadException($"The {name} is invalid.");
        return values;
    }

    private static int Positive(string text, string name) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : throw new DicomReadException($"The {name} is invalid.");
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-5;
    private static ushort Word(byte[] bytes, int offset, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
    private static string ReadText(Stream stream, uint length) { var bytes = new byte[length]; stream.ReadExactly(bytes); return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' '); }
    private static void Skip(Stream stream, uint length)
    {
        if (length == Undefined || stream.Position + length > stream.Length)
        {
            throw new EndOfStreamException();
        }

        stream.Position += length;
    }

    private readonly record struct FrameInfo(int Segment, double Along);

    private sealed class Element(byte[]? value, List<Dataset>? items)
    {
        public byte[]? Value { get; } = value;
        public List<Dataset>? Items { get; } = items;
    }

    private sealed class Dataset
    {
        private static readonly HashSet<uint> _sequences = [SegmentSequence, SharedGroups, PerFrameGroups, PixelMeasures, PlanePosition, PlaneOrientation, SegmentIdentification];
        private readonly Dictionary<uint, Element> _elements = [];

        public static Dataset Parse(Stream stream, DicomTransferSyntax syntax, long end, ushort delimiter)
        {
            var data = new Dataset();
            while (stream.Position < end)
            {
                var header = Header.Parse(stream, syntax);
                if (header.Group == 0xFFFE && header.Element == delimiter) { Skip(stream, header.Length); break; }
                if (header.Group == 0xFFFE) throw new DicomReadException("The DICOM-SEG sequence structure is invalid.");
                if (header.IsSequence || _sequences.Contains(header.Tag)) data._elements[header.Tag] = new Element(null, ReadItems(stream, syntax, header.Length));
                else if (Keep(header.Tag))
                {
                    if (header.Length == Undefined || header.Length > int.MaxValue) throw new DicomReadException("A required DICOM-SEG value has an invalid length.");
                    var bytes = new byte[header.Length];
                    stream.ReadExactly(bytes);
                    data._elements[header.Tag] = new Element(bytes, null);
                }
                else Skip(stream, header.Length);
            }
            return data;
        }

        private static List<Dataset> ReadItems(Stream stream, DicomTransferSyntax syntax, uint length)
        {
            var items = new List<Dataset>();
            var end = length == Undefined ? long.MaxValue : checked(stream.Position + length);
            while (stream.Position < end)
            {
                var item = Header.Parse(stream, syntax);
                if (item.Group == 0xFFFE && item.Element == 0xE0DD) { Skip(stream, item.Length); break; }
                if (item.Group != 0xFFFE || item.Element != 0xE000) throw new DicomReadException("A DICOM-SEG sequence item is invalid.");
                items.Add(Parse(stream, syntax, item.Length == Undefined ? long.MaxValue : checked(stream.Position + item.Length), 0xE00D));
            }
            return items;
        }

        private static bool Keep(uint tag) => tag is Modality or NumberOfFrames or Rows or Columns or PixelSpacing or BitsAllocated or PixelData or SegmentationType or SegmentNumber or SegmentLabel or ReferencedSegmentNumber or RecommendedColor or ImagePosition or ImageOrientation or SliceThickness or SpacingBetweenSlices;
        public string Text(uint tag) => Encoding.ASCII.GetString(Bytes(tag)).TrimEnd('\0', ' ');
        public string? OptionalText(uint tag) => _elements.TryGetValue(tag, out var value) && value.Value is { } bytes ? Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ') : null;
        public byte[] Bytes(uint tag) => _elements.TryGetValue(tag, out var value) && value.Value is { } bytes ? bytes : throw new DicomReadException("A required DICOM-SEG tag is missing.");
        public byte[]? OptionalBytes(uint tag) => _elements.TryGetValue(tag, out var value) ? value.Value : null;
        public IReadOnlyList<Dataset> Items(uint tag) => _elements.TryGetValue(tag, out var value) && value.Items is { } items ? items : [];
        public ushort Number(uint tag, DicomTransferSyntax syntax) { var bytes = Bytes(tag); return bytes.Length == 2 ? DicomSegReader.Word(bytes, 0, syntax != DicomTransferSyntax.ExplicitVrBigEndian) : throw new DicomReadException("A required numeric DICOM-SEG tag has an invalid length."); }
    }

    private readonly record struct Header(ushort Group, ushort Element, ushort Vr, uint Length)
    {
        public uint Tag => ((uint)Group << 16) | Element;
        public bool IsSequence => Vr == 0x5351;
        public static Header Parse(Stream stream, DicomTransferSyntax syntax)
        {
            Span<byte> first = stackalloc byte[8];
            stream.ReadExactly(first);
            var little = syntax != DicomTransferSyntax.ExplicitVrBigEndian;
            var group = little ? BinaryPrimitives.ReadUInt16LittleEndian(first) : BinaryPrimitives.ReadUInt16BigEndian(first);
            var element = little ? BinaryPrimitives.ReadUInt16LittleEndian(first[2..]) : BinaryPrimitives.ReadUInt16BigEndian(first[2..]);
            if (group == 0xFFFE || syntax == DicomTransferSyntax.ImplicitVrLittleEndian) return new(group, element, 0, little ? BinaryPrimitives.ReadUInt32LittleEndian(first[4..]) : BinaryPrimitives.ReadUInt32BigEndian(first[4..]));
            var vr = (ushort)((first[4] << 8) | first[5]);
            if (vr is not (0x4F42 or 0x4F44 or 0x4F46 or 0x4F4C or 0x4F56 or 0x4F57 or 0x5351 or 0x5543 or 0x554E or 0x5552 or 0x5554))
                return new(group, element, vr, little ? BinaryPrimitives.ReadUInt16LittleEndian(first[6..]) : BinaryPrimitives.ReadUInt16BigEndian(first[6..]));
            Span<byte> size = stackalloc byte[4]; stream.ReadExactly(size);
            return new(group, element, vr, little ? BinaryPrimitives.ReadUInt32LittleEndian(size) : BinaryPrimitives.ReadUInt32BigEndian(size));
        }
    }
}
