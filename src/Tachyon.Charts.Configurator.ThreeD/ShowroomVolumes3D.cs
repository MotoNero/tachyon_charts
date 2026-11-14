using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Files;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// The volumes, clouds and objects of the 3D pages of the showroom as sample data: a body with a shell and two spots, a
/// green spiral and a saddle inside it, and a scanned landscape that loads when it is first drawn.
/// </summary>
internal static class ShowroomVolumes3D
{
    private const string Loading = "Func<IProgress<double>, PointCloudArrays>";

    /// <summary>Gets the samples whose data are the finished points or the loading of a cloud.</summary>
    public static IReadOnlyCollection<string> Ready { get; } = ["greenSpiral", "landscape"];

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        new("layeredBody", "A body with a shell and two spots", DataShape.Volume, 128, Body, "VolumeData", method => SampleCatalog.Header("a body with a shell, a textured inside and two spots", method, "VolumeData", "The number of values per side.") + """
                {
                    var values = new ushort[count * count * count];
                    Parallel.For(0, count, z =>
                    {
                        for (var y = 0; y < count; y++)
                        {
                            for (var x = 0; x < count; x++)
                            {
                                var (a, b, c) = ((2.0 * x / (count - 1)) - 1, (2.0 * y / (count - 1)) - 1, (2.0 * z / (count - 1)) - 1);
                                var radius = Math.Sqrt((a * a) + (b * b * 1.5) + (c * c * 1.2));
                                var shell = Math.Exp(-Math.Pow((radius - 0.8) / 0.05, 2));
                                var inside = radius < 0.8 ? 0.25 + (0.08 * Math.Sin(a * 9) * Math.Sin(b * 8) * Math.Sin(c * 7)) : 0;
                                var spot = Math.Exp(-(((a - 0.3) * (a - 0.3)) + ((b + 0.2) * (b + 0.2)) + ((c - 0.1) * (c - 0.1))) / 0.02);
                                var second = 0.95 * Math.Exp(-(((a + 0.35) * (a + 0.35)) + ((b - 0.15) * (b - 0.15)) + ((c + 0.25) * (c + 0.25))) / 0.01);
                                values[(z * count * count) + (y * count) + x] = (ushort)(Math.Clamp(Math.Max(Math.Max(shell * 0.75, inside), Math.Max(spot, second)), 0, 1) * 60_000);
                            }
                        }
                    });
                    return new VolumeData(values, count, count, count);
                }

            """) { MinimumCount = 4 },
        new("greenSpiral", "A green spiral upward", DataShape.Points3D, 400, Spiral, "Points3D", method => SampleCatalog.Header("a green spiral upward", method, "Points3D", "The number of points.") + """
                {
                    var path = new Points3D(count);
                    for (var i = 0; i < count; i++)
                    {
                        var turn = i * 0.05;
                        path.Append(Math.Cos(turn) * 0.5, Math.Sin(turn) * 0.5, (i / (count / 2.0)) - 1, new Rgba8(90, 240, 120, 255));
                    }

                    return path;
                }

            """) { MinimumCount = 2 },
        new("saddleMesh", "A saddle as triangles, tinted by height", DataShape.Model, 96, _ => Saddle(), "ModelFileData", method => SampleCatalog.Header("a saddle as triangles, tinted by height", method, "ModelFileData", "Not used: 96 by 96 corners.") + """
                {
                    const int Side = 96;
                    var (positions, colors, triangles) = (new Point3D[Side * Side], new Rgba8[Side * Side], new List<int>(Side * Side * 6));
                    for (var row = 0; row < Side; row++)
                    {
                        for (var column = 0; column < Side; column++)
                        {
                            var (x, y) = ((column * 2.0 / (Side - 1)) - 1, (row * 2.0 / (Side - 1)) - 1);
                            var z = -0.3 + (0.2 * x * y);
                            var share = (z + 1) / 2;
                            positions[(row * Side) + column] = new Point3D(x, y, z);
                            colors[(row * Side) + column] = new Rgba8((byte)(40 + (215 * share)), (byte)(110 + (60 * share)), (byte)(230 - (170 * share)), 255);
                            if (row > 0 && column > 0)
                            {
                                var corner = (row * Side) + column;
                                triangles.AddRange([corner - Side - 1, corner - Side, corner, corner - Side - 1, corner, corner - 1]);
                            }
                        }
                    }

                    return new ModelFileData(positions, [.. triangles], colors);
                }

            """) { HasCount = false },
        new("landscape", "A scanned landscape, loaded when drawn", DataShape.Points3D, 20_000_000, count => Landscape(count), Loading, method => SampleCatalog.Header("a scanned landscape, made when the cloud is first drawn", method, Loading, "The number of points.") + """
                {
                    return report =>
                    {
                        var (x, y, z, colors) = (new double[count], new double[count], new double[count], new Rgba8[count]);
                        var finished = 0;

                        // A fixed number of parts, so that the points do not depend on the number of processors.
                        Parallel.For(0, 32, part =>
                        {
                            var random = new Random(part);
                            var end = (int)((long)count * (part + 1) / 32);
                            for (var i = (int)((long)count * part / 32); i < end; i++)
                            {
                                var (a, b) = ((random.NextDouble() * 2) - 1, (random.NextDouble() * 2) - 1);
                                var height = (0.45 * Math.Sin(a * 4) * Math.Cos(b * 3)) + (0.15 * Math.Sin((a + b) * 11)) + (0.02 * random.NextDouble());
                                (x[i], y[i], z[i]) = (a, b, height);
                                colors[i] = Colormaps.Viridis.Sample((height + 0.65) / 1.3);
                            }

                            report.Report(Interlocked.Increment(ref finished) / 32.0);
                        });
                        return new PointCloudArrays(x, y, z, colors);
                    };
                }

            """) { MinimumCount = 1 },
    ];

    private static VolumeData Body(int count)
    {
        var values = new ushort[count * count * count];
        Parallel.For(0, count, z =>
        {
            for (var y = 0; y < count; y++)
            {
                for (var x = 0; x < count; x++)
                {
                    var (a, b, c) = ((2.0 * x / (count - 1)) - 1, (2.0 * y / (count - 1)) - 1, (2.0 * z / (count - 1)) - 1);
                    var radius = Math.Sqrt((a * a) + (b * b * 1.5) + (c * c * 1.2));
                    var shell = Math.Exp(-Math.Pow((radius - 0.8) / 0.05, 2));
                    var inside = radius < 0.8 ? 0.25 + (0.08 * Math.Sin(a * 9) * Math.Sin(b * 8) * Math.Sin(c * 7)) : 0;
                    var spot = Math.Exp(-(((a - 0.3) * (a - 0.3)) + ((b + 0.2) * (b + 0.2)) + ((c - 0.1) * (c - 0.1))) / 0.02);
                    var second = 0.95 * Math.Exp(-(((a + 0.35) * (a + 0.35)) + ((b - 0.15) * (b - 0.15)) + ((c + 0.25) * (c + 0.25))) / 0.01);
                    values[(z * count * count) + (y * count) + x] = (ushort)(Math.Clamp(Math.Max(Math.Max(shell * 0.75, inside), Math.Max(spot, second)), 0, 1) * 60_000);
                }
            }
        });
        return new VolumeData(values, count, count, count);
    }

    private static Points3D Spiral(int count)
    {
        var path = new Points3D(count);
        for (var i = 0; i < count; i++)
        {
            var turn = i * 0.05;
            path.Append(Math.Cos(turn) * 0.5, Math.Sin(turn) * 0.5, (i / (count / 2.0)) - 1, new Rgba8(90, 240, 120, 255));
        }

        return path;
    }

    private static ModelFileData Saddle()
    {
        const int Side = 96;
        var (positions, colors, triangles) = (new Point3D[Side * Side], new Rgba8[Side * Side], new List<int>(Side * Side * 6));
        for (var row = 0; row < Side; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                var (x, y) = ((column * 2.0 / (Side - 1)) - 1, (row * 2.0 / (Side - 1)) - 1);
                var z = -0.3 + (0.2 * x * y);
                var share = (z + 1) / 2;
                positions[(row * Side) + column] = new Point3D(x, y, z);
                colors[(row * Side) + column] = new Rgba8((byte)(40 + (215 * share)), (byte)(110 + (60 * share)), (byte)(230 - (170 * share)), 255);
                if (row > 0 && column > 0)
                {
                    var corner = (row * Side) + column;
                    triangles.AddRange([corner - Side - 1, corner - Side, corner, corner - Side - 1, corner, corner - 1]);
                }
            }
        }

        return new ModelFileData(positions, [.. triangles], colors);
    }

    private static Func<IProgress<double>, PointCloudArrays> Landscape(int count) => report =>
    {
        var (x, y, z, colors) = (new double[count], new double[count], new double[count], new Rgba8[count]);
        var finished = 0;
        Parallel.For(0, 32, part =>
        {
            var random = new Random(part); // NOSONAR: makes sample data, no security
            var end = (int)((long)count * (part + 1) / 32);
            for (var i = (int)((long)count * part / 32); i < end; i++)
            {
                var (a, b) = ((random.NextDouble() * 2) - 1, (random.NextDouble() * 2) - 1);
                var height = (0.45 * Math.Sin(a * 4) * Math.Cos(b * 3)) + (0.15 * Math.Sin((a + b) * 11)) + (0.02 * random.NextDouble());
                (x[i], y[i], z[i]) = (a, b, height);
                colors[i] = Colormaps.Viridis.Sample((height + 0.65) / 1.3);
            }

            report.Report(Interlocked.Increment(ref finished) / 32.0);
        });
        return new PointCloudArrays(x, y, z, colors);
    };

    /// <summary>The tissue, shell and spots of the body, as the showroom shows them through it.</summary>
    /// <returns>The transfer function.</returns>
    public static TransferFunction3D Tissue() => new(
        new TransferPoint3D(0, new Rgba8(30, 60, 140, 255), 0),
        new TransferPoint3D(0.12, new Rgba8(30, 60, 140, 255), 0),
        new TransferPoint3D(0.3, new Rgba8(70, 150, 190, 255), 0.003),
        new TransferPoint3D(0.6, new Rgba8(70, 150, 190, 255), 0.006),
        new TransferPoint3D(0.76, new Rgba8(240, 190, 120, 255), 0.03),
        new TransferPoint3D(0.86, new Rgba8(255, 90, 60, 255), 0.5),
        new TransferPoint3D(1, new Rgba8(255, 90, 60, 255), 0.9));

    /// <summary>The same as code.</summary>
    public const string TissueCode = """

            // Clear around the body, its tissue faint, the shell and the spots solid.
            private static TransferFunction3D Tissue() => new(
                new TransferPoint3D(0, new Rgba8(30, 60, 140, 255), 0),
                new TransferPoint3D(0.12, new Rgba8(30, 60, 140, 255), 0),
                new TransferPoint3D(0.3, new Rgba8(70, 150, 190, 255), 0.003),
                new TransferPoint3D(0.6, new Rgba8(70, 150, 190, 255), 0.006),
                new TransferPoint3D(0.76, new Rgba8(240, 190, 120, 255), 0.03),
                new TransferPoint3D(0.86, new Rgba8(255, 90, 60, 255), 0.5),
                new TransferPoint3D(1, new Rgba8(255, 90, 60, 255), 0.9));

        """;
}
