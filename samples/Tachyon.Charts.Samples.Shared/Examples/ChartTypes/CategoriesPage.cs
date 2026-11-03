using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Categories“ of the showroom.</summary>
public static class CategoriesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Categories";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Plain;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Theme(BuiltInThemes.Instrument)
        .XAxis(a => a.Category(["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"], "x"))
        .YAxis(a => a.Numeric("y").Range(0, 10))
        .AddSeries((x, y) => new BarSeries([[3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4]], x, y))
        .Modifiers(Navigate);
}
