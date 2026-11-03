using System.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// Fans of 0.5, 1 and 3 pixel lines in many directions, to judge edge quality.
/// </summary>
public sealed class LineThinScene : IScene
{
    private const int LinesPerFan = 24;

    private static readonly float[] _widths = [0.5f, 1f, 3f];

    /// <inheritdoc />
    public string Name => "LineThin";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 300);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        using var points = device.CreateBuffer(new BufferDesc(LinesPerFan * _widths.Length * 16L, BufferUsage.Vertex));
        for (var fan = 0; fan < _widths.Length; fan++)
        {
            var center = new Vector2(150 + (fan * 300), 150);
            for (var line = 0; line < LinesPerFan; line++)
            {
                var angle = line * MathF.Tau / LinesPerFan;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                points.Write<Vector2>(((fan * LinesPerFan) + line) * 16L, [center + (direction * 20), center + (direction * 130)]);
            }
        }

        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(24, 28, 36, 255));
        for (var i = 0; i < LinesPerFan * _widths.Length; i++)
        {
            frame.DrawPolyline(points, i * 2, 2, _widths[i / LinesPerFan], new Rgba8(240, 240, 240, 255));
        }

        frame.Submit();
    }
}
