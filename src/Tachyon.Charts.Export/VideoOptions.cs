using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Export;

/// <summary>Chooses the program that encodes a video.</summary>
public enum VideoEncoder
{
    /// <summary>Media Foundation on Windows, FFmpeg elsewhere.</summary>
    Auto,

    /// <summary>Media Foundation, part of Windows.</summary>
    MediaFoundation,

    /// <summary>An FFmpeg the user installed.</summary>
    FFmpeg,
}

/// <summary>The size, length and encoding of a video of a chart.</summary>
public sealed record VideoOptions
{
    /// <summary>Gets the width in pixels. Odd values are rounded down, as H.264 needs even sizes.</summary>
    public int Width { get; init; } = 1920;

    /// <summary>Gets the height in pixels. Odd values are rounded down.</summary>
    public int Height { get; init; } = 1080;

    /// <summary>Gets the frames per second.</summary>
    public int FrameRate { get; init; } = 30;

    /// <summary>Gets the length.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the bits per second of the encoded video.</summary>
    public int BitRate { get; init; } = 12_000_000;

    /// <summary>Gets the theme of the video, or <see langword="null"/> for the theme of the chart.</summary>
    public Theme? Theme { get; init; }

    /// <summary>Gets the program that encodes.</summary>
    public VideoEncoder Encoder { get; init; } = VideoEncoder.Auto;

    /// <summary>Gets the path of FFmpeg, or <see langword="null"/> to look for it in the folders of the PATH variable.</summary>
    public string? FFmpegPath { get; init; }
}
