using System.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.Samples.Shared.Spikes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A decimated line over the complete signal.
/// </summary>
/// <param name="name">The scene name.</param>
/// <param name="pointCount">The number of samples.</param>
public sealed class LineSpikeScene(string name, int pointCount) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1200, 400);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var values = SpikeSignal.Get(pointCount);
        var decimator = new MinMaxDecimator(Size.Width);
        var positions = new Vector2[decimator.MaxPointCount];
        var count = decimator.Decimate(values, 0, values.Length, Size.Width, new YMapping(-1.1, 1.1, Size.Height), positions);

        using var points = device.CreateBuffer(new BufferDesc(positions.Length * 8L, BufferUsage.Vertex));
        points.Write<Vector2>(0, positions.AsSpan(0, count));

        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(24, 28, 36, 255));
        frame.DrawPolyline(points, 0, count, 1, new Rgba8(80, 220, 120, 255));
        frame.Submit();
    }
}
