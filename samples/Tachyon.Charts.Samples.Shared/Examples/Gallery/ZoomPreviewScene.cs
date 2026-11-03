using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// The preview of a rubber band zoom over a line.
/// </summary>
public sealed class ZoomPreviewScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "ZoomPreview";

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);
        chart.ZoomRectangle = new DipRect(200, 120, 320, 200);
        return [line];
    }
}
