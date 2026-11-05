using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Axes in the origin“ of the showroom: a function whose axes cross at zero, as on paper.</summary>
public static class AxesInTheOriginPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Axes in the origin";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan, the axes stay at zero or at the nearest edge | Double click: all";

    /// <summary>Gets a function with values on both sides of zero.</summary>
    /// <returns>The points.</returns>
    public static DataSource<double, double> Function()
    {
        var data = new DataSource<double, double>(1001);
        for (var i = 0; i <= 1000; i++)
        {
            var x = -10 + (i * 0.02);
            data.Append(x, (x * Math.Sin(x) / 2) - 1);
        }

        return data;
    }

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c.Title("y = x · sin(x) / 2 − 1")
        .XAxis(a => a.Numeric("x").Range(-10, 10).CrossesAt(0))
        .YAxis(a => a.Numeric("y").Range(-6, 6).CrossesAt(0))
        .AddLine(Function(), s => s.Name("y").Thickness(2))
        .Modifiers(m => m.ZoomPan().Crosshair().Tooltip());
}
