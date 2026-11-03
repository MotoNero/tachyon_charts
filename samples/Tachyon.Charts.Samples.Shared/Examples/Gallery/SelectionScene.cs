using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

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
