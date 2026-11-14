using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// Points that keep coming, as on the live 3D pages of the showroom: the path of a particle, 200 steps every 30 ms, and
/// a sweep of a surface, 30 000 points every 30 ms, a million points per second. The points start empty; the oldest fall
/// out when the capacity is reached. Kept twice, as C# for the preview and as text for the code.
/// </summary>
internal static class LiveSamples3D
{
    private const int PathStep = 200;
    private const int SweepStep = 30_000;

    /// <summary>Gets the identifiers of the live samples.</summary>
    public static IReadOnlySet<string> Ids { get; } = new HashSet<string>(StringComparer.Ordinal) { "particlePathLive", "sweepLive" };

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        new("particlePathLive", "Path of a particle, live", DataShape.Points3D, 100_000, count => new Points3D(count), "Points3D", method => SampleCatalog.Header("the path of a particle, 200 steps every 30 ms; the oldest fall out", method, "Points3D", "How many points stay.") + """
                {
                    var points = new Points3D(count);
                    var (x, y, z) = (1.0, 1.0, 20.0);
                    _pathFeed = new Timer(
                        _ =>
                        {
                            lock (points)
                            {
                                for (var i = 0; i < 200; i++)
                                {
                                    (x, y, z) = (x + (0.02 * (y - x)), y + (0.002 * ((x * (28 - z)) - y)), z + (0.002 * ((x * y) - (8.0 / 3 * z))));
                                    points.Append(x, y, z, Colormaps.Viridis.Sample(points.Total % count / (double)count));
                                }
                            }
                        },
                        null,
                        0,
                        30);
                    return points;
                }

                // Keeps the timer of the path alive.
                private static Timer? _pathFeed;

            """) { MinimumCount = 1000 },
        new("sweepLive", "Sweep of a surface, a million points per second", DataShape.Points3D, 5_000_000, count => new Points3D(count), "Points3D", method => SampleCatalog.Header("a sweep along the first axis, 30 000 points every 30 ms; the oldest fall out", method, "Points3D", "How many points stay.") + """
                {
                    var points = new Points3D(count);
                    var random = new Random(2);
                    var (x, y, z, colors) = (new double[30_000], new double[30_000], new double[30_000], new Rgba8[30_000]);
                    _sweepFeed = new Timer(
                        _ =>
                        {
                            lock (points)
                            {
                                var sweep = points.Total / (double)count % 1;
                                for (var i = 0; i < x.Length; i++)
                                {
                                    var (a, b) = ((sweep * 2) - 1 + (random.NextDouble() * 0.02), (random.NextDouble() * 2) - 1);
                                    (x[i], y[i], z[i]) = (a, b, 0.5 * Math.Sin(a * 4) * Math.Cos(b * 3));
                                    colors[i] = Colormaps.Viridis.Sample(z[i] + 0.5);
                                }

                                points.Append(x, y, z, colors);
                            }
                        },
                        null,
                        0,
                        30);
                    return points;
                }

                // Keeps the timer of the sweep alive.
                private static Timer? _sweepFeed;

            """) { MinimumCount = 30_000 },
    ];

    /// <summary>Starts the points of a live sample coming: a timer, which disposing stops.</summary>
    /// <param name="series">The series.</param>
    /// <param name="data">Its points.</param>
    /// <returns>The timer, or <see langword="null"/> for other data.</returns>
    public static IDisposable? Start(SeriesSpec series, object data)
    {
        if (series.Data.Kind != DataKind.Sample || data is not Points3D points)
        {
            return null;
        }

        return series.Data.Sample switch
        {
            "particlePathLive" => Path(points),
            "sweepLive" => Sweep(points),
            _ => null,
        };
    }

    private static Timer Path(Points3D points)
    {
        var (x, y, z) = (1.0, 1.0, 20.0);
        var capacity = (double)points.Capacity;
        return new Timer(
            _ =>
            {
                lock (points)
                {
                    for (var i = 0; i < PathStep; i++)
                    {
                        (x, y, z) = (x + (0.02 * (y - x)), y + (0.002 * ((x * (28 - z)) - y)), z + (0.002 * ((x * y) - (8.0 / 3 * z))));
                        points.Append(x, y, z, Colormaps.Viridis.Sample(points.Total % capacity / capacity));
                    }
                }
            },
            null,
            0,
            30);
    }

    private static Timer Sweep(Points3D points)
    {
        var random = new Random(2); // NOSONAR: makes sample data, no security
        var (x, y, z, colors) = (new double[SweepStep], new double[SweepStep], new double[SweepStep], new Rgba8[SweepStep]);
        var capacity = (double)points.Capacity;
        return new Timer(
            _ =>
            {
                lock (points)
                {
                    var sweep = points.Total / capacity % 1;
                    for (var i = 0; i < SweepStep; i++)
                    {
                        var (a, b) = ((sweep * 2) - 1 + (random.NextDouble() * 0.02), (random.NextDouble() * 2) - 1);
                        (x[i], y[i], z[i]) = (a, b, 0.5 * Math.Sin(a * 4) * Math.Cos(b * 3));
                        colors[i] = Colormaps.Viridis.Sample(z[i] + 0.5);
                    }

                    points.Append(x, y, z, colors);
                }
            },
            null,
            0,
            30);
    }
}
