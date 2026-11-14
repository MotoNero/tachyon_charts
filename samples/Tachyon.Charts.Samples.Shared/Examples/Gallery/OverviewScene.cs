using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A chart that shows a window of its data, with a small chart below that shows all of them and marks the window.
/// Dragging the window in the small chart moves the chart above.
/// </summary>
public sealed class OverviewScene : IScene
{
    /// <summary>The group that links the overview to its chart.</summary>
    public const string Group = "overview-scene";

    private static readonly Rgba8 _blue = new(80, 170, 240, 255);

    /// <inheritdoc />
    public string Name => "Overview";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 500);

    /// <summary>Builds the chart: the window from 3 to 6 of the signal.</summary>
    /// <param name="c">The chart.</param>
    public static void ConfigureMain(ChartBuilder c) => c
        .Title("Overview")
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").Range(3, 6).Title("Time"))
        .YAxis(a => a.Numeric("y").Range(-1.5, 1.5))
        .AddLine(Signal(), s => s.Name("Signal").Stroke(_blue).Thickness(2))
        .Group(Group);

    /// <summary>Builds the overview below: all of the signal, thin, with the window of the chart above.</summary>
    /// <param name="c">The overview.</param>
    public static void ConfigureOverview(ChartBuilder c) => c
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").AutoRange())
        .YAxis(a => a.Numeric("y").AutoRange())
        .AddLine(Signal(), s => s.Stroke(_blue).Thickness(1))
        .Modifiers(m => m.Overview(Group));

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        // The chart takes two thirds of the height, the overview the rest; each is drawn on its own and then placed.
        var mainSize = new PixelSize(target.Size.Width, target.Size.Height * 2 / 3);
        var overviewSize = new PixelSize(target.Size.Width, target.Size.Height - mainSize.Height);
        var main = Build(ConfigureMain);
        var overview = Build(ConfigureOverview);
        using var mainTarget = device.CreateOffscreenTarget(mainSize, target.DpiScale);
        using var overviewTarget = device.CreateOffscreenTarget(overviewSize, target.DpiScale);

        // The overview finds the chart through its group; in a window the chart control joins it, here the scene does.
        var group = ChartGroup.Get(Group);
        group.Add(main.Surface);
        _ = new ModifierPipeline { Interaction = new ChartInteraction(main.Surface) };
        var pipeline = new ModifierPipeline { Interaction = new ChartInteraction(overview.Surface) };
        foreach (var modifier in overview.Modifiers)
        {
            pipeline.Add(modifier);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            main.Surface.Render(device, mainTarget);
            overview.Surface.Render(device, overviewTarget);
        }

        device.WaitForIdle();
        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(0, 0, 0, 255));
        using var top = Picture(device, mainTarget, mainSize);
        using var bottom = Picture(device, overviewTarget, overviewSize);
        frame.DrawImage(top, new PixelRect(0, 0, mainSize.Width, mainSize.Height));
        frame.DrawImage(bottom, new PixelRect(0, mainSize.Height, overviewSize.Width, overviewSize.Height));
        frame.Submit();
        device.WaitForIdle();

        // The next picture finds its own chart in the group, not this one.
        group.Remove(main.Surface);
    }

    private static BuiltChart Build(Action<ChartBuilder> configure)
    {
        var builder = new ChartBuilder();
        configure(builder);
        return builder.ToDefinition().Build(SkiaTextEngine.Default);
    }

    private static IGpuTexture Picture(IRenderDevice device, IOffscreenRenderTarget source, PixelSize size)
    {
        var pixels = new Rgba8[size.PixelCount];
        source.ReadPixels(pixels);
        var texture = device.CreateTexture(new TextureDesc(size, TextureFormat.Rgba8Unorm));
        texture.Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
        return texture;
    }

    // The signal of the scene: 240 points of a slow sine from 0 to 10.
    private static DataSource<double, double> Signal()
    {
        const int Count = 240;
        var data = new DataSource<double, double>(Count);
        for (var i = 0; i < Count; i++)
        {
            var x = i * 10.0 / (Count - 1);
            data.Append(x, 0.9 * Math.Sin(x * 1.3));
        }

        return data;
    }
}
