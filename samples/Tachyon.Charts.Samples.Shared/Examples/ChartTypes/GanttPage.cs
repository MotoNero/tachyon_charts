using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Gantt“ of the showroom: a test plan over three stations with dependencies and milestones.</summary>
public static class GanttPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Gantt";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = GanttDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("Test plan")
        .XAxis(a => a.DateTime("time").AutoRange())
        .Gantt(GanttDemo.TestPlan())
        .Modifiers(m => m.ZoomPan());
}
