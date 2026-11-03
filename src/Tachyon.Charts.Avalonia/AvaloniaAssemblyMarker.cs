namespace Tachyon.Charts.Avalonia;

/// <summary>
/// Marks the assembly.
/// </summary>
public static class AvaloniaAssemblyMarker
{
    /// <summary>
    /// Gets the name of this assembly.
    /// </summary>
    public static string AssemblyName => typeof(AvaloniaAssemblyMarker).Assembly.GetName().Name!;
}
