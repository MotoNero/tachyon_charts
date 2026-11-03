using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Point clouds that several 3D examples share.</summary>
public static class Clouds3D
{
    /// <summary>Describes three clouds with a color and size per point.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="count">The number of points.</param>
    public static void Cloud(Chart3DBuilder chart, int count)
    {
        var points = new Points3D(count);
        var random = new Random(5);
        double[,] centers = { { -0.45, -0.3, -0.3 }, { 0.4, 0.35, 0.1 }, { -0.1, 0.45, 0.5 } };
        var isLarge = count > 100_000;
        for (var i = 0; i < count; i++)
        {
            var cloud = i % 3;
            var (x, y, z) = (centers[cloud, 0] + Spread(random), centers[cloud, 1] + Spread(random), centers[cloud, 2] + Spread(random));
            var color = Tachyon.Charts.Core.Colors.Colormaps.Viridis.Sample((z + 1) / 2);
            points.Append(x, y, z, color, isLarge ? 1.5 : 3 + (8 * random.NextDouble()));
        }

        chart.Title("Three clouds").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddScatter(new Scatter3D(points));
    }

    internal static double Spread(Random random) => 0.24 * (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5);

    /// <summary>Describes a scanned landscape as a cloud.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="count">The number of points.</param>
    public static void Landscape(Chart3DBuilder chart, int count)
    {
        // Small clouds form at once, which keeps the snapshot the same on every run.
        PointCloud3D cloud;
        if (count <= PointCloud3D.PreviewLimit)
        {
            var points = CreateLandscape(count, new Progress<double>());
            cloud = new PointCloud3D(points.X, points.Y, points.Z, points.Colors);
        }
        else
        {
            cloud = new PointCloud3D(report => CreateLandscape(count, report));
        }

        chart.Title("Scanned landscape").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddCloud(cloud);
    }

    internal static PointCloudArrays CreateLandscape(int count, IProgress<double> report)
    {
        var (x, y, z) = (new double[count], new double[count], new double[count]);
        var colors = new Tachyon.Charts.Rendering.Rgba8[count];
        var map = Tachyon.Charts.Core.Colors.Colormaps.Viridis;
        var finished = 0;
        // A fixed number of parts, so that the points do not depend on the number of processors.
        const int Parts = 32;
        Parallel.For(0, Parts, part =>
        {
            var random = new Random(part);
            var end = (int)((long)count * (part + 1) / Parts);
            for (var i = (int)((long)count * part / Parts); i < end; i++)
            {
                var (a, b) = ((random.NextDouble() * 2) - 1, (random.NextDouble() * 2) - 1);
                var height = (0.45 * Math.Sin(a * 4) * Math.Cos(b * 3)) + (0.15 * Math.Sin((a + b) * 11)) + (0.02 * random.NextDouble());
                (x[i], y[i], z[i]) = (a, b, height);
                colors[i] = map.Sample((height + 0.65) / 1.3);
            }

            report.Report(Interlocked.Increment(ref finished) / (double)Parts);
        });
        return new PointCloudArrays(x, y, z, colors);
    }
}
