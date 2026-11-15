using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>
/// The picture of the cloud from a file: drawn until its pages have come onto the GPU. Made on request only, as it
/// writes a test file of 2.4 GB the first time.
/// </summary>
public sealed class CloudFromAFilePicture : IScene
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "CloudFile3D";

    /// <inheritdoc />
    string IScene.Name => Name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var scene = new Chart3DBuilder();
        ThreeD.CloudFromAFilePage.Configure(scene);
        var builder = new ChartBuilder();
        scene.Apply(builder);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 6)
        {
            chart.Render(device, target);
            device.WaitForIdle();
        }

        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}
