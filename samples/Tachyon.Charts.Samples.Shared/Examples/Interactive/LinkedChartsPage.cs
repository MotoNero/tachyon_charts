using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Samples.Shared.Examples.Interactive;

/// <summary>Three charts in one group: zoom and crosshair apply to all of them, the axes line up.</summary>
public static class LinkedChartsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Gruppe (3 Charts)";

    /// <summary>How to operate the charts.</summary>
    public const string Hint = "Zoom and crosshair apply to all three charts";

    /// <summary>Gets the charts: a title and a scale each, so that the axes need different widths.</summary>
    public static IReadOnlyList<(string Title, double Scale)> Charts { get; } = [("Small", 1), ("Large", 100_000), ("Medium", 25)];

    /// <summary>Builds one of the charts.</summary>
    /// <param name="c">The chart.</param>
    /// <param name="title">The title.</param>
    /// <param name="scale">The scale of the values.</param>
    /// <param name="group">The name of the group.</param>
    public static void Configure(ChartBuilder c, string title, double scale, string group)
    {
        var source = new DataSource<double, double>(2000);
        for (var i = 0; i < 2000; i++)
        {
            source.Append(i * 0.01, scale * Math.Sin((i * 0.01) * (1 + (scale % 3))));
        }

        c.Title(title)
            .Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("x").Range(0, 20))
            .YAxis(a => a.Numeric("y").AutoRange())
            .AddLine(source, s => s.Thickness(1.5))
            .Group(group)
            .Modifiers(m => m.ZoomPan().Crosshair());
    }
}
