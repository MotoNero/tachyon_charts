using Tachyon.Charts.Medical;

namespace Tachyon.Charts.Configurator.Medical;

/// <summary>
/// Reads own medical files for the preview and writes the same reading as C#. The paths stay in memory: the generated
/// code takes them from the command line, never as text in the code or the saved configuration.
/// </summary>
internal static class MedicalFiles
{
    private static readonly object _gate = new();
    private static (string Path, DateTime Written, MedicalVolume Volume)? _last;

    /// <summary>Names what a medical source misses.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    public static string? Problem(SeriesSpec series) =>
        series.Data.Path is not { Length: > 0 } path ? $"Choose the medical file of {series.Name}."
        : !File.Exists(path) && !Directory.Exists(path) ? $"The medical file of {series.Name} is not there."
        : series.Data.Segmentation is { Length: > 0 } segmentation && !File.Exists(segmentation) ? $"The segmentation file of {series.Name} is not there."
        : null;

    /// <summary>Reads a DICOM image.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The display.</returns>
    public static MedicalImageDisplay ReadImage(string path)
    {
        using var stream = File.OpenRead(path);
        return new MedicalImageDisplay(DicomReader.Read(stream));
    }

    /// <summary>
    /// Reads a volume: a NIfTI file, or a DICOM series whose folder a path or one of its files names. The preview keeps
    /// the last volume, so that changing a setting does not read the series again.
    /// </summary>
    /// <param name="path">The file or folder.</param>
    /// <returns>A display of its own for the volume.</returns>
    public static MedicalMprDisplay ReadVolume(string path)
    {
        var written = Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
        lock (_gate)
        {
            if (_last is not { } last || last.Path != path || last.Written != written)
            {
                _last = (path, written, Volume(path));
            }

            return new MedicalMprDisplay(_last.Value.Volume);
        }
    }

    private static MedicalVolume Volume(string path)
    {
        if (IsNifti(path))
        {
            using var stream = File.OpenRead(path);
            return NiftiImageReader.Read(stream);
        }

        var streams = SeriesFiles(path).Select(File.OpenRead).ToList();
        try
        {
            return DicomSeriesReader.Read(streams);
        }
        finally
        {
            streams.ForEach(stream => stream.Dispose());
        }
    }

    /// <summary>Reads a segmentation: NIfTI labels or DICOM SEG.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The segmentation.</returns>
    public static MedicalSegmentation ReadSegmentation(string path)
    {
        using var stream = File.OpenRead(path);
        return IsNifti(path) ? NiftiSegmentationReader.Read(stream) : DicomSegReader.Read(stream);
    }

    private static bool IsNifti(string path) => path.EndsWith(".nii", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".nii.gz", StringComparison.OrdinalIgnoreCase);

    // The files of a series: those of the folder without an extension or with one of DICOM.
    private static IEnumerable<string> SeriesFiles(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Directory.GetFiles(folder).Where(file => Path.GetExtension(file).ToUpperInvariant() is "" or ".DCM" or ".IMA").Order(StringComparer.Ordinal);
    }

    /// <summary>Writes the reading of a DICOM image as C#.</summary>
    /// <param name="series">The series.</param>
    /// <param name="method">The name of the method.</param>
    /// <returns>The code.</returns>
    public static DataCode ImageCode(SeriesSpec series, string method) => new(
        nameof(MedicalImageDisplay),
        method,
        $"{method}(ChartSetup.MedicalData)",
        Paths(series, "The DICOM image to show") + $$"""

                /// <summary>Reads the image of {{System.Security.SecurityElement.Escape(series.Name)}} from a DICOM file.</summary>
                /// <param name="path">The file.</param>
                /// <returns>The display of the image.</returns>
                public static MedicalImageDisplay {{method}}(string path)
                {
                    using var stream = File.OpenRead(path);
                    return new MedicalImageDisplay(DicomReader.Read(stream));
                }

            """,
        ["Tachyon.Charts.Medical"]);

    /// <summary>Writes the reading of a volume as C#, with its segmentation file when there is one.</summary>
    /// <param name="series">The series.</param>
    /// <param name="method">The name of the method.</param>
    /// <returns>The code.</returns>
    public static DataCode VolumeCode(SeriesSpec series, string method) => new(
        nameof(MedicalMprDisplay),
        method,
        $"{method}(ChartSetup.MedicalData)",
        Paths(series, "The NIfTI volume, or a file or the folder of a DICOM series, to show") + $$"""

                /// <summary>Reads the volume of {{System.Security.SecurityElement.Escape(series.Name)}}: a NIfTI file, or a DICOM series whose folder the path or one of its files names.</summary>
                /// <param name="path">The file or folder.</param>
                /// <returns>The display of the volume.</returns>
                public static MedicalMprDisplay {{method}}(string path)
                {
                    if (IsNifti(path))
                    {
                        using var stream = File.OpenRead(path);
                        return new MedicalMprDisplay(NiftiImageReader.Read(stream));
                    }

                    var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
                    var files = Directory.GetFiles(folder).Where(file => Path.GetExtension(file).ToUpperInvariant() is "" or ".DCM" or ".IMA").Order(StringComparer.Ordinal);
                    var streams = files.Select(File.OpenRead).ToList();
                    try
                    {
                        return new MedicalMprDisplay(DicomSeriesReader.Read(streams));
                    }
                    finally
                    {
                        streams.ForEach(stream => stream.Dispose());
                    }
                }

                private static bool IsNifti(string path) => path.EndsWith(".nii", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".nii.gz", StringComparison.OrdinalIgnoreCase);

            """ + (series.Data.Segmentation is { Length: > 0 } ? SegmentationCode : string.Empty),
        ["Tachyon.Charts.Medical"]);

    // The places of the data come from the command line, so that no path of medical data stands in the code.
    private static string Paths(SeriesSpec series, string what) => $$"""

                /// <summary>{{what}}: the first argument of the command line. Paths of medical data are not written into the code.</summary>
                public static string MedicalData { get; set; } = Environment.GetCommandLineArgs() is { Length: > 1 } arguments ? arguments[1] : string.Empty;

            """ + (series.Data.Segmentation is { Length: > 0 } ? """

                /// <summary>The segmentation of the volume, NIfTI labels or DICOM SEG: the second argument of the command line.</summary>
                public static string SegmentationData { get; set; } = Environment.GetCommandLineArgs() is { Length: > 2 } arguments ? arguments[2] : string.Empty;

            """ : string.Empty);

    private const string SegmentationCode = """

            /// <summary>Reads a segmentation: NIfTI labels or DICOM SEG.</summary>
            /// <param name="path">The file.</param>
            /// <returns>The segmentation.</returns>
            public static MedicalSegmentation ReadSegmentation(string path)
            {
                using var stream = File.OpenRead(path);
                return IsNifti(path) ? NiftiSegmentationReader.Read(stream) : DicomSegReader.Read(stream);
            }

        """ + MedicalSetup.WithModeCode;
}
