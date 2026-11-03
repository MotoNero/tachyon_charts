using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Ring buffers after several wraps: few points drawn one by one, and many points reduced to buckets.
/// </summary>
public sealed class LineFifoWrapScene : LineScene
{
    private const int Laps = 3;

    /// <inheritdoc />
    public override string Name => "LineFifoWrap";

    /// <summary>
    /// Fills a ring, so that it holds the X range 0 to 10 after some laps.
    /// </summary>
    /// <param name="capacity">The size of the ring.</param>
    /// <param name="function">The values.</param>
    /// <returns>The ring.</returns>
    public static FifoDataSource<double, double> CreateRing(int capacity, Func<double, double> function)
    {
        var ring = new FifoDataSource<double, double>(capacity);
        var total = capacity * Laps;
        for (var i = 0; i < total; i++)
        {
            var x = (i - total + capacity) * 10.0 / (capacity - 1);
            ring.Append(x, function(x));
        }

        return ring;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var few = new LineSeries<double, double>(CreateRing(301, value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        var many = new LineSeries<double, double>(
            CreateRing(200_000, value => (0.3 * Math.Sin(value * 2)) - 0.6 + (Math.Sin(value * 4000) * 0.1)),
            x,
            y)
        {
            Color = Orange,
            Thickness = 1,
        };
        chart.AddSeries(few);
        chart.AddSeries(many);
        return [few, many];
    }
}
