namespace Tachyon.Charts.Medical;

/// <summary>An error in a DICOM data set or an unsupported encoding.</summary>
public sealed class DicomReadException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="DicomReadException"/> class.</summary>
    /// <param name="message">A description that contains no source values or path.</param>
    public DicomReadException(string message)
        : base(message)
    {
    }
}
