using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>The antenna pattern that 2D and 3D examples share.</summary>
public static class Antenna3D
{
    /// <summary>Gets a pattern with one main lobe, side lobes and a known result: 15 dB at 30° and 20°, 40° wide and 24° high.</summary>
    public static AntennaPattern3D Antenna { get; } = CreateAntenna();

    internal static AntennaPattern3D CreateAntenna()
    {
        const int Columns = 361;
        const int Rows = 181;
        var (azimuths, elevations) = (new double[Columns], new double[Rows]);
        var gains = new double[Rows, Columns];
        for (var row = 0; row < Rows; row++)
        {
            elevations[row] = row - 90;
            for (var column = 0; column < Columns; column++)
            {
                azimuths[column] = column - 180;
                var (a, e) = ((((azimuths[column] - 30 + 540) % 360) - 180) / 40, (elevations[row] - 20) / 24);
                var main = 15 - (12 * ((a * a) + (e * e)));
                var side = -14 + (6 * Math.Cos(double.DegreesToRadians(elevations[row])) * Math.Cos(double.DegreesToRadians(5 * azimuths[column]))) + (4 * Math.Cos(double.DegreesToRadians(4 * elevations[row])));
                gains[row, column] = 10 * Math.Log10(Math.Pow(10, main / 10) + Math.Pow(10, side / 10));
            }
        }

        return new AntennaPattern3D(azimuths, elevations, gains);
    }
}
