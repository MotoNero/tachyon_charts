using System.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// The minimal scene: background, rectangle and line.
/// </summary>
public sealed class SmokeScene : IScene
{
    /// <inheritdoc />
    public string Name => "Smoke";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(320, 200);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        using var points = device.CreateBuffer(new BufferDesc(32, BufferUsage.Vertex));
        points.Write<Vector2>(0, [new Vector2(20, 180), new Vector2(120, 80), new Vector2(200, 150), new Vector2(300, 40)]);

        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(24, 28, 36, 255));
        frame.FillRectangle(new PixelRect(40, 30, 120, 70), new Rgba8(60, 130, 230, 255));
        frame.DrawPolyline(points, 0, 4, 3, new Rgba8(80, 220, 120, 255));
        frame.Submit();
    }
}
