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
    private readonly string _name;
    private readonly MedicalSegmentation3DMode? _segmentationMode;

    /// <summary>The snapshot name.</summary>
    public const string Name = "MedicalMpr";

    /// <summary>Creates the medical MPR scene.</summary>
    public MedicalMprScene()
        : this(Name, null)
    {
    }

    internal MedicalMprScene(string name, MedicalSegmentation3DMode? segmentationMode)
    {
        _name = name;
        _segmentationMode = segmentationMode;
        Size = segmentationMode == MedicalSegmentation3DMode.Volume ? new(900, 640) : new(1000, 800);
    }

    string IScene.Name => _name;

    /// <inheritdoc />
    public PixelSize Size { get; }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var volume = MedicalPhantom.Volume();
        var display = new MedicalMprDisplay(volume);
        if (_segmentationMode is { } mode)
        {
            var source = MedicalPhantom.Segmentation(volume);
            var segmentation = new MedicalSegmentation(source.Data, new ImageSpacing(source.VoxelSpacing.Y, source.VoxelSpacing.X), source.VoxelSpacing.Z, source.Origin, source.Orientation, source.Segments)
            {
                ThreeDMode = mode,
            };
            display.AddSegmentation(segmentation);
        }
        var chartSize = new PixelSize(target.Size.Width / 2, target.Size.Height / 2);
        var charts = BuildCharts(display);
        if (_segmentationMode == MedicalSegmentation3DMode.Volume)
        {
            charts[0].Render(device, target);
            charts[0].Render(device, target);
            device.WaitForIdle();
            foreach (var chart in charts)
            {
                foreach (var series in chart.Series) (series as IDisposable)?.Dispose();
            }

            return;
        }

        var targets = new IOffscreenRenderTarget[ChartCount];
        for (var index = 0; index < ChartCount; index++)
        {
            targets[index] = device.CreateOffscreenTarget(chartSize, target.DpiScale);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var index = 1; index < ChartCount; index++) charts[index].Render(device, targets[index]);
        }

        for (var pass = 0; pass < 2; pass++) charts[0].Render(device, targets[0]);

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
