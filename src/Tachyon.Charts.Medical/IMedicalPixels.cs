using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

/// <summary>The grayscale picture of a plane or a reformatted section, as the MPR series draws it.</summary>
internal interface IMedicalPixels
{
    /// <summary>Gets the number of columns.</summary>
    int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    int Height { get; }

    /// <summary>Gets the size of a pixel in millimetres.</summary>
    ImageSpacing Spacing { get; }

    /// <summary>Gets a number that grows with each new picture.</summary>
    long Version { get; }

    /// <summary>Reads a value; NaN outside the volume.</summary>
    /// <param name="column">The column.</param>
    /// <param name="row">The row.</param>
    /// <returns>The value.</returns>
    float Read(int column, int row);

    /// <summary>Copies the picture into a texture of 32-bit floats.</summary>
    /// <param name="texture">The texture.</param>
    void Write(IGpuTexture texture);
}
