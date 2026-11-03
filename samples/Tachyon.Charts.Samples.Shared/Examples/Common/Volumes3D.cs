using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>The body inside a volume and its transfer function, shared by the volume examples.</summary>
public static class Volumes3D
{
    /// <summary>Creates a body with a shell and bright spots inside, as a scanner would give it, in 16 bits.</summary>
    /// <param name="side">The number of values per side.</param>
    /// <returns>The values.</returns>
    public static VolumeData CreateBody(int side)
    {
        var values = new ushort[side * side * side];
        Parallel.For(0, side, z =>
        {
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var (a, b, c) = ((2.0 * x / (side - 1)) - 1, (2.0 * y / (side - 1)) - 1, (2.0 * z / (side - 1)) - 1);
                    values[(z * side * side) + (y * side) + x] = (ushort)(BodyAt(a, b, c) * 60_000);
                }
            }
        });
        return new VolumeData(values, side, side, side);
    }

    internal static double BodyAt(double a, double b, double c)
    {
        var radius = Math.Sqrt((a * a) + (b * b * 1.5) + (c * c * 1.2));
        var shell = Math.Exp(-Math.Pow((radius - 0.8) / 0.05, 2));
        var inside = radius < 0.8 ? 0.25 + (0.08 * Math.Sin(a * 9) * Math.Sin(b * 8) * Math.Sin(c * 7)) : 0;
        var spot = Math.Exp(-(((a - 0.3) * (a - 0.3)) + ((b + 0.2) * (b + 0.2)) + ((c - 0.1) * (c - 0.1))) / 0.02);
        var second = 0.95 * Math.Exp(-(((a + 0.35) * (a + 0.35)) + ((b - 0.15) * (b - 0.15)) + ((c + 0.25) * (c + 0.25))) / 0.01);
        return Math.Clamp(Math.Max(Math.Max(shell * 0.75, inside), Math.Max(spot, second)), 0, 1);
    }

    /// <summary>Gets a function that hides the air, tints the tissue and shows the shell and bright spots.</summary>
    /// <returns>The function.</returns>
    public static TransferFunction3D CreateTransfer()
    {
        var clear = new Tachyon.Charts.Rendering.Rgba8(30, 60, 140, 255);
        var tissue = new Tachyon.Charts.Rendering.Rgba8(70, 150, 190, 255);
        var shell = new Tachyon.Charts.Rendering.Rgba8(240, 190, 120, 255);
        var spot = new Tachyon.Charts.Rendering.Rgba8(255, 90, 60, 255);
        return new TransferFunction3D(
            new TransferPoint3D(0, clear, 0),
            new TransferPoint3D(0.12, clear, 0),
            new TransferPoint3D(0.3, tissue, 0.003),
            new TransferPoint3D(0.6, tissue, 0.006),
            new TransferPoint3D(0.76, shell, 0.03),
            new TransferPoint3D(0.86, spot, 0.5),
            new TransferPoint3D(1, spot, 0.9));
    }

    /// <summary>Describes a look through a volume.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="side">The number of values per side.</param>
    public static void LookThrough(Chart3DBuilder chart, int side)
    {
        var volume = new Volume3D(CreateBody(side), new DoubleRange(-1, 1), new DoubleRange(-1, 1), new DoubleRange(-1, 1)) { Transfer = CreateTransfer(), ShowsSlices = false };
        chart.Title("Through a body").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddVolume(volume);
    }
}
