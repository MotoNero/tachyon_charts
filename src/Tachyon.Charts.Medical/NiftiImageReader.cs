namespace Tachyon.Charts.Medical;

/// <summary>
/// Reads the image of a NIfTI-1 file, the form in which research data such as MRI often come: values and patient-space
/// geometry only. Free-text fields of the header, such as the description, are never read.
/// </summary>
public static class NiftiImageReader
{
    /// <summary>Reads an uncompressed or gzip-compressed single-file NIfTI-1 volume.</summary>
    /// <param name="stream">The readable stream, which remains owned by the caller.</param>
    /// <returns>The volume; integers of up to 16 bits keep two bytes per value.</returns>
    /// <exception cref="InvalidDataException">The stream is malformed or unsupported.</exception>
    public static MedicalVolume Read(Stream stream) => NiftiSegmentationReader.ReadVolume(stream);
}
