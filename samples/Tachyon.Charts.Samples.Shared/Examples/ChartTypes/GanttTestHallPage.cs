using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Gantt, 100 000 tasks“ of the showroom: 100 stations in ten halls with a thousand tests each.</summary>
public static class GanttTestHallPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Gantt, 100 000 tasks";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = GanttDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Test hall: 100 stations, 100 000 tests")
        .XAxis(a => a.DateTime("time").AutoRange())
        .Gantt(GanttDemo.TestHall())
        .Modifiers(m => m.ZoomPan());
}
