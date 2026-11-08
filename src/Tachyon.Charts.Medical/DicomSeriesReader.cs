using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>Builds a regularly spaced volume from single-frame DICOM images.</summary>
public static class DicomSeriesReader
{
    /// <summary>Reads and combines a series. Streams remain owned by the caller.</summary>
    /// <param name="streams">Readable and seekable DICOM Part 10 streams in any slice order.</param>
    /// <returns>The volume sorted by patient-space slice position.</returns>
    /// <exception cref="DicomReadException">The images do not form one regular series.</exception>
    public static MedicalVolume Read(IEnumerable<Stream> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var images = new List<MedicalImage>();
        foreach (var stream in streams)
        {
            images.Add(DicomReader.Read(stream));
        }

        return Build(images);
    }

    /// <summary>Combines already read images into a regularly spaced volume.</summary>
    /// <param name="images">Images from one series in any slice order.</param>
    /// <returns>The volume sorted by patient-space slice position.</returns>
    /// <exception cref="DicomReadException">The images do not form one regular series.</exception>
    public static MedicalVolume Build(IEnumerable<MedicalImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var slices = images.ToList();
        if (slices.Count < 2)
        {
            throw new DicomReadException("A volume requires at least two images.");
        }

        var first = slices[0];
        var uid = first.SeriesInstanceUid;
        var orientation = first.Orientation;
        var spacing = first.Spacing;
        if (string.IsNullOrEmpty(uid) || orientation is null || spacing is null || first.Position is null)
        {
            throw new DicomReadException("The series UID, image position, orientation or pixel spacing is missing.");
        }

        for (var i = 1; i < slices.Count; i++)
        {
            ValidateCompatible(first, slices[i], uid, orientation.Value, spacing.Value);
        }

        var normal = orientation.Value.Normal;
        slices.Sort((left, right) => Coordinate(left.Position!.Value, normal).CompareTo(Coordinate(right.Position!.Value, normal)));
        var coordinates = new double[slices.Count];
        for (var i = 0; i < slices.Count; i++)
        {
            coordinates[i] = Coordinate(slices[i].Position!.Value, normal);
        }

        var sliceSpacing = (coordinates[^1] - coordinates[0]) / (coordinates.Length - 1);
        if (!double.IsFinite(sliceSpacing) || sliceSpacing <= 0)
        {
            throw new DicomReadException("Slice positions must be distinct along the image normal.");
        }

        var tolerance = Math.Max(1e-3, sliceSpacing * 1e-3);
        for (var i = 1; i < coordinates.Length; i++)
        {
            if (Math.Abs((coordinates[i] - coordinates[i - 1]) - sliceSpacing) > tolerance)
            {
                throw new DicomReadException("Slice positions are not regularly spaced.");
            }
        }

        var data = BuildData(slices, first.Width, first.Height);
        return new MedicalVolume(
            data,
            spacing.Value,
            sliceSpacing,
            slices[0].Position!.Value,
            orientation.Value,
            first.WindowCenter,
            first.WindowWidth,
            first.Modality);
    }

    private static void ValidateCompatible(
        MedicalImage first,
        MedicalImage image,
        string uid,
        PatientOrientation orientation,
        ImageSpacing spacing)
    {
        if (!string.Equals(uid, image.SeriesInstanceUid, StringComparison.Ordinal))
        {
            throw new DicomReadException("The images belong to different series.");
        }

        if (image.Width != first.Width
            || image.Height != first.Height
            || image.BitsAllocated != first.BitsAllocated
            || image.IsSigned != first.IsSigned
            || image.Position is null
            || image.Orientation is null
            || image.Spacing is null)
        {
            throw new DicomReadException("The images have incompatible geometry or pixel formats.");
        }

        var otherOrientation = image.Orientation.Value;
        var otherSpacing = image.Spacing.Value;
        if (!Near(orientation.Across, otherOrientation.Across)
            || !Near(orientation.Down, otherOrientation.Down)
            || !Near(spacing.Row, otherSpacing.Row)
            || !Near(spacing.Column, otherSpacing.Column))
        {
            throw new DicomReadException("The images have inconsistent orientation or pixel spacing.");
        }
    }

    private static VolumeData BuildData(IReadOnlyList<MedicalImage> slices, int width, int height)
    {
        var sliceLength = checked(width * height);
        var length = checked(sliceLength * slices.Count);
        var kind = slices[0].Pixels.Kind;
        for (var i = 1; i < slices.Count; i++)
        {
            if (slices[i].Pixels.Kind != kind || slices[i].Pixels.Slope != slices[0].Pixels.Slope || slices[i].Pixels.Intercept != slices[0].Pixels.Intercept)
            {
                kind = PixelBufferKind.Float;
                break;
            }
        }

        switch (kind)
        {
            case PixelBufferKind.Byte:
                var bytes = new byte[length];
                for (var i = 0; i < slices.Count; i++)
                {
                    slices[i].Pixels.CopyTo(bytes, i * sliceLength);
                }

                return new VolumeData(bytes, width, height, slices.Count);
            case PixelBufferKind.Word:
                var words = new ushort[length];
                for (var i = 0; i < slices.Count; i++)
                {
                    slices[i].Pixels.CopyTo(words, i * sliceLength);
                }

                return new VolumeData(words, width, height, slices.Count, slices[0].Pixels.Slope, slices[0].Pixels.Intercept);
            default:
                var values = new float[length];
                for (var i = 0; i < slices.Count; i++)
                {
                    slices[i].Pixels.CopyTo(values, i * sliceLength);
                }

                return new VolumeData(values, width, height, slices.Count);
        }
    }

    private static double Coordinate(Point3D position, Point3D normal) => Point3D.Dot(position, normal);

    private static bool Near(Point3D left, Point3D right) =>
        Near(left.X, right.X) && Near(left.Y, right.Y) && Near(left.Z, right.Z);

    private static bool Near(double left, double right) => Math.Abs(left - right) <= 1e-5;
}
