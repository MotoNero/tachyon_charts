using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

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
