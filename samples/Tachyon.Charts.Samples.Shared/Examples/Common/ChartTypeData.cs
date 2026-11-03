using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;

using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Axes, data and operation that the chart type examples share.</summary>
public static class ChartTypeData
{
    internal const string Plain = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all";
    internal const string Points = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all | Pointer: values";
    internal const string Radial = "Wheel: zoom the radius";
    internal const string Edit = "Drag an annotation: move it | Wheel: zoom | Drag: pan";

    internal static readonly double[][] Bars = [[3, 5, 2, 6, 4, 7], [2, 1, 4, 3, 5, 2], [1, 3, 2, 2, 2, 3]];

    internal static readonly string[] Degrees = ["0°", "45°", "90°", "135°", "180°", "225°", "270°", "315°"];


    internal static DataSource<double, double> Cut()
    {
        var curve = HillCut();
        var data = new DataSource<double, double>(curve.X.Length);
        for (var i = 0; i < curve.X.Length; i++)
        {
            data.Append(curve.X[i], curve.Values[i]);
        }

        return data;
    }

    internal static ChartBuilder Numeric(ChartBuilder builder, double left, double right, double bottom, double top) => builder
        .Theme(BuiltInThemes.Instrument)
        .XAxis(a => a.Numeric("x").Range(left, right))
        .YAxis(a => a.Numeric("y").Range(bottom, top));

    internal static ChartBuilder Categories(ChartBuilder builder) => builder
        .Theme(BuiltInThemes.Instrument)
        .XAxis(a => a.Category(["Normal", "Skewed", "Two peaks"], "x"))
        .YAxis(a => a.Numeric("y").Range(0, 8));

    internal static void Polar(ChartBuilder builder, double radius, IReadOnlyList<string> spokes, Func<AxisBase, AxisBase, ChartSeries> series) => builder
        .Theme(BuiltInThemes.Instrument)
        .XAxis(a => a.Numeric("angle").Range(0, 360).Hidden())
        .YAxis(a => a.Numeric("radial").Range(0, radius).Hidden())
        .AddSeries(series)
        .AddOverlay(chart => new PolarGridOverlay(chart.Series[0].YAxis, spokes))
        .Modifiers(m => m.Custom(() => new RadialZoomModifier()));

    internal static void Navigate(ModifierBuilder modifiers) => modifiers.ZoomPan().RubberBand();

    internal static void WithValues(ModifierBuilder modifiers) => modifiers.ZoomPan().RubberBand().Crosshair().Tooltip();

    /// <summary>Samples a curve.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The points.</returns>
    public static DataSource<double, double> Sample(Func<double, double> curve, int count = 300, double length = 10)
    {
        var data = new DataSource<double, double>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i * length / (count - 1);
            data.Append(x, curve(x));
        }

        return data;
    }

    internal static DataSource<double, double> Cloud()
    {
        var data = new DataSource<double, double>(40);
        for (var i = 0; i < 40; i++)
        {
            data.Append(0.5 + (9 * ((i * 0.618) % 1)), 0.5 + (9 * ((i * 0.7549) % 1)));
        }

        return data;
    }

    internal static float[] Sizes() => [.. Enumerable.Range(0, 40).Select(i => 6f + (i % 7 * 6))];

    internal static double[] Errors() => [.. Enumerable.Range(0, 13).Select(i => 0.15 + (0.05 * (i % 5)))];

    internal static double[] Angles() => [.. Enumerable.Range(0, 361).Select(i => (double)i)];

    internal static double[] Skewed()
    {
        var random = new Random(3);
        return [.. Enumerable.Range(0, 5000).Select(_ => -Math.Log(1 - random.NextDouble()) * 2)];
    }

    internal static double[][] GroupedSamples()
    {
        var random = new Random(5);
        return
        [
            [.. Enumerable.Range(0, 300).Select(_ => (random.NextDouble() + random.NextDouble() + random.NextDouble()) * 2)],
            [.. Enumerable.Range(0, 300).Select(_ => -Math.Log(1 - random.NextDouble()) + 1)],
            [.. Enumerable.Range(0, 300).Select(_ => random.Next(2) == 0 ? 2 + random.NextDouble() : 5 + random.NextDouble())],
        ];
    }
}
