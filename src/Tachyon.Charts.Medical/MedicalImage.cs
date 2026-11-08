using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>An uncompressed DICOM transfer syntax supported by the reader.</summary>
public enum DicomTransferSyntax
{
    /// <summary>Implicit value representations with little-endian values.</summary>
    ImplicitVrLittleEndian,
    /// <summary>Explicit value representations with little-endian values.</summary>
    ExplicitVrLittleEndian,
    /// <summary>Explicit value representations with big-endian values.</summary>
    ExplicitVrBigEndian,
}

/// <summary>The grayscale direction of a DICOM image.</summary>
public enum DicomPhotometricInterpretation
{
    /// <summary>The lowest value is shown as black.</summary>
    Monochrome2,
    /// <summary>The lowest value is shown as white.</summary>
    Monochrome1,
}

/// <summary>Spacing between rows and columns, in millimetres.</summary>
/// <param name="Row">The distance between adjacent rows.</param>
/// <param name="Column">The distance between adjacent columns.</param>
public readonly record struct ImageSpacing(double Row, double Column);

/// <summary>Patient-space directions of an image plane.</summary>
/// <param name="Across">The direction in which the column index increases.</param>
/// <param name="Down">The direction in which the row index increases.</param>
public readonly record struct PatientOrientation(Point3D Across, Point3D Down)
{
    /// <summary>Gets the normal of the image plane.</summary>
    public Point3D Normal => Point3D.Cross(Across, Down).Normalized();
}

/// <summary>One grayscale image read from a DICOM data set.</summary>
public sealed class MedicalImage
{
    private readonly PixelBuffer _pixels;

    internal MedicalImage(DicomImageMetadata metadata, PixelBuffer pixels)
    {
        Width = metadata.Width;
        Height = metadata.Height;
        BitsAllocated = metadata.BitsAllocated;
        IsSigned = metadata.IsSigned;
        TransferSyntax = metadata.TransferSyntax;
        PhotometricInterpretation = metadata.PhotometricInterpretation;
        Spacing = metadata.Spacing;
        Position = metadata.Position;
        Orientation = metadata.Orientation;
        SliceThickness = metadata.SliceThickness;
        SpacingBetweenSlices = metadata.SpacingBetweenSlices;
        RescaleSlope = metadata.RescaleSlope;
        RescaleIntercept = metadata.RescaleIntercept;
        WindowCenter = metadata.WindowCenter;
        WindowWidth = metadata.WindowWidth;
        Modality = metadata.Modality;
        SeriesInstanceUid = metadata.SeriesInstanceUid;
        _pixels = pixels;
    }

    /// <summary>Gets the number of columns.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }

    /// <summary>Gets the number of allocated bits per pixel.</summary>
    public int BitsAllocated { get; }

    /// <summary>Gets whether stored pixel values are signed.</summary>
    public bool IsSigned { get; }

    /// <summary>Gets the transfer syntax used by the source data set.</summary>
    public DicomTransferSyntax TransferSyntax { get; }

    /// <summary>Gets the grayscale direction.</summary>
    public DicomPhotometricInterpretation PhotometricInterpretation { get; }

    /// <summary>Gets the in-plane spacing, or <see langword="null"/> when absent.</summary>
    public ImageSpacing? Spacing { get; }

    /// <summary>Gets the patient-space position of the first pixel, or <see langword="null"/> when absent.</summary>
    public Point3D? Position { get; }

    /// <summary>Gets the patient-space orientation, or <see langword="null"/> when absent.</summary>
    public PatientOrientation? Orientation { get; }

    /// <summary>Gets the nominal slice thickness in millimetres, or <see langword="null"/>.</summary>
    public double? SliceThickness { get; }

    /// <summary>Gets the declared distance between slice centres in millimetres, or <see langword="null"/>.</summary>
    public double? SpacingBetweenSlices { get; }

    /// <summary>Gets the rescale slope applied to stored values.</summary>
    public double RescaleSlope { get; }

    /// <summary>Gets the rescale intercept applied to stored values.</summary>
    public double RescaleIntercept { get; }

    /// <summary>Gets the first window centre from the file, or <see langword="null"/>.</summary>
    public double? WindowCenter { get; }

    /// <summary>Gets the first window width from the file, or <see langword="null"/>.</summary>
    public double? WindowWidth { get; }

    /// <summary>Gets the modality, when present.</summary>
    public string? Modality { get; }

    internal string? SeriesInstanceUid { get; }

    internal PixelBuffer Pixels => _pixels;

    /// <summary>Gets a rescaled value.</summary>
    /// <param name="column">The zero-based column.</param>
    /// <param name="row">The zero-based row.</param>
    /// <returns>The value after rescale slope and intercept.</returns>
    public double this[int column, int row]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Width);
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Height);
            return _pixels[(row * Width) + column];
        }
    }

    /// <summary>Copies all rescaled values, row by row.</summary>
    /// <param name="target">The destination with room for <c>Width × Height</c> values.</param>
    public void CopyValues(Span<float> target) => _pixels.CopyValues(target);
}

/// <summary>A regularly spaced stack of DICOM images.</summary>
public sealed class MedicalVolume
{
    /// <summary>Initializes a regularly spaced volume from already rescaled values and its physical geometry.</summary>
    /// <param name="data">The values, with columns, rows and slices mapped to X, Y and Z.</param>
    /// <param name="inPlane">The row and column spacing in millimetres.</param>
    /// <param name="sliceSpacing">The spacing between slice centres in millimetres.</param>
    /// <param name="origin">The patient-space position of the first voxel.</param>
    /// <param name="orientation">The patient-space orientation.</param>
    /// <param name="windowCenter">The preferred window centre, or <see langword="null"/>.</param>
    /// <param name="windowWidth">The preferred window width, or <see langword="null"/>.</param>
    /// <param name="modality">The modality, when known.</param>
    public MedicalVolume(
        VolumeData data,
        ImageSpacing inPlane,
        double sliceSpacing,
        Point3D origin,
        PatientOrientation orientation,
        double? windowCenter,
        double? windowWidth,
        string? modality)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        if (!double.IsFinite(inPlane.Row) || inPlane.Row <= 0 || !double.IsFinite(inPlane.Column) || inPlane.Column <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inPlane), inPlane, "Pixel spacing must be finite and positive.");
        }

        if (!double.IsFinite(sliceSpacing) || sliceSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sliceSpacing), sliceSpacing, "Slice spacing must be finite and positive.");
        }

        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "The origin must be finite.");
        }

        if (!double.IsFinite(orientation.Across.Length)
            || !double.IsFinite(orientation.Down.Length)
            || !double.IsFinite(orientation.Normal.Length)
            || orientation.Normal.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation needs two finite, independent directions.");
        }

        VoxelSpacing = new Point3D(inPlane.Column, inPlane.Row, sliceSpacing);
        Origin = origin;
        Orientation = orientation;
        WindowCenter = windowCenter;
        WindowWidth = windowWidth;
        Modality = modality;
    }

    /// <summary>Gets the values, with columns, rows and slices mapped to X, Y and Z.</summary>
    public VolumeData Data { get; }

    /// <summary>Gets the voxel spacing in millimetres along columns, rows and slices.</summary>
    public Point3D VoxelSpacing { get; }

    /// <summary>Gets the patient-space position of the first voxel.</summary>
    public Point3D Origin { get; }

    /// <summary>Gets the patient-space orientation.</summary>
    public PatientOrientation Orientation { get; }

    /// <summary>Gets the first window centre from the series, or <see langword="null"/>.</summary>
    public double? WindowCenter { get; }

    /// <summary>Gets the first window width from the series, or <see langword="null"/>.</summary>
    public double? WindowWidth { get; }

    /// <summary>Gets the modality, when present.</summary>
    public string? Modality { get; }
}

internal enum PixelBufferKind
{
    Byte,
    Word,
    Float,
}

internal sealed class PixelBuffer
{
    private readonly byte[]? _bytes;
    private readonly ushort[]? _words;
    private readonly float[]? _floats;

    public PixelBuffer(byte[] values) => _bytes = values;

    public PixelBuffer(ushort[] values, double slope = 1, double intercept = 0) => (_words, Slope, Intercept) = (values, slope, intercept);

    public PixelBuffer(float[] values) => _floats = values;

    /// <summary>Gets the factor of a stored 16-bit value.</summary>
    public double Slope { get; } = 1;

    /// <summary>Gets the value of a stored 16-bit zero.</summary>
    public double Intercept { get; }

    public PixelBufferKind Kind
    {
        get
        {
            if (_bytes is not null)
            {
                return PixelBufferKind.Byte;
            }

            return _words is not null ? PixelBufferKind.Word : PixelBufferKind.Float;
        }
    }

    public int Length => _bytes?.Length ?? _words?.Length ?? _floats!.Length;

    public double this[int index]
    {
        get
        {
            if (_bytes is not null)
            {
                return _bytes[index];
            }

            return _words is not null ? (_words[index] * Slope) + Intercept : _floats![index];
        }
    }

    public void CopyValues(Span<float> target)
    {
        if (target.Length < Length)
        {
            throw new ArgumentException("The destination is shorter than the image.", nameof(target));
        }

        if (_floats is not null)
        {
            _floats.CopyTo(target);
            return;
        }

        for (var i = 0; i < Length; i++)
        {
            target[i] = (float)this[i];
        }
    }

    public void CopyTo(byte[] target, int offset) => _bytes!.CopyTo(target, offset);

    public void CopyTo(ushort[] target, int offset) => _words!.CopyTo(target, offset);

    public void CopyTo(float[] target, int offset)
    {
        if (_floats is not null)
        {
            _floats.CopyTo(target, offset);
            return;
        }

        for (var i = 0; i < Length; i++)
        {
            target[offset + i] = (float)this[i];
        }
    }
}

internal sealed record DicomImageMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int BitsAllocated { get; set; }
    public int BitsStored { get; set; }
    public int HighBit { get; set; }
    public bool IsSigned { get; set; }
    public int SamplesPerPixel { get; set; } = 1;
    public int NumberOfFrames { get; set; } = 1;
    public DicomTransferSyntax TransferSyntax { get; set; }
    public DicomPhotometricInterpretation PhotometricInterpretation { get; set; } = DicomPhotometricInterpretation.Monochrome2;
    public ImageSpacing? Spacing { get; set; }
    public Point3D? Position { get; set; }
    public PatientOrientation? Orientation { get; set; }
    public double? SliceThickness { get; set; }
    public double? SpacingBetweenSlices { get; set; }
    public double RescaleSlope { get; set; } = 1;
    public double RescaleIntercept { get; set; }
    public double? WindowCenter { get; set; }
    public double? WindowWidth { get; set; }
    public string? Modality { get; set; }
    public string? SeriesInstanceUid { get; set; }
}
