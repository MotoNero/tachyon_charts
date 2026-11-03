using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

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
