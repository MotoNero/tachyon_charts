using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Bubbles“ of the showroom: wells of three fields in space, the volume of each bubble shows its yield.</summary>
public static class BubblesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Bubbles";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Wheel: zoom | Pointer: values";

    private static readonly Rgba8[] _fields = [new(255, 190, 11, 255), new(58, 134, 255, 255), new(6, 214, 160, 255)];

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        const int PerField = 60;
        var random = new Random(11);
        var count = _fields.Length * PerField;
        var (x, y, z, yields) = (new double[count], new double[count], new double[count], new double[count]);
        var colors = new Rgba8[count];
        (double X, double Y, double Z)[] middles = [(2.5, 3, 4), (7, 6.5, 6), (4.5, 7.5, 2.5)];
        for (var i = 0; i < count; i++)
        {
            var field = i / PerField;
            var (mx, my, mz) = middles[field];
            (x[i], y[i], z[i]) = (mx + (Gauss(random) * 1.3), my + (Gauss(random) * 1.1), mz + (Gauss(random) * 0.9));
            yields[i] = 20 + (380 * Math.Pow(random.NextDouble(), 3));
            colors[i] = _fields[field];
        }

        var bubbles = new Bubbles3D(x, y, z, Bubbles3D.ScaleToVolume(yields, 0.15, 0.9)) { Colors = colors, Values = yields };
        c.Title("Wells, volume by yield in m³ per day")
            .XAxis(0, 10, "East in km").YAxis(0, 10, "North in km").ZAxis(0, 8, "Depth in 100 m")
            .AddBubbles(bubbles);
    }

    private static double Gauss(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
