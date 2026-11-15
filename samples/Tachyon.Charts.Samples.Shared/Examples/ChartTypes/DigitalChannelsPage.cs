using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „Digital channels“ of the showroom: sixteen channels of a logic analyzer, a billion points each, one
/// above the other. Zoomed out each pixel column shows an edge where its channel changes, down to a single nanosecond.
/// </summary>
public static class DigitalChannelsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Digital channels";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | Pointer: values";

    /// <summary>The points per channel.</summary>
    public const long Count = 1_000_000_000;

    private static readonly Lazy<Core.Data.DigitalTransitions[]> _channels = new(() => LogicBus.Create(Count));

    /// <summary>Gets the names of the lanes from the bottom up, as the axis lists them.</summary>
    public static IReadOnlyList<string> Lanes { get; } = [.. LogicBus.Names.Reverse()];

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        c.Title(SampleText.T("16 channels of 1 billion points each"))
            .XAxis(a => a.Numeric("x").Title(SampleText.T("Time in s")).AutoRange())
            .YAxis(a => a.Category(Lanes, "y"));
        for (var channel = 0; channel < LogicBus.Names.Count; channel++)
        {
            var (index, lane) = (channel, LogicBus.Names.Count - 1 - channel);
            c.AddSeries((x, y) => new DigitalSeries(_channels.Value[index], x, y) { Name = LogicBus.Names[index], Low = lane - 0.3, High = lane + 0.3, Color = LogicBus.ColorOf(index) });
        }

        c.Modifiers(m => m.ZoomPan().RubberBand().Tooltip());
    }
}
