using System.Diagnostics;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>Shows a saved file to the user, for the dialog after an export of the showroom.</summary>
public static class FileLocation
{
    /// <summary>
    /// Opens the folder of a file: on Windows in the explorer with the file selected, elsewhere with the program the
    /// system opens folders with.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    public static void Show(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var process = OperatingSystem.IsWindows()
            ? Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })
            : Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path) ?? path) { UseShellExecute = true });
    }
}
