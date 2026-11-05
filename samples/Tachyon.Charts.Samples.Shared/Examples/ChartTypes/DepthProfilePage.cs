using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Depth profile“ of the showroom: a borehole log, turned so that the depth runs down.</summary>
public static class DepthProfilePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Depth profile";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Pointer: values | Double click: all";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var (gamma, temperature, samples) = (new DataSource<double, double>(1501), new DataSource<double, double>(1501), new DataSource<double, double>(16));
        for (var depth = 0; depth <= 1500; depth++)
        {
            // Layers of clay show as high gamma rays; the rock warms by about 3 °C per 100 m.
            var layer = Math.Sin(depth / 90.0) + (0.5 * Math.Sin(depth / 23.0)) + (0.25 * Math.Sin(depth / 7.0));
            gamma.Append(depth, 60 + (30 * layer));
            temperature.Append(depth, 12 + (depth * 0.03) + (2 * Math.Sin(depth / 300.0)));
        }

        for (var i = 0; i < 16; i++)
        {
            var depth = 60 + (i * 90.0);
            samples.Append(depth, 12 + (depth * 0.03) + (2 * Math.Sin(depth / 300.0)) + (i % 3 == 0 ? 2.5 : -1.5));
        }

        c.Title("Borehole log").XDown()
            .XAxis(a => a.Numeric("depth").Title("Depth in m").Range(0, 1500))
            .YAxis(a => a.Numeric("value").Title("Gamma in API, temperature in °C").Range(0, 120))
            .AddSeries((x, y) => new AreaSeries(gamma, x, y) { Fill = new SolidFill(new Rgba8(255, 190, 11, 90)) })
            .AddLine(temperature, s => s.Name("Temperature").Stroke(new Rgba8(247, 37, 133, 255)).Thickness(2))
            .AddScatter(samples, s => s.Name("Core samples").Size(9).Fill(new Rgba8(76, 201, 240, 255)))
            .Modifiers(m => m.ZoomPan().Crosshair().Tooltip());
    }
}
