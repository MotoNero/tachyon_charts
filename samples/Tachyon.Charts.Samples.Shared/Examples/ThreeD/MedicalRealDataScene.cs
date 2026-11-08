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

/// <summary>
/// A medical showroom page with real data as one 2 × 2 picture: the 3D chart and its three 2D charts. The data must have
/// been downloaded before, for example in the showroom; these pictures are made only on request and never in tests.
/// </summary>
public sealed class MedicalRealDataScene : IScene
{
    private const int ChartCount = 4;
    private readonly string _name;
    private readonly string _page;
    private readonly int _data;
    private readonly Action<MedicalMprDisplay>? _measure;

    /// <summary>The snapshot name of the analysis page with the chest CT.</summary>
    public const string Name = "MedicalRealAnalysis";

    /// <summary>Initializes the analysis page with the chest CT.</summary>
    public MedicalRealDataScene()
        : this(Name, "Medical analysis", 2, null)
    {
    }

    private MedicalRealDataScene(string name, string page, int data, Action<MedicalMprDisplay>? measure)
    {
        (_name, _page, _data, _measure) = (name, page, data, measure);
    }

    /// <summary>Gets the pictures: analysis, sections and measurements of the chest CT, and the head MRI.</summary>
    public static IReadOnlyList<IScene> All { get; } =
    [
        new MedicalRealDataScene(),
        new MedicalRealDataScene("MedicalRealSections", "Medical oblique and curved", 2, null),
        new MedicalRealDataScene("MedicalRealMeasurements", "Medical CT and MRI", 2, Measure),
        new MedicalRealDataScene("MedicalRealMri", "Medical oblique and curved", 3, null),
    ];

    string IScene.Name => _name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(1400, 1000);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var page = Pages3D.Find(_page) ?? throw new InvalidOperationException($"The page {_page} is missing.");
        var data = (MedicalDataChoice)page.Data!;
        data.Select(_data);
        var threeD = new Chart3DBuilder();
        page.Configure(threeD);
        var builder = new ChartBuilder();
        threeD.Apply(builder);
        var charts = new ChartSurface[ChartCount];
        charts[0] = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        for (var index = 1; index < ChartCount; index++)
        {
            var companion = new ChartBuilder();
            page.GridCompanions![index - 1](companion);
            charts[index] = companion.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        }

        if (_measure is not null && data.Current is { } display)
        {
            _measure(display);
        }

        var size = new PixelSize(target.Size.Width / 2, target.Size.Height / 2);
        var targets = new IOffscreenRenderTarget[ChartCount];
        for (var index = 0; index < ChartCount; index++)
        {
            targets[index] = device.CreateOffscreenTarget(size, target.DpiScale);
            var passes = index == 0 ? 400 : 2;
            for (var pass = 0; pass < passes && (pass < 2 || !threeD.Scene.Volumes.All(volume => volume.IsReady)); pass++)
            {
                charts[index].Render(device, targets[index]);
            }

            charts[index].Render(device, targets[index]);
        }

        device.WaitForIdle();
        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(0, 0, 0, 255));
        var textures = new IGpuTexture[ChartCount];
        for (var index = 0; index < ChartCount; index++)
        {
            var pixels = new Rgba8[size.PixelCount];
            targets[index].ReadPixels(pixels);
            textures[index] = device.CreateTexture(new TextureDesc(size, TextureFormat.Rgba8Unorm));
            textures[index].Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
            frame.DrawImage(textures[index], new PixelRect((index % 2) * size.Width, (index / 2) * size.Height, size.Width, size.Height));
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

    // A distance, an angle, a rectangle and a freehand region on the axial slice, in shares of the slice.
    private static void Measure(MedicalMprDisplay display)
    {
        var axial = display.Axial;
        var (w, h) = (axial.Width * axial.Spacing.Column, axial.Height * axial.Spacing.Row);
        var layer = axial.Measurements;
        layer.Add(MedicalMeasurementKind.Distance, [new(w * 0.2, h * 0.62), new(w * 0.47, h * 0.62)], name: "Right lung");
        layer.Add(MedicalMeasurementKind.Angle, [new(w * 0.62, h * 0.3), new(w * 0.5, h * 0.42), new(w * 0.72, h * 0.5)]);
        layer.Add(MedicalMeasurementKind.Rectangle, [new(w * 0.46, h * 0.63), new(w * 0.54, h * 0.7)], name: "Aorta");
        var region = new MedicalPoint[10];
        for (var i = 0; i < region.Length; i++)
        {
            var angle = 2 * Math.PI * i / region.Length;
            region[i] = new(w * (0.63 + (0.07 * Math.Cos(angle))), h * (0.42 + (0.09 * Math.Sin(angle))));
        }

        layer.Add(MedicalMeasurementKind.Freehand, region, name: "Heart");
    }
}
