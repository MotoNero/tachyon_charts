using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „1 million bubbles“ of the showroom: a spiral of a million lit spheres whose size stays the same on the screen.</summary>
public static class MillionBubblesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "1 million bubbles";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        const int Count = 1_000_000;
        var random = new Random(5);
        var (x, y, z, sizes) = (new double[Count], new double[Count], new double[Count], new double[Count]);
        var colors = new Rgba8[Count];
        for (var i = 0; i < Count; i++)
        {
            // Two arms that wind outwards, thicker near the middle.
            var reach = Math.Pow(random.NextDouble(), 0.7);
            var angle = (reach * 7) + (random.Next(2) * Math.PI) + (Gauss(random) * 0.12);
            var spread = 0.025 + (0.06 * (1 - reach));
            x[i] = (reach * Math.Cos(angle)) + (Gauss(random) * spread);
            y[i] = (reach * Math.Sin(angle)) + (Gauss(random) * spread);
            z[i] = Gauss(random) * spread * 0.8;
            sizes[i] = 2 + (4 * Math.Pow(random.NextDouble(), 6));
            colors[i] = Colormaps.Plasma.Sample(1 - (reach * 0.85));
        }

        var bubbles = new Bubbles3D(x, y, z, sizes) { SizeUnit = BubbleSizeUnit.Screen, Colors = colors, Values = sizes };
        c.Title("A million bubbles, size in screen points")
            .XAxis(-1.2, 1.2, "x").YAxis(-1.2, 1.2, "y").ZAxis(-1.2, 1.2, "z")
            .AddBubbles(bubbles);
    }

    private static double Gauss(Random random) =>
        Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
