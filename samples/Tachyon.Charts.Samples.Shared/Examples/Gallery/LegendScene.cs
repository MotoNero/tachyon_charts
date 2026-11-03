using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

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
