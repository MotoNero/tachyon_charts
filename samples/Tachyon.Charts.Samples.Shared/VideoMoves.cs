using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>Moves for the videos of the showroom.</summary>
public static class VideoMoves
{
    /// <summary>Turns a camera once around the scene during the video, one equal step per frame.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="frames">The frames of the video.</param>
    /// <returns>The step for each frame.</returns>
    public static Action<int, TimeSpan> Orbit(Camera3D camera, int frames)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);
        var degrees = 360.0 / frames;
        return (frame, _) =>
        {
            if (frame > 0)
            {
                camera.Orbit(degrees, 0);
            }
        };
    }
}
