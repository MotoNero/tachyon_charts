using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Last value at the axis“ of the showroom: three live temperatures with their newest values at the axis.</summary>
public static class LastValuePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Last value at the axis";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = LastValueDemo.Hint;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var lines = LastValueDemo.Live();
        c.Title("Temperatures, live")
            .XAxis(a => a.Numeric("t").Title("Time in s").AutoRange())
            .YAxis(a => a.Numeric("temperature").Title("Temperature in °C").Range(45, 75).Position(Tachyon.Charts.Core.Axes.AxisPosition.Right));
        for (var i = 0; i < lines.Length; i++)
        {
            var name = LastValueDemo.Names[i];
            var index = i;
            c.AddLine(lines[i], s => s.Name(name).Stroke(LastValueDemo.Colors[index]).Thickness(1.5).LastValue());
        }

        c.Modifiers(m => m.ZoomPan());
    }
}
