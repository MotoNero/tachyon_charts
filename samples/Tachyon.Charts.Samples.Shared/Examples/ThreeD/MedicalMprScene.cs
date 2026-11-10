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
    private readonly bool _measured;

    /// <summary>The snapshot name.</summary>
    public const string Name = "MedicalMpr";

    /// <summary>Creates the medical MPR scene.</summary>
    public MedicalMprScene()
        : this(Name, null)
    {
    }

    internal MedicalMprScene(string name, MedicalSegmentation3DMode? segmentationMode, bool measured = false)
    {
        _name = name;
        _segmentationMode = segmentationMode;
        _measured = measured;
        Size = segmentationMode == MedicalSegmentation3DMode.Volume || measured ? new(900, 640) : new(1000, 800);
    }

    string IScene.Name => _name;

    /// <inheritdoc />
    public PixelSize Size { get; }

    // The categorical volume alone, in the whole picture.
    private static void RenderVolumeOnly(IRenderDevice device, IRenderTarget target, ChartSurface[] charts, MedicalMprDisplay display)
    {
        RenderUntilLoaded(device, target, charts[0], display);
        device.WaitForIdle();
        foreach (var chart in charts)
        {
            foreach (var series in chart.Series) (series as IDisposable)?.Dispose();
        }
    }

    // A distance and a circle on the axial plane, an angle on the coronal one and a rectangle on the sagittal one, in
    // shares of each plane; the 3D chart shows them as lines on their slices.
    private static void Measure(MedicalMprDisplay display)
    {
        static (double Width, double Height) Size(MedicalMprSlice slice) => (slice.Width * slice.Spacing.Column, slice.Height * slice.Spacing.Row);
        var (w, h) = Size(display.Axial);
        display.Axial.Measurements.Add(MedicalMeasurementKind.Distance, [new(w * 0.2, h * 0.7), new(w * 0.8, h * 0.7)]);
        display.Axial.Measurements.Add(MedicalMeasurementKind.Circle, [new(w * 0.34, h * 0.43), new(w * 0.4, h * 0.43)]);
        (w, h) = Size(display.Coronal);
        display.Coronal.Measurements.Add(MedicalMeasurementKind.Angle, [new(w * 0.3, h * 0.3), new(w * 0.5, h * 0.6), new(w * 0.7, h * 0.3)]);
        (w, h) = Size(display.Sagittal);
        display.Sagittal.Measurements.Add(MedicalMeasurementKind.Rectangle, [new(w * 0.35, h * 0.35), new(w * 0.65, h * 0.6)]);
    }

    // The volume reaches the GPU over several frames; the picture shows it whole.
    private static void RenderUntilLoaded(IRenderDevice device, IRenderTarget target, ChartSurface chart, MedicalMprDisplay display)
    {
        for (var pass = 0; pass < 200 && (pass < 2 || !display.Volume.IsReady); pass++)
        {
            chart.Render(device, target);
        }

        chart.Render(device, target);
    }

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
        if (_measured)
        {
            Measure(display);
        }

        var chartSize = new PixelSize(target.Size.Width / 2, target.Size.Height / 2);
        var charts = BuildCharts(display);
        if (_segmentationMode == MedicalSegmentation3DMode.Volume || _measured)
        {
            RenderVolumeOnly(device, target, charts, display);
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

        RenderUntilLoaded(device, targets[0], charts[0], display);

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
