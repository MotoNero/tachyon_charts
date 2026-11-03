namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// A WGSL shader failed to compile.
/// </summary>
public sealed class ShaderCompilationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderCompilationException"/> class.
    /// </summary>
    /// <param name="fileName">The shader file.</param>
    /// <param name="line">The 1-based line of the error, or 0 if the compiler reported none.</param>
    /// <param name="column">The 1-based column of the error, or 0 if the compiler reported none.</param>
    /// <param name="compilerMessage">The compiler output.</param>
    public ShaderCompilationException(string fileName, int line, int column, string compilerMessage)
        : base($"{fileName}:{line}:{column}: {compilerMessage}")
    {
        FileName = fileName;
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Gets the shader file.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the 1-based line of the error, or 0 if the compiler reported none.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the 1-based column of the error, or 0 if the compiler reported none.
    /// </summary>
    public int Column { get; }
}
