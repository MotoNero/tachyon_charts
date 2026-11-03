using System.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// All primitives of the render API, including alpha blending.
/// </summary>
public sealed class PrimitivesScene : IScene
{
    /// <inheritdoc />
    public string Name => "Primitives";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(320, 200);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        using var points = device.CreateBuffer(new BufferDesc(32, BufferUsage.Vertex));
        points.Write<Vector2>(0, [new Vector2(20, 180), new Vector2(120, 120), new Vector2(200, 170), new Vector2(300, 100)]);

        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(24, 28, 36, 255));
        frame.FillTriangle(new Vector2(160, 20), new Vector2(260, 150), new Vector2(60, 150), new Rgba8(230, 60, 60, 255));
        frame.FillRectangle(new PixelRect(20, 20, 60, 40), new Rgba8(60, 130, 230, 160));
        frame.DrawPolyline(points, 0, 4, 3, new Rgba8(80, 220, 120, 255));
        frame.DrawTextPlaceholder(new PixelRect(230, 20, 70, 12), new Rgba8(200, 200, 200, 255));
        frame.Submit();
    }
}
