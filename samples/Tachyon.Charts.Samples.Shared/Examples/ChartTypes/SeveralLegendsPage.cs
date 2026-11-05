using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Several legends“ of the showroom: temperatures and pressures, each with a legend of its own.</summary>
public static class SeveralLegendsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Several legends";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Click in a legend: switch a series off and on | Pointer on a legend: highlight the series | Wheel: zoom | Drag: pan";

    private static readonly string[] _temperatures = ["Inlet", "Core", "Outlet"];
    private static readonly string[] _pressures = ["Pump", "Filter", "Valve"];
    private static readonly Rgba8[] _colors =
    [
        new(255, 190, 11, 255), new(251, 86, 7, 255), new(247, 37, 133, 255),
        new(76, 201, 240, 255), new(58, 134, 255, 255), new(6, 214, 160, 255),
    ];

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        c.Title("Test bench")
            .XAxis(a => a.Numeric("t").Title("Time in s").Range(0, 60))
            .YAxis(a => a.Numeric("temperature").Title("Temperature in °C").Range(0, 120))
            .YAxis(a => a.Numeric("pressure").Title("Pressure in bar").Range(0, 12).Position(AxisPosition.Right));
        for (var i = 0; i < 3; i++)
        {
            var (name, color) = (_temperatures[i], _colors[i]);
            c.AddLine(Curve(i, 40 + (i * 25), 8), s => s.Name(name).Stroke(color).Thickness(1.5).YAxis("temperature"));
        }

        for (var i = 0; i < 3; i++)
        {
            var (name, color) = (_pressures[i], _colors[i + 3]);
            c.AddLine(Curve(i + 3, 3 + (i * 2.5), 0.8), s => s.Name(name).Stroke(color).Thickness(1.5).YAxis("pressure"));
        }

        c.Legend(l => l.Title("Temperatures").Series(_temperatures).Place(LegendPlacement.TopLeft))
            .Legend(l => l.Title("Pressures").Series(_pressures).Place(LegendPlacement.Bottom))
            .Modifiers(m => m.ZoomPan());
    }

    private static DataSource<double, double> Curve(int seed, double level, double swing)
    {
        var data = new DataSource<double, double>(601);
        for (var i = 0; i <= 600; i++)
        {
            var t = i * 0.1;
            data.Append(t, level + (swing * Math.Sin((t * 0.15) + seed)) + (swing * 0.3 * Math.Sin((t * 0.9) + (seed * 2))));
        }

        return data;
    }
}
