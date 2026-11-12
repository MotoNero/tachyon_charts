using System.Globalization;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The data of the gallery scenes that show single abilities of the charts: a cloud of 50 million points, rows of
/// markers, points with colors of their own, large and uneven heatmaps, a running spectrogram and curves for
/// logarithmic axes and gradients.
/// </summary>
internal static class ShowroomChecks
{
    private const string Colored = "(DataSource<double, double> Points, float[] Sizes, Rgba8[] Colors)";
    private const string Uneven = "(HeatmapData Data, double[] XEdges, double[] YEdges)";

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Cloud(),
        .. Enumerable.Range(0, 6).Select(shape => Curve($"markerRow{shape + 1}", $"Row {shape + 1} of markers", 9, 1, 8, x => 6 - shape + (Math.Sin(x * 1.7) * 0.16), Invariant($"{6 - shape} + (Math.Sin(x * 1.7) * 0.16)"))),
        Curve("logDecay", "Decay over four decades, a gap below zero", 200, 0, 10, x => x is > 4 and < 5 ? -1 : 5000 * Math.Exp(-x * 1.2), "x is > 4 and < 5 ? -1 : 5000 * Math.Exp(-x * 1.2)"),
        Curve("logSteps", "Squared waves with a sign", 200, 0, 10, x => 64 * Math.Sin(x) * Math.Sin(x) * Math.Sign(Math.Sin(x * 0.7)), "64 * Math.Sin(x) * Math.Sin(x) * Math.Sign(Math.Sin(x * 0.7))"),
        Curve("logBoth", "A fading wave through zero", 200, 0, 10, x => 800 * Math.Sin(x * 1.5) * Math.Exp(-x * 0.4), "800 * Math.Sin(x * 1.5) * Math.Exp(-x * 0.4)"),
        Curve("gradientWave", "A wave over 0 to 40", 200, -0.5, 10, x => 20 + (15 * Math.Sin(x * 0.9)), "20 + (15 * Math.Sin(x * 0.9))"),
        new("gradientRow", "Ten bars", DataShape.Rows, 10, _ => new[] { new double[] { 3, 5, 7, 4, 8, 6, 9, 5, 4, 6 } }, "double[][]", method => SampleCatalog.Header("ten bars", method, "double[][]", "Not used: the values are fixed.") + """
                {
                    return [[3, 5, 7, 4, 8, 6, 9, 5, 4, 6]];
                }

            """) { HasCount = false, MinimumCount = 1 },
        ColorGrid(),
        FineField(),
        Decades(),
        Tone(),
        SteadyTone(),
        FineField(withHole: false),
    ];

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static SampleInfo Curve(string id, string name, int count, double start, double length, Func<double, double> curve, string formula)
    {
        var from = start == 0 ? string.Empty : start.ToString(CultureInfo.InvariantCulture) + " + ";
        var size = length.ToString(CultureInfo.InvariantCulture);
        return new(
            id,
            name,
            DataShape.Points,
            count,
            n =>
            {
                var data = new DataSource<double, double>(n);
                for (var i = 0; i < n; i++)
                {
                    var x = start + (i * length / (n - 1));
                    data.Append(x, curve(x));
                }

                return data;
            },
            "DataSource<double, double>",
            method => SampleCatalog.Header(name.ToLowerInvariant(), method, "DataSource<double, double>") + $$"""
                    {
                        var data = new DataSource<double, double>(count);
                        for (var i = 0; i < count; i++)
                        {
                            var x = {{from}}(i * {{size}} / (count - 1.0));
                            data.Append(x, {{formula}});
                        }

                        return data;
                    }

                """)
        { Curve = curve, CurveCode = formula };
    }

    // Four clusters of points, made in parallel and the same on every run; floats keep 50 million points small.
    private static SampleInfo Cloud() => new(
        "cloud50M",
        "Four clusters of many points",
        DataShape.Points,
        50_000_000,
        n =>
        {
            const int Parts = 16;
            var (x, y) = (new float[n], new float[n]);
            Parallel.For(0, Parts, part =>
            {
                var random = new Random(part + 1); // NOSONAR: makes sample data, no security
                var end = (int)((part + 1L) * n / Parts);
                for (var i = (int)((long)part * n / Parts); i < end; i++)
                {
                    var (centerX, centerY, spread) = random.Next(4) switch
                    {
                        0 => (3f, 3f, 0.9f),
                        1 => (7f, 6.5f, 0.6f),
                        2 => (2.5f, 7.5f, 0.35f),
                        _ => (5f, 5f, 2.2f),
                    };
                    x[i] = centerX + (Bell(random) * spread);
                    y[i] = centerY + (Bell(random) * spread);
                }
            });
            var cloud = new DataSource<float, float>(n);
            cloud.Append(x, y);
            return cloud;
        },
        "DataSource<float, float>",
        method => SampleCatalog.Header("four clusters of many points", method, "DataSource<float, float>") + """
                {
                    const int Parts = 16;
                    var (x, y) = (new float[count], new float[count]);
                    Parallel.For(0, Parts, part =>
                    {
                        var random = new Random(part + 1);
                        var end = (int)((part + 1L) * count / Parts);
                        for (var i = (int)((long)part * count / Parts); i < end; i++)
                        {
                            var (centerX, centerY, spread) = random.Next(4) switch
                            {
                                0 => (3f, 3f, 0.9f),
                                1 => (7f, 6.5f, 0.6f),
                                2 => (2.5f, 7.5f, 0.35f),
                                _ => (5f, 5f, 2.2f),
                            };

                            // Twelve uniform values make a good bell curve, cheaper than logarithms.
                            var (bellX, bellY) = (-6f, -6f);
                            for (var k = 0; k < 12; k++)
                            {
                                bellX += random.NextSingle();
                            }

                            for (var k = 0; k < 12; k++)
                            {
                                bellY += random.NextSingle();
                            }

                            x[i] = centerX + (bellX * spread);
                            y[i] = centerY + (bellY * spread);
                        }
                    });
                    var cloud = new DataSource<float, float>(count);
                    cloud.Append(x, y);
                    return cloud;
                }

            """);

    private static float Bell(Random random)
    {
        var sum = -6f;
        for (var i = 0; i < 12; i++)
        {
            sum += random.NextSingle();
        }

        return sum;
    }

    // A grid of points, the color running from blue to red and growing more opaque upwards, the size growing upwards.
    private static SampleInfo ColorGrid() => new(
        "colorGrid",
        "A grid of colored points",
        DataShape.Bubbles,
        180,
        _ =>
        {
            const int Columns = 18, Rows = 10;
            var points = new DataSource<double, double>(Columns * Rows);
            var (sizes, colors) = (new float[Columns * Rows], new Rgba8[Columns * Rows]);
            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var index = (row * Columns) + column;
                    points.Append(column + 1, row + 1 + (Math.Sin((column * 0.9) + row) * 0.12));
                    colors[index] = Heat(column / (float)(Columns - 1), row / (float)(Rows - 1));
                    sizes[index] = 6 + (row * 1.1f);
                }
            }

            return (points, sizes, colors);
        },
        Colored,
        method => SampleCatalog.Header("a grid of colored points", method, Colored, "Not used: 18 by 10 points.") + """
                {
                    const int Columns = 18, Rows = 10;
                    var points = new DataSource<double, double>(Columns * Rows);
                    var (sizes, colors) = (new float[Columns * Rows], new Rgba8[Columns * Rows]);
                    for (var row = 0; row < Rows; row++)
                    {
                        for (var column = 0; column < Columns; column++)
                        {
                            var index = (row * Columns) + column;
                            var (x, y) = (column / (float)(Columns - 1), row / (float)(Rows - 1));
                            points.Append(column + 1, row + 1 + (Math.Sin((column * 0.9) + row) * 0.12));
                            colors[index] = new Rgba8((byte)MathF.Round(50 + (205 * x)), (byte)MathF.Round(210 - (130 * MathF.Abs((2 * x) - 1))), (byte)MathF.Round(235 - (180 * x)), (byte)MathF.Round(150 + (105 * y)));
                            sizes[index] = 6 + (row * 1.1f);
                        }
                    }

                    return (points, sizes, colors);
                }

            """)
    { HasCount = false };

    private static Rgba8 Heat(float x, float y) =>
        new((byte)MathF.Round(50 + (205 * x)), (byte)MathF.Round(210 - (130 * MathF.Abs((2 * x) - 1))), (byte)MathF.Round(235 - (180 * x)), (byte)MathF.Round(150 + (105 * y)));

    // A fine field over 0 to 8 with a hole, 4096 cells per side by default.
    // The part of the formula of the fine field that leaves the hole.
    private const string Hole = "x is > 5 and < 6.5 && y is > 1 and < 2.5 ? float.NaN : ";

    private static SampleInfo FineField(bool withHole = true) => new(
        withHole ? "fineFieldWithHole" : "fineField",
        withHole ? "A fine field with a hole" : "A fine field without a hole",
        DataShape.Grid,
        4096,
        n =>
        {
            var values = new float[n * n];
            Parallel.For(0, n, row =>
            {
                var y = row * 8.0 / n;
                for (var column = 0; column < n; column++)
                {
                    var x = column * 8.0 / n;
                    values[(row * n) + column] = withHole && x is > 5 and < 6.5 && y is > 1 and < 2.5 ? float.NaN : (float)((Math.Sin(x * x * 0.6) * Math.Cos(y * 2.1)) + Math.Exp(-(((x - 3) * (x - 3)) + ((y - 5) * (y - 5)))));
                }
            });
            var data = new HeatmapData(n, n);
            data.SetValues(values);
            return data;
        },
        "HeatmapData",
        method => (SampleCatalog.Header(withHole ? "a fine field with a hole" : "a fine field without a hole", method, "HeatmapData", "The number of cells per side.") + """
                {
                    var values = new float[count * count];
                    Parallel.For(0, count, row =>
                    {
                        var y = row * 8.0 / count;
                        for (var column = 0; column < count; column++)
                        {
                            var x = column * 8.0 / count;
                            values[(row * count) + column] = x is > 5 and < 6.5 && y is > 1 and < 2.5 ? float.NaN : (float)((Math.Sin(x * x * 0.6) * Math.Cos(y * 2.1)) + Math.Exp(-(((x - 3) * (x - 3)) + ((y - 5) * (y - 5)))));
                        }
                    });
                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    return data;
                }

            """).Replace(Hole, withHole ? Hole : string.Empty, StringComparison.Ordinal))
    { MinimumCount = 2 };

    // Six by four cells of their own widths, the values over four decades, one missing.
    private static SampleInfo Decades() => new(
        "decades",
        "Cells over four decades",
        DataShape.UnevenGrid,
        6,
        _ =>
        {
            var data = new HeatmapData(6, 4);
            data.SetValues([.. Enumerable.Range(0, 24).Select(i => i == 9 ? float.NaN : MathF.Pow(10, i / 6f))]);
            return (data, new double[] { 0, 1, 3, 4, 8, 9, 10 }, new double[] { 1, 10, 100, 1000, 10000 });
        },
        Uneven,
        method => SampleCatalog.Header("cells over four decades", method, Uneven, "Not used: six by four cells.") + """
                {
                    var data = new HeatmapData(6, 4);
                    data.SetValues([.. Enumerable.Range(0, 24).Select(i => i == 9 ? float.NaN : MathF.Pow(10, i / 6f))]);
                    return (data, [0, 1, 3, 4, 8, 9, 10], [1, 10, 100, 1000, 10000]);
                }

            """)
    { HasCount = false };

    // The tone without pauses over 512 rows, as a spectrogram that keeps running shows it.
    private static SampleInfo SteadyTone() => new(
        "steadyTone",
        "A wandering tone over 512 rows",
        DataShape.Spectrogram,
        1024,
        n =>
        {
            var (data, row) = (new SpectrogramData(n, 512), new float[n]);
            for (long index = 0; index < 512; index++)
            {
                SpectrogramFeed.Append(data, index, row, pauses: false);
            }

            return data;
        },
        "SpectrogramData",
        method => SampleCatalog.Header("a wandering tone over 512 rows", method, "SpectrogramData", "The number of frequencies.") + """
                {
                    var (data, row) = (new SpectrogramData(count, 512), new float[count]);
                    for (long index = 0; index < 512; index++)
                    {
                        var center = (count / 2.0) + (count / 2.5 * Math.Sin(index * 0.03));
                        for (var i = 0; i < count; i++)
                        {
                            var (distance, overtone) = ((i - center) / 6, (i - (center / 2)) / 10);
                            row[i] = (float)(Math.Exp(-distance * distance) + (0.4 * Math.Exp(-overtone * overtone)) + (0.05 * Math.Sin(i * 0.9)));
                        }

                        data.AppendRow(row);
                    }

                    return data;
                }

            """)
    { MinimumCount = 2 };

    // A tone that wanders and its overtone, row by row with short pauses; 400 rows into 256.
    private static SampleInfo Tone() => new(
        "wanderingTone",
        "A wandering tone with pauses",
        DataShape.Spectrogram,
        256,
        n =>
        {
            var data = new SpectrogramData(n, 256);
            var row = new float[n];
            for (long index = 0; index < 400; index++)
            {
                var center = (n / 2.0) + (n / 2.5 * Math.Sin(index * 0.03));
                for (var i = 0; i < n; i++)
                {
                    var (distance, overtone) = ((i - center) / 6, (i - (center / 2)) / 10);
                    row[i] = index % 100 is >= 60 and < 66 ? float.NaN : (float)(Math.Exp(-distance * distance) + (0.4 * Math.Exp(-overtone * overtone)) + (0.05 * Math.Sin(i * 0.9)));
                }

                data.AppendRow(row);
            }

            return data;
        },
        "SpectrogramData",
        method => SampleCatalog.Header("a wandering tone with pauses", method, "SpectrogramData", "The number of frequencies.") + """
                {
                    var data = new SpectrogramData(count, 256);
                    var row = new float[count];
                    for (long index = 0; index < 400; index++)
                    {
                        var center = (count / 2.0) + (count / 2.5 * Math.Sin(index * 0.03));
                        for (var i = 0; i < count; i++)
                        {
                            var (distance, overtone) = ((i - center) / 6, (i - (center / 2)) / 10);
                            row[i] = index % 100 is >= 60 and < 66 ? float.NaN : (float)(Math.Exp(-distance * distance) + (0.4 * Math.Exp(-overtone * overtone)) + (0.05 * Math.Sin(i * 0.9)));
                        }

                        data.AppendRow(row);
                    }

                    return data;
                }

            """)
    { MinimumCount = 2 };
}
