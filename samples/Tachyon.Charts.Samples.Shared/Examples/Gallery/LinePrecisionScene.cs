using System.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.Samples.Shared.Spikes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Two milliseconds of 2026 timestamps at 1 µs resolution on a large value offset. Absolute floats
/// would collapse the whole line into one point.
/// </summary>
public sealed class LinePrecisionScene : IScene
{
    private const int SampleCount = 2001;
    private const long TicksPerMicrosecond = 10;
    private const double ValueOffset = 1_000_000_000;

    /// <summary>
    /// Gets the first timestamp of the scene.
    /// </summary>
    public static long StartTicks { get; } = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;

    /// <inheritdoc />
    public string Name => "LinePrecision";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 300);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var origin = new RelativeOrigin(StartTicks, (SampleCount - 1) * (double)TicksPerMicrosecond / Size.Width);
        var values = new YMapping(ValueOffset - 1.2e-3, ValueOffset + 1.2e-3, Size.Height);

        var positions = new Vector2[SampleCount];
        for (var i = 0; i < SampleCount; i++)
        {
            var ticks = StartTicks + (i * TicksPerMicrosecond);
            var value = ValueOffset + (1e-3 * Math.Sin(i * Math.Tau / 500));
            positions[i] = new Vector2(origin.ToPixel(ticks), values.ToPixel(value));
        }

        using var points = device.CreateBuffer(new BufferDesc(positions.Length * 8L, BufferUsage.Vertex));
        points.Write<Vector2>(0, positions);

        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(24, 28, 36, 255));
        frame.DrawPolyline(points, 0, SampleCount, 2, new Rgba8(240, 180, 60, 255));
        frame.Submit();
    }
}
