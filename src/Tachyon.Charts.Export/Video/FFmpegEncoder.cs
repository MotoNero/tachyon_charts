using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export.Video;

/// <summary>
/// Writes H.264 in MP4 with an FFmpeg the user installed: the frames go raw through its standard input. FFmpeg is not
/// part of the package, so its licence does not come along.
/// </summary>
internal sealed class FFmpegEncoder : IVideoEncoder
{
    private readonly Process _process;
    private readonly Stream _input;

    public FFmpegEncoder(string program, string path, PixelSize size, int frameRate, int bitRate)
    {
        var arguments = string.Create(
            CultureInfo.InvariantCulture,
            $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {size.Width}x{size.Height} -r {frameRate} -i - -c:v libx264 -b:v {bitRate} -pix_fmt yuv420p -movflags +faststart \"{path}\"");
        var start = new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        _process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg did not start.");
        _input = _process.StandardInput.BaseStream;
    }

    /// <summary>Finds FFmpeg: the given path, else the folders of the PATH variable.</summary>
    /// <param name="given">A path the user gave, or <see langword="null"/>.</param>
    /// <returns>The program, or <see langword="null"/> if there is none.</returns>
    public static string? Find(string? given)
    {
        if (given is not null)
        {
            return File.Exists(given) ? given : null;
        }

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Write(ReadOnlySpan<Rgba8> pixels) => _input.Write(MemoryMarshal.AsBytes(pixels));

    public void Finish()
    {
        _input.Close();
        var errors = _process.StandardError.ReadToEnd();
        _process.WaitForExit();
        if (_process.ExitCode != 0)
        {
            throw new InvalidOperationException("FFmpeg could not write the video: " + errors.Trim());
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
        }

        _process.Dispose();
    }
}
