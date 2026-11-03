using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// A chart with modifiers and simulated input. The layout must exist before input can hit anything,
/// so the scene renders twice.
/// </summary>
public abstract class InteractionScene : LineScene
{
    /// <summary>
    /// Gets the modifiers.
    /// </summary>
    /// <returns>The modifiers.</returns>
    protected abstract IEnumerable<ChartModifier> CreateModifiers();

    /// <inheritdoc />
    public override void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, series) = CreateChart();
        Style(chart);
        chart.Render(device, target);

        var pipeline = new ModifierPipeline { Interaction = new ChartInteraction(chart) };
        foreach (var modifier in CreateModifiers())
        {
            pipeline.Add(modifier);
        }

        Simulate(pipeline, chart);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    /// <summary>
    /// Adjusts the chart.
    /// </summary>
    /// <param name="chart">The chart.</param>
    protected virtual void Style(ChartSurface chart)
    {
    }

    /// <summary>
    /// Sends the simulated input.
    /// </summary>
    /// <param name="pipeline">The modifiers.</param>
    /// <param name="chart">The chart.</param>
    protected abstract void Simulate(ModifierPipeline pipeline, ChartSurface chart);

    /// <summary>
    /// Moves the pointer to a place in the plot.
    /// </summary>
    /// <param name="pipeline">The modifiers.</param>
    /// <param name="chart">The chart.</param>
    /// <param name="x">The share of the plot width.</param>
    /// <param name="y">The share of the plot height.</param>
    /// <param name="buttons">The held buttons.</param>
    /// <param name="modifiers">The held keys.</param>
    protected static void MoveTo(ModifierPipeline pipeline, ChartSurface chart, double x, double y, PointerButtons buttons = PointerButtons.None, InputModifiers modifiers = InputModifiers.None) =>
        pipeline.Process(new PointerInputEvent(PointerEventKind.Moved, 1, At(chart, x, y), buttons, Modifiers: modifiers));

    /// <summary>
    /// Finds a place in the plot.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="x">The share of the plot width.</param>
    /// <param name="y">The share of the plot height.</param>
    /// <returns>The position.</returns>
    protected static Vector2 At(ChartSurface chart, double x, double y)
    {
        var plot = chart.CurrentLayout!.PlotArea;
        return new Vector2((float)(plot.X + (plot.Width * x)), (float)(plot.Y + (plot.Height * y)));
    }
}

/// <summary>
/// A crosshair with values on the axes, in the light theme to show that it follows the theme.
/// </summary>
public class CrosshairScene : InteractionScene
{
    /// <inheritdoc />
    public override string Name => "Crosshair";

    /// <summary>
    /// Gets a value indicating whether the crosshair snaps to points.
    /// </summary>
    protected virtual bool IsSnapping => false;

    /// <inheritdoc />
    protected override void Style(ChartSurface chart) => chart.Theme = BuiltInThemes.TachyonLight;

    /// <inheritdoc />
    protected override IEnumerable<ChartModifier> CreateModifiers() => [new CrosshairModifier { SnapToData = IsSnapping, SnapDistance = 60 }];

    /// <inheritdoc />
    protected override void Simulate(ModifierPipeline pipeline, ChartSurface chart) => MoveTo(pipeline, chart, 0.37, IsSnapping ? 0.74 : 0.3);

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3), 40), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);
        return [line];
    }
}

/// <summary>
/// A crosshair sitting on the nearest point.
/// </summary>
public sealed class CrosshairSnapScene : CrosshairScene
{
    /// <inheritdoc />
    public override string Name => "CrosshairSnap";

    /// <inheritdoc />
    protected override bool IsSnapping => true;
}

/// <summary>
/// A vertical line with the values of three series, two of them close together.
/// </summary>
public sealed class RolloverScene : InteractionScene
{
    /// <inheritdoc />
    public override string Name => "Rollover";

    /// <inheritdoc />
    protected override IEnumerable<ChartModifier> CreateModifiers() => [new RolloverModifier()];

    /// <inheritdoc />
    protected override void Simulate(ModifierPipeline pipeline, ChartSurface chart) => MoveTo(pipeline, chart, 0.44, 0.5);

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var first = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2, Name = "Druck" };
        var second = new LineSeries<double, double>(CreateData(value => (0.9 * Math.Sin(value * 1.3)) + 0.05), x, y) { Color = Green, Thickness = 2, Name = "Soll" };
        var third = new LineSeries<double, double>(CreateData(value => 0.6 * Math.Cos(value * 0.8)), x, y) { Color = Orange, Thickness = 2, Name = "Temperatur" };
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.AddSeries(third);
        return [first, second, third];
    }
}

/// <summary>
/// All kinds of annotations over a line.
/// </summary>
public sealed class AnnotationsScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "Annotations";

    /// <summary>
    /// Creates a small checkerboard with a transparent half.
    /// </summary>
    /// <returns>The pixels of 8 by 8 squares.</returns>
    private static Rgba8[] CreateImage()
    {
        var pixels = new Rgba8[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var alpha = (byte)(x < 4 ? 255 : 120);
                pixels[(y * 8) + x] = (x + y) % 2 == 0 ? new Rgba8(255, 255, 255, alpha) : new Rgba8(200, 60, 160, alpha);
            }
        }

        return pixels;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);

        var band = new BandAnnotation(x) { Color = new Rgba8(110, 210, 130, 45) };
        band.SetRange(7.5, 9);
        chart.AddOverlay(band);

        var box = new BoxAnnotation(x, y);
        box.SetCorners(2, 1.2, 4, 0.4);
        chart.AddOverlay(box);

        chart.AddOverlay(new HorizontalLineAnnotation(y) { Y = -1.1, Label = "Grenzwert", Color = new Rgba8(240, 90, 90, 255) });
        chart.AddOverlay(new VerticalLineAnnotation(x) { X = 6, Label = "Start", Color = Green });

        var text = new TextAnnotation(x, y) { Text = "Maximum", Color = Orange };
        text.MoveTo(0.75, 1.1);
        chart.AddOverlay(text);

        var arrow = new ArrowAnnotation(x, y) { Color = Orange };
        arrow.SetEnds(0.9, 1.05, 1.21, 0.935);
        chart.AddOverlay(arrow);

        var marker = new MarkerAnnotation(x, y) { Color = new Rgba8(240, 90, 90, 255), Size = 14 };
        marker.MoveTo(3.62, -0.9);
        chart.AddOverlay(marker);

        var image = new ImageAnnotation(CreateImage(), new PixelSize(8, 8), null, null) { DisplaySize = (64, 64) };
        image.MoveTo(800, 60);
        chart.AddOverlay(image);

        return [line];
    }
}

/// <summary>
/// Scattered points after a lasso selection, the selected ones marked.
/// </summary>
public sealed class SelectionScene : InteractionScene
{
    /// <inheritdoc />
    public override string Name => "Selection";

    /// <inheritdoc />
    protected override IEnumerable<ChartModifier> CreateModifiers() => [new SelectionModifier { Mode = SelectionMode.Lasso }];

    /// <inheritdoc />
    protected override void Simulate(ModifierPipeline pipeline, ChartSurface chart)
    {
        (double X, double Y)[] outline = [(0.2, 0.2), (0.55, 0.15), (0.75, 0.5), (0.5, 0.85), (0.3, 0.6)];
        var start = At(chart, outline[0].X, outline[0].Y);
        pipeline.Process(new PointerInputEvent(PointerEventKind.Pressed, 1, start, PointerButtons.Primary, PointerButton.Primary, InputModifiers.Shift));
        foreach (var (x, y) in outline.Skip(1))
        {
            MoveTo(pipeline, chart, x, y, PointerButtons.Primary, InputModifiers.Shift);
        }

        pipeline.Process(new PointerInputEvent(PointerEventKind.Released, 1, At(chart, 0.3, 0.6), ChangedButton: PointerButton.Primary));
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var random = new Random(5);
        var source = new DataSource<double, double>();
        for (var i = 0; i < 400; i++)
        {
            source.Append(random.NextDouble() * 10, (random.NextDouble() * 3) - 1.5);
        }

        var points = new ScatterSeries<double, double>(source, x, y) { Color = Blue, Size = 5 };
        chart.AddSeries(points);
        return [points];
    }
}

/// <summary>
/// Two cursors across one period of a sine.
/// </summary>
public sealed class MeasureCursorsScene : InteractionScene
{
    /// <inheritdoc />
    public override string Name => "MeasureCursors";

    /// <inheritdoc />
    protected override IEnumerable<ChartModifier> CreateModifiers() => [new MeasureCursorModifier()];

    /// <inheritdoc />
    protected override void Simulate(ModifierPipeline pipeline, ChartSurface chart)
    {
        pipeline.Process(new PointerInputEvent(PointerEventKind.Pressed, 1, At(chart, 0.12, 0.2), PointerButtons.Primary, PointerButton.Primary, InputModifiers.Alt));
        MoveTo(pipeline, chart, 0.6, 0.72, PointerButtons.Primary, InputModifiers.Alt);
        pipeline.Process(new PointerInputEvent(PointerEventKind.Released, 1, At(chart, 0.6, 0.72), ChangedButton: PointerButton.Primary));
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);
        return [line];
    }
}

/// <summary>
/// A legend with a hidden series and one under the pointer.
/// </summary>
public sealed class LegendScene : InteractionScene
{
    /// <inheritdoc />
    public override string Name => "Legend";

    /// <inheritdoc />
    protected override IEnumerable<ChartModifier> CreateModifiers() => [new LegendModifier()];

    /// <inheritdoc />
    public override void Render(IRenderDevice device, IRenderTarget target)
    {
        // The legend knows its rows only after a frame with it.
        var (chart, series) = CreateChart();
        chart.Render(device, target);
        var pipeline = new ModifierPipeline { Interaction = new ChartInteraction(chart) };
        var legend = new LegendModifier();
        pipeline.Add(legend);
        chart.Render(device, target);

        var rows = legend.Overlay!.Rows;
        var hidden = rows[1].Bounds;
        var hovered = rows[0].Bounds;
        pipeline.Process(new PointerInputEvent(PointerEventKind.Pressed, 1, new Vector2((float)(hidden.X + 20), (float)(hidden.Y + 4)), PointerButtons.Primary, PointerButton.Primary));
        pipeline.Process(new PointerInputEvent(PointerEventKind.Moved, 1, new Vector2((float)(hovered.X + 20), (float)(hovered.Y + 4))));
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc />
    protected override void Simulate(ModifierPipeline pipeline, ChartSurface chart)
    {
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var first = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2, Name = "Druck" };
        var second = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Cos(value * 1.3)), x, y) { Color = Orange, Thickness = 2, Name = "Versteckt" };
        var third = new LineSeries<double, double>(CreateData(value => 0.5 * Math.Cos(value * 0.8)), x, y) { Color = Green, Thickness = 2, Name = "Temperatur" };
        chart.AddSeries(first);
        chart.AddSeries(second);
        chart.AddSeries(third);
        return [first, second, third];
    }
}

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

/// <summary>
/// A small chart with the window of another one marked.
/// </summary>
public sealed class OverviewScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "Overview";

    /// <inheritdoc />
    public override void Render(IRenderDevice device, IRenderTarget target)
    {
        var (chart, series) = CreateChart();
        var mainAxis = new NumericAxis("main", AxisPosition.Bottom) { VisibleRange = new DoubleRange(3, 6) };
        var main = new ChartSurface([mainAxis], [new Tachyon.Charts.Core.Layout.LayoutPane([new NumericAxis("y", AxisPosition.Left)])], SkiaTextEngine.Default);
        var interaction = new ChartInteraction(main);
        var pipeline = new ModifierPipeline { Interaction = new ChartInteraction(chart) };
        pipeline.Add(new OverviewModifier(() => interaction));
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);
        return [line];
    }
}
