using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Large point clouds for the scatter examples.</summary>
public static class PointCloudData
{
    /// <summary>Creates a cloud of points.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The points.</returns>
    public static DataSource<float, float> CreateCloud(int count)
    {
        const int Parts = 16;
        var x = new float[count];
        var y = new float[count];
        Parallel.For(0, Parts, part =>
        {
            var random = new Random(part + 1);
            var end = (int)((part + 1L) * count / Parts);
            for (var i = (int)((long)part * count / Parts); i < end; i++)
            {
                var cluster = random.Next(4);
                var (centerX, centerY, spread) = cluster switch
                {
                    0 => (3f, 3f, 0.9f),
                    1 => (7f, 6.5f, 0.6f),
                    2 => (2.5f, 7.5f, 0.35f),
                    _ => (5f, 5f, 2.2f),
                };

                // Twelve uniform values make a good bell curve, cheaper than logarithms.
                x[i] = centerX + (Bell(random) * spread);
                y[i] = centerY + (Bell(random) * spread);
            }
        });

        var cloud = new DataSource<float, float>(count);
        cloud.Append(x, y);
        return cloud;
    }

    internal static float Bell(Random random)
    {
        var sum = -6f;
        for (var i = 0; i < 12; i++)
        {
            sum += random.NextSingle();
        }

        return sum;
    }
}
