using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Surfaces and the hill that several 3D examples share.</summary>
public static class Surfaces3D
{
    /// <summary>Describes a surface from a raster.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="withGap">Whether a part of the values is missing.</param>
    /// <param name="side">The number of values per side.</param>
    /// <param name="contours">The height lines.</param>
    public static void Field(Chart3DBuilder chart, bool withGap, int side, Contours3D? contours = null) => chart
        .Title("Surface")
        .XAxis(0, 6, "x")
        .YAxis(0, 6, "y")
        .ZAxis(-1.5, 2, "z")
        .AddSurface(new Surface3D(ContourScene.CreateField(side, withGap), new DoubleRange(0, 6), new DoubleRange(0, 6)) { Contours = contours });

    /// <summary>Describes a gentle hill.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="material">The material.</param>
    public static void Hill(Chart3DBuilder chart, Material3D material) => chart
        .Title("Surface")
        .XAxis(-1, 1, "x")
        .YAxis(-1, 1, "y")
        .ZAxis(-1, 1, "z")
        .AddMesh(CreateSurface((x, y) => 0.7 * Math.Sin(x * 3) * Math.Cos(y * 2.5), Tint, material));

    /// <summary>Creates a surface over a square.</summary>
    /// <param name="height">The height over two places from -1 to 1.</param>
    /// <param name="color">The color for a height.</param>
    /// <param name="material">The material.</param>
    /// <returns>The surface.</returns>
    public static Mesh3D CreateSurface(Func<double, double, double> height, Func<double, Rgba8> color, Material3D material)
    {
        const int Side = 96;
        var vertices = new Vertex3D[Side * Side];
        var indices = new List<int>(Side * Side * 6);
        for (var row = 0; row < Side; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                var (x, y) = ((column * 2.0 / (Side - 1)) - 1, (row * 2.0 / (Side - 1)) - 1);
                var z = height(x, y);
                vertices[(row * Side) + column] = new Vertex3D(new Point3D(x, y, z), color(z));
                if (row > 0 && column > 0)
                {
                    var corner = (row * Side) + column;
                    indices.AddRange([corner - Side - 1, corner - Side, corner, corner - Side - 1, corner, corner - 1]);
                }
            }
        }

        return new Mesh3D(vertices, indices) { Material = material };
    }

    internal static Rgba8 Tint(double height)
    {
        var share = (height + 1) / 2;
        return new Rgba8((byte)(40 + (215 * share)), (byte)(110 + (60 * share)), (byte)(230 - (170 * share)), 255);
    }

    internal static Surface3D CreateHillSurface()
    {
        const int Side = 256;
        var values = new float[Side * Side];
        for (var i = 0; i < values.Length; i++)
        {
            var (x, y) = ((2.0 * (i % Side) / (Side - 1)) - 1, (2.0 * (i / Side) / (Side - 1)) - 1);
            values[i] = (float)(0.7 * Math.Sin(x * 3) * Math.Cos(y * 2.5));
        }

        var data = new Tachyon.Charts.Core.Data.HeatmapData(Side, Side);
        data.SetValues(values);
        return new Surface3D(data, new DoubleRange(-1, 1), new DoubleRange(-1, 1));
    }
}
