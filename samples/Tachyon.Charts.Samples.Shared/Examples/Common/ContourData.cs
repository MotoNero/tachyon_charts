using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A height field for the contour examples.</summary>
public static class ContourData
{
    /// <summary>Creates the field.</summary>
    /// <param name="length">The number of samples per side.</param>
    /// <param name="withGap">Whether a part of the values is missing, to test how gaps look.</param>
    /// <returns>The grid.</returns>
    public static HeatmapData CreateField(int length, bool withGap = true)
    {
        var values = new float[length * length];
        for (var row = 0; row < length; row++)
        {
            var y = row * 6.0 / length;
            for (var column = 0; column < length; column++)
            {
                var x = column * 6.0 / length;
                var missing = withGap && x > 4.8 && y > 4.8;
                values[(row * length) + column] = missing ? float.NaN : (float)(Math.Sin(x) * Math.Cos(y * 1.3) + (0.6 * Math.Exp(-(((x - 2) * (x - 2)) + ((y - 4) * (y - 4))))));
            }
        }

        var data = new HeatmapData(length, length);
        data.SetValues(values);
        return data;
    }
}
