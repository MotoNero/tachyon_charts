using Tachyon.Charts.Core.Data;
using Tachyon.Charts.ThreeD.Files;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>The sample data of the 3D types. Each keeps its formula twice, as C# for the preview and as text for the code, side by side.</summary>
internal static class Samples3D
{
    private const string Points = "(double[] X, double[] Y, double[] Z)";
    private const string Balls = "(double[] X, double[] Y, double[] Z, double[] Diameters)";

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Grid("hill", "Hill and valley", 64, (x, y) => 0.7 * Math.Sin(x * 0.9) * Math.Cos(y * 0.7), "0.7 * Math.Sin(x * 0.9) * Math.Cos(y * 0.7)"),
        Grid("blocks", "Blocks", 12, (x, y) => 0.5 + (0.4 * Math.Sin(x * 0.7) * Math.Cos(y * 0.5)), "0.5 + (0.4 * Math.Sin(x * 0.7) * Math.Cos(y * 0.5))"),
        Helix(),
        Scattered("scattered", "Scattered measurements", 400, 0),
        Scattered("dust", "Cloud around a surface", 100_000, 0.15),
        Bubbles(),
        Blobs(),
        Dipole(),
        Torus(),
    ];

    // A ring of triangles around the middle of the box: the count of segments around the ring, a quarter of them around the tube.
    private static SampleInfo Torus() => new(
        "torus",
        "Torus",
        DataShape.Model,
        64,
        count =>
        {
            var (around, tube) = (count, Math.Max(3, count / 4));
            var positions = new Point3D[around * tube];
            var triangles = new int[around * tube * 6];
            for (var i = 0; i < around; i++)
            {
                for (var j = 0; j < tube; j++)
                {
                    var (u, v) = (i * 2 * Math.PI / around, j * 2 * Math.PI / tube);
                    positions[(i * tube) + j] = new Point3D(5 + ((3 + Math.Cos(v)) * Math.Cos(u)), 5 + ((3 + Math.Cos(v)) * Math.Sin(u)), 5 + Math.Sin(v));
                    var (a, b, c, d) = ((i * tube) + j, (((i + 1) % around) * tube) + j, (((i + 1) % around) * tube) + ((j + 1) % tube), (i * tube) + ((j + 1) % tube));
                    int[] corners = [a, b, c, a, c, d];
                    corners.CopyTo(triangles, ((i * tube) + j) * 6);
                }
            }

            return new ModelFileData(positions, triangles, null);
        },
        "ModelFileData",
        method => SampleCatalog.Header("a torus", method, "ModelFileData", "The number of segments around the ring.") + """
                {
                    var (around, tube) = (count, Math.Max(3, count / 4));
                    var positions = new Point3D[around * tube];
                    var triangles = new int[around * tube * 6];
                    for (var i = 0; i < around; i++)
                    {
                        for (var j = 0; j < tube; j++)
                        {
                            var (u, v) = (i * 2 * Math.PI / around, j * 2 * Math.PI / tube);
                            positions[(i * tube) + j] = new Point3D(5 + ((3 + Math.Cos(v)) * Math.Cos(u)), 5 + ((3 + Math.Cos(v)) * Math.Sin(u)), 5 + Math.Sin(v));
                            var (a, b, c, d) = ((i * tube) + j, (((i + 1) % around) * tube) + j, (((i + 1) % around) * tube) + ((j + 1) % tube), (i * tube) + ((j + 1) % tube));
                            int[] corners = [a, b, c, a, c, d];
                            corners.CopyTo(triangles, ((i * tube) + j) * 6);
                        }
                    }

                    return new ModelFileData(positions, triangles, null);
                }

            """)
    { MinimumCount = 3 };

    private static SampleInfo Grid(string id, string name, int side, Func<double, double, double> height, string formula) => new(
        id,
        name,
        DataShape.Grid,
        side,
        count =>
        {
            var values = new float[count * count];
            for (var row = 0; row < count; row++)
            {
                for (var column = 0; column < count; column++)
                {
                    values[(row * count) + column] = (float)height(column * 10.0 / (count - 1), row * 10.0 / (count - 1));
                }
            }

            var data = new HeatmapData(count, count);
            data.SetValues(values);
            return data;
        },
        "HeatmapData",
        method => SampleCatalog.Header(name.ToLowerInvariant(), method, "HeatmapData", "The columns and rows of the grid.") + $$"""
                {
                    var values = new float[count * count];
                    for (var row = 0; row < count; row++)
                    {
                        for (var column = 0; column < count; column++)
                        {
                            var (x, y) = (column * 10.0 / (count - 1), row * 10.0 / (count - 1));
                            values[(row * count) + column] = (float)({{formula}});
                        }
                    }

                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    return data;
                }

            """);

    private static SampleInfo Helix() => new(
        "helix",
        "Helix",
        DataShape.Points3D,
        600,
        count =>
        {
            var (x, y, z) = (new double[count], new double[count], new double[count]);
            for (var i = 0; i < count; i++)
            {
                var t = i * 10.0 / (count - 1);
                (x[i], y[i], z[i]) = (5 + (4 * Math.Cos(2 * t)), 5 + (4 * Math.Sin(2 * t)), (t / 5) - 1);
            }

            return (x, y, z);
        },
        Points,
        method => SampleCatalog.Header("a helix", method, Points) + """
                {
                    var (x, y, z) = (new double[count], new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        var t = i * 10.0 / (count - 1);
                        (x[i], y[i], z[i]) = (5 + (4 * Math.Cos(2 * t)), 5 + (4 * Math.Sin(2 * t)), (t / 5) - 1);
                    }

                    return (x, y, z);
                }

            """);

    // Points spread over the box like measurements, the same on every run, with the height of the hill and some noise.
    private static SampleInfo Scattered(string id, string name, int count, double noise) => new(
        id,
        name,
        DataShape.Points3D,
        count,
        n =>
        {
            var (x, y, z) = (new double[n], new double[n], new double[n]);
            for (var i = 0; i < n; i++)
            {
                (x[i], y[i]) = (5 + (4.5 * Math.Sin(i * 1.37)), 5 + (4.5 * Math.Sin((i * 0.731) + 1)));
                z[i] = (0.7 * Math.Sin(x[i] * 0.9) * Math.Cos(y[i] * 0.7)) + (noise * Math.Sin(i * 2.311));
            }

            return (x, y, z);
        },
        Points,
        method => SampleCatalog.Header(name.ToLowerInvariant(), method, Points) + $$"""
                {
                    var (x, y, z) = (new double[count], new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        (x[i], y[i]) = (5 + (4.5 * Math.Sin(i * 1.37)), 5 + (4.5 * Math.Sin((i * 0.731) + 1)));
                        z[i] = (0.7 * Math.Sin(x[i] * 0.9) * Math.Cos(y[i] * 0.7)) + ({{CodeText.Number(noise)}} * Math.Sin(i * 2.311));
                    }

                    return (x, y, z);
                }

            """);

    private static SampleInfo Bubbles() => new(
        "balls",
        "Bubbles over a hill",
        DataShape.Bubbles3D,
        40,
        count =>
        {
            var (x, y, z, d) = (new double[count], new double[count], new double[count], new double[count]);
            for (var i = 0; i < count; i++)
            {
                (x[i], y[i]) = (5 + (4.5 * Math.Sin(i * 1.37)), 5 + (4.5 * Math.Sin((i * 0.731) + 1)));
                z[i] = 0.7 * Math.Sin(x[i] * 0.9) * Math.Cos(y[i] * 0.7);
                d[i] = 0.4 + (0.6 * (0.5 + (0.5 * Math.Sin(i * 1.3))));
            }

            return (x, y, z, d);
        },
        Balls,
        method => SampleCatalog.Header("bubbles over a hill", method, Balls) + """
                {
                    var (x, y, z, d) = (new double[count], new double[count], new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        (x[i], y[i]) = (5 + (4.5 * Math.Sin(i * 1.37)), 5 + (4.5 * Math.Sin((i * 0.731) + 1)));
                        z[i] = 0.7 * Math.Sin(x[i] * 0.9) * Math.Cos(y[i] * 0.7);
                        d[i] = 0.4 + (0.6 * (0.5 + (0.5 * Math.Sin(i * 1.3))));
                    }

                    return (x, y, z, d);
                }

            """);

    // Two blobs of different size in a cube of voxels.
    private static SampleInfo Blobs() => new(
        "blobs",
        "Two blobs",
        DataShape.Volume,
        48,
        count =>
        {
            var values = new float[count * count * count];
            for (var k = 0; k < count; k++)
            {
                for (var j = 0; j < count; j++)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var (u, v, w) = (i / (count - 1.0), j / (count - 1.0), k / (count - 1.0));
                        values[(((k * count) + j) * count) + i] = (float)(Math.Exp(-(((u - 0.4) * (u - 0.4)) + ((v - 0.5) * (v - 0.5)) + ((w - 0.5) * (w - 0.5))) / 0.02)
                            + (0.7 * Math.Exp(-(((u - 0.68) * (u - 0.68)) + ((v - 0.4) * (v - 0.4)) + ((w - 0.6) * (w - 0.6))) / 0.01)));
                    }
                }
            }

            return new VolumeData(values, count, count, count);
        },
        "VolumeData",
        method => SampleCatalog.Header("two blobs", method, "VolumeData", "The voxels along each edge.") + """
                {
                    var values = new float[count * count * count];
                    for (var k = 0; k < count; k++)
                    {
                        for (var j = 0; j < count; j++)
                        {
                            for (var i = 0; i < count; i++)
                            {
                                var (u, v, w) = (i / (count - 1.0), j / (count - 1.0), k / (count - 1.0));
                                values[(((k * count) + j) * count) + i] = (float)(Math.Exp(-(((u - 0.4) * (u - 0.4)) + ((v - 0.5) * (v - 0.5)) + ((w - 0.5) * (w - 0.5))) / 0.02)
                                    + (0.7 * Math.Exp(-(((u - 0.68) * (u - 0.68)) + ((v - 0.4) * (v - 0.4)) + ((w - 0.6) * (w - 0.6))) / 0.01)));
                            }
                        }
                    }

                    return new VolumeData(values, count, count, count);
                }

            """)
    { MinimumCount = 2 };

    // A dipole with a little ripple: gains in dB over 37 elevations and the given number of azimuths.
    private static SampleInfo Dipole()
    {
        const string Type = "(double[] Azimuths, double[] Elevations, double[,] Gains)";
        return new(
            "dipole",
            "Dipole",
            DataShape.Pattern,
            73,
            count =>
            {
                var (azimuths, elevations, gains) = (new double[count], new double[37], new double[37, count]);
                for (var a = 0; a < count; a++)
                {
                    azimuths[a] = a * 360.0 / (count - 1);
                }

                for (var e = 0; e < 37; e++)
                {
                    elevations[e] = (e * 5.0) - 90;
                    for (var a = 0; a < count; a++)
                    {
                        var (elevation, azimuth) = (elevations[e] * Math.PI / 180, azimuths[a] * Math.PI / 180);
                        var power = (Math.Cos(elevation) * Math.Cos(elevation) * (0.15 + (0.85 * Math.Cos(azimuth) * Math.Cos(azimuth)))) + 1e-4;
                        gains[e, a] = (10 * Math.Log10(power)) + (0.5 * Math.Sin(4 * azimuth));
                    }
                }

                return (azimuths, elevations, gains);
            },
            Type,
            method => SampleCatalog.Header("a dipole", method, Type, "The number of azimuths.") + """
                    {
                        var (azimuths, elevations, gains) = (new double[count], new double[37], new double[37, count]);
                        for (var a = 0; a < count; a++)
                        {
                            azimuths[a] = a * 360.0 / (count - 1);
                        }

                        for (var e = 0; e < 37; e++)
                        {
                            elevations[e] = (e * 5.0) - 90;
                            for (var a = 0; a < count; a++)
                            {
                                var (elevation, azimuth) = (elevations[e] * Math.PI / 180, azimuths[a] * Math.PI / 180);
                                var power = (Math.Cos(elevation) * Math.Cos(elevation) * (0.15 + (0.85 * Math.Cos(azimuth) * Math.Cos(azimuth)))) + 1e-4;
                                gains[e, a] = (10 * Math.Log10(power)) + (0.5 * Math.Sin(4 * azimuth));
                            }
                        }

                        return (azimuths, elevations, gains);
                    }

                """)
        { MinimumCount = 3 };
    }
}
