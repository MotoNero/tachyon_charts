using System.Globalization;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Three charts in a group on top of each other: labels of different widths, yet aligned plots, and the
/// zoom of the first chart in all of them.
/// </summary>
public sealed class GroupStackedScene : IScene
{
    private const int ChartCount = 3;

    /// <inheritdoc />
    public string Name => "GroupStacked";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 600);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var size = new PixelSize(target.Size.Width, target.Size.Height / ChartCount);
        var group = new ChartGroup();
        var parts = new (ChartSurface Chart, IDisposable Series)[ChartCount];
        double[] heights = [1, 100_000, 25];
        Rgba8[] colors = [new(80, 170, 240, 255), new(240, 130, 90, 255), new(110, 210, 130, 255)];
        for (var i = 0; i < ChartCount; i++)
        {
            var height = heights[i];
            var x = new NumericAxis("x", AxisPosition.Bottom)
            {
                VisibleRange = new DoubleRange(0, 10),
                LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
            };
            var y = new NumericAxis("y", AxisPosition.Left)
            {
                VisibleRange = new DoubleRange(-1.2 * height, 1.2 * height),
                LabelFormat = new NumericLabelFormat { Culture = CultureInfo.InvariantCulture },
            };
            var chart = new ChartSurface([x], [new LayoutPane([y])], SkiaTextEngine.Default);
            var source = new DataSource<double, double>();
            for (var point = 0; point < 400; point++)
            {
                var value = point * 0.025;
                source.Append(value, height * Math.Sin((value * (i + 1)) + i));
            }

            var line = new LineSeries<double, double>(source, x, y) { Color = colors[i], Thickness = 1.5 };
            chart.AddSeries(line);
            group.Add(chart);
            parts[i] = (chart, line);
        }

        var targets = new IOffscreenRenderTarget[ChartCount];
        for (var i = 0; i < ChartCount; i++)
        {
            targets[i] = device.CreateOffscreenTarget(size, target.DpiScale);
        }

        parts[0].Chart.HorizontalAxes[0].VisibleRange = new DoubleRange(2, 8);

        // Twice: the first frame tells the group the room each chart needs.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < ChartCount; i++)
            {
                parts[i].Chart.Render(device, targets[i]);
            }
        }

        device.WaitForIdle();
        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(0, 0, 0, 255));
        var textures = new IGpuTexture[ChartCount];
        for (var i = 0; i < ChartCount; i++)
        {
            var pixels = new Rgba8[size.PixelCount];
            targets[i].ReadPixels(pixels);
            textures[i] = device.CreateTexture(new TextureDesc(size, TextureFormat.Rgba8Unorm));
            textures[i].Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
            frame.DrawImage(textures[i], new PixelRect(0, i * size.Height, size.Width, size.Height));
        }

        frame.Submit();
        device.WaitForIdle();
        for (var i = 0; i < ChartCount; i++)
        {
            textures[i].Dispose();
            targets[i].Dispose();
            parts[i].Series.Dispose();
        }
    }
}
