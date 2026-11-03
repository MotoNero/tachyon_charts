using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A heatmap of 4096 × 4096 cells for the examples.</summary>
public static class Heatmap4kData
{
    /// <summary>Creates the grid.</summary>
    /// <param name="withGap">Whether a part of the values is missing, to test how gaps look.</param>
    /// <returns>The grid.</returns>
    public static HeatmapData CreateData(bool withGap = true)
    {
        var values = new float[Length * Length];
        Parallel.For(0, Length, row =>
        {
            var y = row * 8.0 / Length;
            for (var column = 0; column < Length; column++)
            {
                var x = column * 8.0 / Length;
                var missing = withGap && x is > 5 and < 6.5 && y is > 1 and < 2.5;
                values[(row * Length) + column] = missing ? float.NaN : (float)(Math.Sin(x * x * 0.6) * Math.Cos(y * 2.1) + Math.Exp(-(((x - 3) * (x - 3)) + ((y - 5) * (y - 5)))));
            }
        });

        var data = new HeatmapData(Length, Length);
        data.SetValues(values);
        return data;
    }

    internal const int Length = 4096;
}
