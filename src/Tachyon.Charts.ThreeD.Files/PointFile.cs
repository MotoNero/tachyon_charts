using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>A file that cannot be read as points, a volume or a model, with the reason in the message.</summary>
public sealed class PointFileException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PointFileException"/> class.</summary>
    public PointFileException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PointFileException"/> class.</summary>
    /// <param name="message">The reason.</param>
    public PointFileException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PointFileException"/> class.</summary>
    /// <param name="message">The reason.</param>
    /// <param name="innerException">The cause.</param>
    public PointFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The points of a file.</summary>
/// <param name="x">The first coordinates.</param>
/// <param name="y">The second coordinates.</param>
/// <param name="z">The third coordinates.</param>
/// <param name="colors">The colors, or <see langword="null"/> when the file has none.</param>
public sealed class PointFileData(double[] x, double[] y, double[] z, Rgba8[]? colors)
{
    /// <summary>Gets the first coordinates.</summary>
    public double[] X { get; } = x;

    /// <summary>Gets the second coordinates.</summary>
    public double[] Y { get; } = y;

    /// <summary>Gets the third coordinates.</summary>
    public double[] Z { get; } = z;

    /// <summary>Gets the colors.</summary>
    public Rgba8[]? Colors { get; } = colors;

    /// <summary>Creates a cloud. It takes over the arrays, so the points exist only once.</summary>
    /// <returns>The cloud.</returns>
    public PointCloud3D ToCloud() => new(X, Y, Z, Colors);
}

/// <summary>
/// Reads clouds of points from XYZ, PLY and LAS with code of its own. All readers know the number of points before they
/// start and write straight into the final arrays, so a file of 100 million points is never held twice. LAZ is not
/// supported.
/// </summary>
public static class PointFile
{
    /// <summary>Reads a file by its extension.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The points.</returns>
    /// <exception cref="PointFileException">The file is damaged or of an unknown kind.</exception>
    public static PointFileData Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path).ToUpperInvariant();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return extension switch
        {
            ".XYZ" or ".TXT" or ".PTS" or ".CSV" => ReadXyz(stream),
            ".PLY" => ReadPly(stream),
            ".LAS" => ReadLas(stream),
            ".LAZ" => throw new PointFileException("LAZ files are compressed and not supported. Convert the file to LAS first."),
            _ => throw new PointFileException($"The kind of file '{extension}' is unknown. Known are XYZ, PLY and LAS."),
        };
    }

    /// <summary>Reads text with x, y and z per line, optionally followed by red, green and blue from 0 to 255.</summary>
    /// <param name="stream">The file. It must allow seeking, as the lines are counted first.</param>
    /// <returns>The points.</returns>
    /// <exception cref="PointFileException">The file is damaged.</exception>
    public static PointFileData ReadXyz(Stream stream) => XyzReader.Read(Seekable(stream));

    /// <summary>Reads a PLY file, as text or binary in either byte order.</summary>
    /// <param name="stream">The file. It must allow seeking.</param>
    /// <returns>The points.</returns>
    /// <exception cref="PointFileException">The file is damaged.</exception>
    public static PointFileData ReadPly(Stream stream) => PlyReader.Read(Seekable(stream));

    /// <summary>Reads a LAS file of the versions 1.0 to 1.4.</summary>
    /// <param name="stream">The file. It must allow seeking.</param>
    /// <returns>The points.</returns>
    /// <exception cref="PointFileException">The file is damaged.</exception>
    public static PointFileData ReadLas(Stream stream) => LasReader.Read(Seekable(stream));

    /// <summary>Creates the arrays, and turns a lack of memory into a clear message.</summary>
    /// <param name="count">The number of points.</param>
    /// <param name="hasColors">Whether colors follow.</param>
    /// <returns>The empty points.</returns>
    internal static PointFileData Allocate(long count, bool hasColors)
    {
        if (count <= 0)
        {
            throw new PointFileException("The file holds no points.");
        }

        if (count > Array.MaxLength)
        {
            throw new PointFileException($"The file holds {count} points, more than one list can take.");
        }

        try
        {
            return new PointFileData(new double[count], new double[count], new double[count], hasColors ? new Rgba8[count] : null);
        }
        catch (OutOfMemoryException exception)
        {
            throw new PointFileException($"There is not enough memory for {count} points.", exception);
        }
    }

    private static Stream Seekable(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.CanSeek ? stream : throw new ArgumentException("The stream must allow seeking.", nameof(stream));
    }
}
