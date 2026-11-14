using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// The data of the 3D pages of the showroom as sample data: the same formulas, counts and colors. Some give the finished
/// points or bubbles with a color each, which the types then take as they are.
/// </summary>
internal static class ShowroomSamples3D
{
    private const string Points = "(double[] X, double[] Y, double[] Z)";
    private const string Pattern = "(double[] Azimuths, double[] Elevations, double[,] Gains)";

    // The samples whose data are the finished points or bubbles.
    private static readonly HashSet<string> _ready = ["threeClouds", "particlePath", "wells", "spiralBubbles"];

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        new("threeClouds", "Three clouds, colored by height", DataShape.Points3D, 3000, ThreeClouds, "Points3D", method => SampleCatalog.Header("three clouds, colored by height", method, "Points3D", "The number of points; above 100 000 they are small.") + """
                {
                    var points = new Points3D(count);
                    var random = new Random(5);
                    double[,] centers = { { -0.45, -0.3, -0.3 }, { 0.4, 0.35, 0.1 }, { -0.1, 0.45, 0.5 } };
                    for (var i = 0; i < count; i++)
                    {
                        var cloud = i % 3;
                        var (x, y, z) = (centers[cloud, 0] + Spread(random), centers[cloud, 1] + Spread(random), centers[cloud, 2] + Spread(random));
                        points.Append(x, y, z, Colormaps.Viridis.Sample((z + 1) / 2), count > 100_000 ? 1.5 : 3 + (8 * random.NextDouble()));
                    }

                    return points;
                }

                private static double Spread(Random random) => 0.24 * (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5);

            """) { MinimumCount = 3 },
        new("particlePath", "Path of a particle", DataShape.Points3D, 200_000, ParticlePath, "Points3D", method => SampleCatalog.Header("the path of a particle, colored along its way", method, "Points3D", "The number of steps.") + """
                {
                    var points = new Points3D(count);
                    var (x, y, z) = (1.0, 1.0, 20.0);
                    for (var i = 0; i < count; i++)
                    {
                        (x, y, z) = (x + (0.02 * (y - x)), y + (0.002 * ((x * (28 - z)) - y)), z + (0.002 * ((x * y) - (8.0 / 3 * z))));
                        points.Append(x, y, z, Colormaps.Viridis.Sample(i / (double)count));
                    }

                    return points;
                }

            """) { MinimumCount = 2 },
        new("wells", "Wells of three fields, volume by yield", DataShape.Bubbles3D, 180, _ => Wells(), "Bubbles3D", method => SampleCatalog.Header("wells of three fields, the volume by the yield", method, "Bubbles3D", "Not used: 60 wells per field.") + """
                {
                    Rgba8[] fields = [new(255, 190, 11, 255), new(58, 134, 255, 255), new(6, 214, 160, 255)];
                    (double X, double Y, double Z)[] middles = [(2.5, 3, 4), (7, 6.5, 6), (4.5, 7.5, 2.5)];
                    var random = new Random(11);
                    var (x, y, z, yields, colors) = (new double[180], new double[180], new double[180], new double[180], new Rgba8[180]);
                    for (var i = 0; i < 180; i++)
                    {
                        var field = i / 60;
                        var (mx, my, mz) = middles[field];
                        (x[i], y[i], z[i]) = (mx + (Gauss(random) * 1.3), my + (Gauss(random) * 1.1), mz + (Gauss(random) * 0.9));
                        yields[i] = 20 + (380 * Math.Pow(random.NextDouble(), 3));
                        colors[i] = fields[field];
                    }

                    return new Bubbles3D(x, y, z, Bubbles3D.ScaleToVolume(yields, 0.15, 0.9)) { Colors = colors, Values = yields };
                }

                private static double Gauss(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

            """) { HasCount = false },
        new("spiralBubbles", "Two arms of a spiral", DataShape.Bubbles3D, 1_000_000, SpiralBubbles, "Bubbles3D", method => SampleCatalog.Header("two arms of a spiral, thicker near the middle", method, "Bubbles3D", "The number of bubbles.") + """
                {
                    var random = new Random(5);
                    var (x, y, z, sizes, colors) = (new double[count], new double[count], new double[count], new double[count], new Rgba8[count]);
                    for (var i = 0; i < count; i++)
                    {
                        var reach = Math.Pow(random.NextDouble(), 0.7);
                        var angle = (reach * 7) + (random.Next(2) * Math.PI) + (Gauss(random) * 0.12);
                        var spread = 0.025 + (0.06 * (1 - reach));
                        x[i] = (reach * Math.Cos(angle)) + (Gauss(random) * spread);
                        y[i] = (reach * Math.Sin(angle)) + (Gauss(random) * spread);
                        z[i] = Gauss(random) * spread * 0.8;
                        sizes[i] = 2 + (4 * Math.Pow(random.NextDouble(), 6));
                        colors[i] = Colormaps.Plasma.Sample(1 - (reach * 0.85));
                    }

                    return new Bubbles3D(x, y, z, sizes) { Colors = colors, Values = sizes };
                }

                private static double Gauss(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

            """) { MinimumCount = 1 },
        new("rasterHills", "Two hills over a raster", DataShape.Grid, 16, RasterHills, "HeatmapData", method => SampleCatalog.Header("two hills over a raster", method, "HeatmapData", "The number of cells per side.") + """
                {
                    var values = new float[count * count];
                    for (var row = 0; row < count; row++)
                    {
                        for (var column = 0; column < count; column++)
                        {
                            var (x, y) = ((column + 0.5) / count, (row + 0.5) / count);
                            values[(row * count) + column] = (float)(0.15 + (0.85 * Math.Exp(-14 * (((x - 0.35) * (x - 0.35)) + ((y - 0.6) * (y - 0.6))))) + (0.35 * Math.Exp(-30 * (((x - 0.8) * (x - 0.8)) + ((y - 0.25) * (y - 0.25))))));
                        }
                    }

                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    return data;
                }

            """) { MinimumCount = 2 },
        new("mainLobe", "A main lobe with side lobes", DataShape.Pattern, 361, _ => MainLobe(), Pattern, method => SampleCatalog.Header("a main lobe at 30° and 20° with side lobes", method, Pattern, "Not used: one value per degree.") + """
                {
                    var (azimuths, elevations, gains) = (new double[361], new double[181], new double[181, 361]);
                    for (var row = 0; row < 181; row++)
                    {
                        elevations[row] = row - 90;
                        for (var column = 0; column < 361; column++)
                        {
                            azimuths[column] = column - 180;
                            var (a, e) = ((((azimuths[column] - 30 + 540) % 360) - 180) / 40, (elevations[row] - 20) / 24);
                            var main = 15 - (12 * ((a * a) + (e * e)));
                            var side = -14 + (6 * Math.Cos(double.DegreesToRadians(elevations[row])) * Math.Cos(double.DegreesToRadians(5 * azimuths[column]))) + (4 * Math.Cos(double.DegreesToRadians(4 * elevations[row])));
                            gains[row, column] = 10 * Math.Log10(Math.Pow(10, main / 10) + Math.Pow(10, side / 10));
                        }
                    }

                    return (azimuths, elevations, gains);
                }

            """) { HasCount = false },
        new("engineMap", "Temperature over speed and load", DataShape.Points3D, 160, count => EngineMap(count), Points, method => SampleCatalog.Header("the temperature over speed and load", method, Points, "The number of measurements.") + """
                {
                    var random = new Random(4);
                    var (speed, load, temperature) = (new double[count], new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        speed[i] = 800 + (5200 * random.NextDouble());
                        load[i] = 10 + (90 * random.NextDouble() * random.NextDouble());
                        var (a, b) = (speed[i] / 6000, load[i] / 100);
                        temperature[i] = 70 + (40 * a * b) + (15 * Math.Sin(a * 5) * b);
                    }

                    return (speed, load, temperature);
                }

            """) { MinimumCount = 3 },
    ];

    // The box each finished set of points fills, as the page shows it.
    private static readonly Dictionary<string, (DoubleRange X, DoubleRange Y, DoubleRange Z)> _boxes = new(StringComparer.Ordinal)
    {
        ["threeClouds"] = (new(-1, 1), new(-1, 1), new(-1, 1)),
        ["particlePath"] = (new(-25, 25), new(-30, 30), new(0, 55)),
        ["wells"] = (new(0, 10), new(0, 10), new(0, 8)),
        ["spiralBubbles"] = (new(-1.2, 1.2), new(-1.2, 1.2), new(-1.2, 1.2)),
        ["pipeSensors"] = (new(-1.25, 126.25), new(-63.75, 63.75), new(-26.25, 101.25)),
        ["spiralPath"] = (new(-1, 1), new(-1, 1), new(-1, 1)),
        ["greenSpiral"] = (new(-1, 1), new(-1, 1), new(-1, 1)),
        ["landscape"] = (new(-1, 1), new(-1, 1), new(-1, 1)),
    };

    /// <summary>Gets the box the finished points or bubbles of a series fill, or <see langword="null"/> for other data.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The ranges of the three axes.</returns>
    public static (DoubleRange X, DoubleRange Y, DoubleRange Z)? BoxOf(SeriesSpec series) =>
        IsReady(series) && _boxes.TryGetValue(series.Data.Sample, out var box) ? box : null;

    /// <summary>Gets a value indicating whether the data of a series are the finished points or bubbles.</summary>
    /// <param name="series">The series.</param>
    /// <returns>Whether its sample makes the finished object.</returns>
    public static bool IsReady(SeriesSpec series) => series.Data.Kind == DataKind.Sample && (_ready.Contains(series.Data.Sample) || ShowroomModels3D.Ready.Contains(series.Data.Sample) || ShowroomVolumes3D.Ready.Contains(series.Data.Sample));

    private static Points3D ThreeClouds(int count)
    {
        var points = new Points3D(count);
        var random = new Random(5); // NOSONAR: makes sample data, no security
        double[,] centers = { { -0.45, -0.3, -0.3 }, { 0.4, 0.35, 0.1 }, { -0.1, 0.45, 0.5 } };
        for (var i = 0; i < count; i++)
        {
            var cloud = i % 3;
            var (x, y, z) = (centers[cloud, 0] + Spread(random), centers[cloud, 1] + Spread(random), centers[cloud, 2] + Spread(random));
            points.Append(x, y, z, Colormaps.Viridis.Sample((z + 1) / 2), count > 100_000 ? 1.5 : 3 + (8 * random.NextDouble()));
        }

        return points;
    }

    private static double Spread(Random random) => 0.24 * (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5);

    private static Points3D ParticlePath(int count)
    {
        var points = new Points3D(count);
        var (x, y, z) = (1.0, 1.0, 20.0);
        for (var i = 0; i < count; i++)
        {
            (x, y, z) = (x + (0.02 * (y - x)), y + (0.002 * ((x * (28 - z)) - y)), z + (0.002 * ((x * y) - (8.0 / 3 * z))));
            points.Append(x, y, z, Colormaps.Viridis.Sample(i / (double)count));
        }

        return points;
    }

    private static Bubbles3D Wells()
    {
        Rgba8[] fields = [new(255, 190, 11, 255), new(58, 134, 255, 255), new(6, 214, 160, 255)];
        (double X, double Y, double Z)[] middles = [(2.5, 3, 4), (7, 6.5, 6), (4.5, 7.5, 2.5)];
        var random = new Random(11); // NOSONAR: makes sample data, no security
        var (x, y, z, yields, colors) = (new double[180], new double[180], new double[180], new double[180], new Rgba8[180]);
        for (var i = 0; i < 180; i++)
        {
            var field = i / 60;
            var (mx, my, mz) = middles[field];
            (x[i], y[i], z[i]) = (mx + (Gauss(random) * 1.3), my + (Gauss(random) * 1.1), mz + (Gauss(random) * 0.9));
            yields[i] = 20 + (380 * Math.Pow(random.NextDouble(), 3));
            colors[i] = fields[field];
        }

        return new Bubbles3D(x, y, z, Bubbles3D.ScaleToVolume(yields, 0.15, 0.9)) { Colors = colors, Values = yields };
    }

    private static Bubbles3D SpiralBubbles(int count)
    {
        var random = new Random(5); // NOSONAR: makes sample data, no security
        var (x, y, z, sizes, colors) = (new double[count], new double[count], new double[count], new double[count], new Rgba8[count]);
        for (var i = 0; i < count; i++)
        {
            var reach = Math.Pow(random.NextDouble(), 0.7);
            var angle = (reach * 7) + (random.Next(2) * Math.PI) + (Gauss(random) * 0.12);
            var spread = 0.025 + (0.06 * (1 - reach));
            x[i] = (reach * Math.Cos(angle)) + (Gauss(random) * spread);
            y[i] = (reach * Math.Sin(angle)) + (Gauss(random) * spread);
            z[i] = Gauss(random) * spread * 0.8;
            sizes[i] = 2 + (4 * Math.Pow(random.NextDouble(), 6));
            colors[i] = Colormaps.Plasma.Sample(1 - (reach * 0.85));
        }

        return new Bubbles3D(x, y, z, sizes) { Colors = colors, Values = sizes };
    }

    private static double Gauss(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

    private static HeatmapData RasterHills(int count)
    {
        var values = new float[count * count];
        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < count; column++)
            {
                var (x, y) = ((column + 0.5) / count, (row + 0.5) / count);
                values[(row * count) + column] = (float)(0.15 + (0.85 * Math.Exp(-14 * (((x - 0.35) * (x - 0.35)) + ((y - 0.6) * (y - 0.6))))) + (0.35 * Math.Exp(-30 * (((x - 0.8) * (x - 0.8)) + ((y - 0.25) * (y - 0.25))))));
            }
        }

        var data = new HeatmapData(count, count);
        data.SetValues(values);
        return data;
    }

    private static (double[] Azimuths, double[] Elevations, double[,] Gains) MainLobe()
    {
        var (azimuths, elevations, gains) = (new double[361], new double[181], new double[181, 361]);
        for (var row = 0; row < 181; row++)
        {
            elevations[row] = row - 90;
            for (var column = 0; column < 361; column++)
            {
                azimuths[column] = column - 180;
                var (a, e) = ((((azimuths[column] - 30 + 540) % 360) - 180) / 40, (elevations[row] - 20) / 24);
                var main = 15 - (12 * ((a * a) + (e * e)));
                var side = -14 + (6 * Math.Cos(double.DegreesToRadians(elevations[row])) * Math.Cos(double.DegreesToRadians(5 * azimuths[column]))) + (4 * Math.Cos(double.DegreesToRadians(4 * elevations[row])));
                gains[row, column] = 10 * Math.Log10(Math.Pow(10, main / 10) + Math.Pow(10, side / 10));
            }
        }

        return (azimuths, elevations, gains);
    }

    private static (double[] X, double[] Y, double[] Z) EngineMap(int count)
    {
        var random = new Random(4); // NOSONAR: makes sample data, no security
        var (speed, load, temperature) = (new double[count], new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            speed[i] = 800 + (5200 * random.NextDouble());
            load[i] = 10 + (90 * random.NextDouble() * random.NextDouble());
            var (a, b) = (speed[i] / 6000, load[i] / 100);
            temperature[i] = 70 + (40 * a * b) + (15 * Math.Sin(a * 5) * b);
        }

        return (speed, load, temperature);
    }
}
