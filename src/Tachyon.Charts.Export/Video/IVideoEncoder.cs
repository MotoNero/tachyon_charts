using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export.Video;

/// <summary>Turns frames into a video file.</summary>
internal interface IVideoEncoder : IDisposable
{
    /// <summary>Adds the next frame.</summary>
    /// <param name="pixels">The pixels, top row first.</param>
    void Write(ReadOnlySpan<Rgba8> pixels);

    /// <summary>Completes the file.</summary>
    void Finish();
}
