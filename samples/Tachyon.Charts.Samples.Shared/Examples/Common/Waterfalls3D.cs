using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A waterfall with a moving peak, shared by two 3D pictures.</summary>
public static class Waterfalls3D
{
    public static void Waterfall(Chart3DBuilder chart, WaterfallStyle style)
    {
        const int Columns = 512;
        const int Rows = 200;
        var data = new SpectrogramData(Columns, Rows);
        var row = new float[Columns];

        // More rows than the ring holds, so that the picture starts in the middle of the ring.
        for (var time = 0; time < Rows + 37; time++)
        {
            var center = 0.3 + (0.4 * time / (Rows + 37));
            for (var column = 0; column < Columns; column++)
            {
                var place = column / (double)(Columns - 1);
                var peak = Math.Exp(-Math.Pow((place - center) / 0.03, 2));
                var second = 0.6 * Math.Exp(-Math.Pow((place - 0.8) / 0.02, 2));
                row[column] = (float)(-80 + (70 * Math.Max(peak, second)) + (0.5 * Math.Sin((column * 0.13) + (time * 0.21))));
            }

            data.AppendRow(row);
        }

        chart.Title("Waterfall")
            .XAxis(0, 10, "Frequency in MHz")
            .YAxis(0, 20, "Age in s")
            .ZAxis(-90, 0, "Level in dBm")
            .AddWaterfall(new Waterfall3D(data, new DoubleRange(0, 10), new DoubleRange(0, 20)) { Style = style });
    }
}
