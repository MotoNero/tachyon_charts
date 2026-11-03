using System.Numerics;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

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
