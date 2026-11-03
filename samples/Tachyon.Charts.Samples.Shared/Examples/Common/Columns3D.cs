using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Columns that two 3D examples share.</summary>
public static class Columns3D
{
    /// <summary>Describes columns over a raster, with a color by value.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="side">The number of columns per side.</param>
    public static void Columns(Chart3DBuilder chart, int side)
    {
        var values = new float[side * side];
        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                var (x, y) = ((column + 0.5) / side, (row + 0.5) / side);
                values[(row * side) + column] = (float)(0.15 + (0.85 * Math.Exp(-14 * (((x - 0.35) * (x - 0.35)) + ((y - 0.6) * (y - 0.6))))) + (0.35 * Math.Exp(-30 * (((x - 0.8) * (x - 0.8)) + ((y - 0.25) * (y - 0.25))))));
            }
        }

        var data = new Tachyon.Charts.Core.Data.HeatmapData(side, side);
        data.SetValues(values);
        chart.Title("Columns over a raster")
            .XAxis(0, 16, "x").YAxis(0, 16, "y").ZAxis(0, 1.2, "Value")
            .AddBars(new Bars3D(data, new DoubleRange(0, 16), new DoubleRange(0, 16)) { Material = new Material3D { Gloss = 0.4 } });
    }
}
