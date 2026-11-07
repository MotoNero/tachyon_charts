using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The four linked medical MPR charts in their showroom layout.</summary>
public sealed class MedicalMprScene : IScene
{
    private const int ChartCount = 4;

    /// <summary>The snapshot name.</summary>
    public const string Name = "MedicalMpr";

    string IScene.Name => Name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1000, 800);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var display = new MedicalMprDisplay(MedicalPhantom.Volume());
        var chartSize = new PixelSize(target.Size.Width / 2, target.Size.Height / 2);
        var charts = BuildCharts(display);
        var targets = new IOffscreenRenderTarget[ChartCount];
        for (var index = 0; index < ChartCount; index++)
        {
            targets[index] = device.CreateOffscreenTarget(chartSize, target.DpiScale);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var index = 0; index < ChartCount; index++)
            {
                charts[index].Render(device, targets[index]);
            }
        }

        device.WaitForIdle();
        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(0, 0, 0, 255));
        var textures = new IGpuTexture[ChartCount];
        for (var index = 0; index < ChartCount; index++)
        {
            var pixels = new Rgba8[chartSize.PixelCount];
            targets[index].ReadPixels(pixels);
            textures[index] = device.CreateTexture(new TextureDesc(chartSize, TextureFormat.Rgba8Unorm));
            textures[index].Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
            frame.DrawImage(
                textures[index],
                new PixelRect((index % 2) * chartSize.Width, (index / 2) * chartSize.Height, chartSize.Width, chartSize.Height));
        }

        frame.Submit();
        device.WaitForIdle();
        for (var index = 0; index < ChartCount; index++)
        {
            textures[index].Dispose();
            targets[index].Dispose();
            foreach (var series in charts[index].Series)
            {
                (series as IDisposable)?.Dispose();
            }
        }
    }

    private static ChartSurface[] BuildCharts(MedicalMprDisplay display)
    {
        var threeD = new Chart3DBuilder();
        display.Configure3D(threeD);
        var threeDChart = new ChartBuilder();
        threeD.Apply(threeDChart);

        var axial = new ChartBuilder();
        display.Axial.Configure(axial);
        var coronal = new ChartBuilder();
        display.Coronal.Configure(coronal);
        var sagittal = new ChartBuilder();
        display.Sagittal.Configure(sagittal);
        return
        [
            threeDChart.ToDefinition().Build(SkiaTextEngine.Default).Surface,
            axial.ToDefinition().Build(SkiaTextEngine.Default).Surface,
            coronal.ToDefinition().Build(SkiaTextEngine.Default).Surface,
            sagittal.ToDefinition().Build(SkiaTextEngine.Default).Surface,
        ];
    }
}
