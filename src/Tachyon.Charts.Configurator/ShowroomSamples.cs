using System.Globalization;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The data of the showroom pages as sample data, so that a page opens with data of the same form: the same formula,
/// number of points and range. Each keeps its formula once for the preview and once as the code it writes.
/// </summary>
internal static class ShowroomSamples
{
    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Curve("pureSine", "Sine", 300, 10, Math.Sin, "Math.Sin(x)"),
        Curve("steps", "Sine in steps", 60, 10, x => Math.Round(Math.Sin(x) * 3) / 3, "Math.Round(Math.Sin(x) * 3) / 3"),
        Curve("raisedSine", "Raised sine", 300, 10, x => 1.2 + Math.Sin(x), "1.2 + Math.Sin(x)"),
        Curve("fallingPower", "Falling over decades", 300, 10, x => 5000 * Math.Exp(-x * 1.2), "5000 * Math.Exp(-x * 1.2)"),
        Curve("fadingPulses", "Fading pulses", 41, 40, x => 2.5 * Math.Exp(-0.1 * x) * Math.Abs(Math.Cos(x * 0.6)), "2.5 * Math.Exp(-0.1 * x) * Math.Abs(Math.Cos(x * 0.6))"),
        Cloud("cloud", "Evenly spread points", sizes: false),
        Cloud("cloudSizes", "Evenly spread points with sizes", sizes: true),
        Rows("barRows", "Three rows of six bars", [[3, 5, 2, 6, 4, 7], [2, 1, 4, 3, 5, 2], [1, 3, 2, 2, 2, 3]]),
        Rows("months", "A value per month", [[3, 4, 5, 6, 7, 8, 9, 8, 7, 6, 5, 4]]),
        Exponential(),
        ThreeGroups(),
        Curve("fixedCurve", "Two sines over 20", 2000, 20, x => Math.Sin(x) + (0.3 * Math.Sin(x * 7)), "Math.Sin(x) + (0.3 * Math.Sin(x * 7))"),
        Layers(),
        Errors(),
        Curve("peakBeside", "A peak beside small values", 2000, 19.99, x => 20 + (10 * Math.Sin(x * 3)) + (5 * Math.Sin(x * 11)) + (960 * Math.Exp(-((x - 12) * (x - 12)) / 0.02)), "20 + (10 * Math.Sin(x * 3)) + (5 * Math.Sin(x * 11)) + (960 * Math.Exp(-((x - 12) * (x - 12)) / 0.02))"),
        .. Enumerable.Range(0, 3).Select(line => Curve($"temperature{line + 1}", $"Temperature {line + 1}", 3000, 60, t => 60 + (8 * Math.Sin((t * 0.21) + (line * 2.1))) + (2 * Math.Sin((t * 1.7) + line)) + (line - 1), Invariant($"60 + (8 * Math.Sin((x * 0.21) + {line * 2.1})) + (2 * Math.Sin((x * 1.7) + {line})) + {line - 1}"))),
        .. Enumerable.Range(0, 6).Select(seed =>
        {
            var (level, swing) = seed < 3 ? (40 + (seed * 25.0), 8.0) : (3 + ((seed - 3) * 2.5), 0.8);
            return Curve($"bench{seed + 1}", $"Test bench {seed + 1}", 601, 60, t => level + (swing * Math.Sin((t * 0.15) + seed)) + (swing * 0.3 * Math.Sin((t * 0.9) + (seed * 2))), Invariant($"{level} + ({swing} * Math.Sin((x * 0.15) + {seed})) + ({swing * 0.3} * Math.Sin((x * 0.9) + {seed * 2}))"));
        }),
        Curve("hillCut", "A cut through hills", 256, 2, x => 0.7 * Math.Sin(x * 3) * Math.Cos(0.625), "0.7 * Math.Sin(x * 3) * Math.Cos(0.625)", -1),
        .. Enumerable.Range(0, 3).Select(line => Curve($"recordedTemperature{line + 1}", $"Recorded temperature {line + 1}", 285, 28.4, t => 60 + (8 * Math.Sin((t * 0.21) + (line * 2.1))) + (2 * Math.Sin((t * 1.7) + line)) + (line - 1), Invariant($"60 + (8 * Math.Sin((x * 0.21) + {line * 2.1})) + (2 * Math.Sin((x * 1.7) + {line})) + {line - 1}"))),
        Curve("quarterTarget", "A target rising per quarter", 8, 7, x => 52 + (4 * x), "52 + (4 * x)"),
        Rows("quarters", "Sales of two regions per quarter", [[48, 55, 61, 58, 70, 76, 81, 90], [35, 38, 42.5, 47, 44, 52, 57.25, 63]]),
        Sensors(),
        Curve("previewWave", "A wave for a theme preview", 180, 10, x => Math.Sin(x * 1.4) * 0.9, "Math.Sin(x * 1.4) * 0.9"),
        Curve("previewMarks", "Marks for a theme preview", 18, 1700.0 / 179, x => Math.Cos(x * 1.4) * 0.75, "Math.Cos(x * 1.4) * 0.75"),
        GappedLayers(),
        Curve("waveAroundZero", "x sin(x) / 2 - 1", 1001, 20, x => (x * Math.Sin(x) / 2) - 1, "(x * Math.Sin(x) / 2) - 1", -10),
        Curve("gammaLog", "Gamma log of layers", 1501, 1500, depth => 60 + (30 * (Math.Sin(depth / 90.0) + (0.5 * Math.Sin(depth / 23.0)) + (0.25 * Math.Sin(depth / 7.0)))), "60 + (30 * (Math.Sin(x / 90.0) + (0.5 * Math.Sin(x / 23.0)) + (0.25 * Math.Sin(x / 7.0))))"),
        Curve("boreholeTemperature", "Temperature down a borehole", 1501, 1500, depth => 12 + (depth * 0.03) + (2 * Math.Sin(depth / 300.0)), "12 + (x * 0.03) + (2 * Math.Sin(x / 300.0))"),
        Curve("coreSamples", "Core samples down a borehole", 16, 1350, depth => 12 + (depth * 0.03) + (2 * Math.Sin(depth / 300.0)) + (Math.Round((depth - 60) / 90) % 3 == 0 ? 2.5 : -1.5), "12 + (x * 0.03) + (2 * Math.Sin(x / 300.0)) + (Math.Round((x - 60) / 90) % 3 == 0 ? 2.5 : -1.5)", 60),
    ];

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // Three layers, the middle one with a gap from 4 to 5.
    private static SampleInfo GappedLayers() => new(
        "gappedLayers",
        "Three layers, one with a gap",
        DataShape.Layers,
        300,
        n =>
        {
            var layers = new[] { new DataSource<double, double>(n), new DataSource<double, double>(n), new DataSource<double, double>(n) };
            for (var i = 0; i < n; i++)
            {
                var x = i * 10.0 / (n - 1);
                layers[0].Append(x, 2 + Math.Sin(x));
                layers[1].Append(x, x is > 4 and < 5 ? double.NaN : 2 + Math.Cos(x * 0.7));
                layers[2].Append(x, 1.5 + (0.5 * Math.Sin(x * 2)));
            }

            return layers;
        },
        "DataSource<double, double>[]",
        method => SampleCatalog.Header("three layers, one with a gap", method, "DataSource<double, double>[]") + """
                {
                    var layers = new[] { new DataSource<double, double>(count), new DataSource<double, double>(count), new DataSource<double, double>(count) };
                    for (var i = 0; i < count; i++)
                    {
                        var x = i * 10.0 / (count - 1);
                        layers[0].Append(x, 2 + Math.Sin(x));
                        layers[1].Append(x, x is > 4 and < 5 ? double.NaN : 2 + Math.Cos(x * 0.7));
                        layers[2].Append(x, 1.5 + (0.5 * Math.Sin(x * 2)));
                    }

                    return layers;
                }

            """);

    // One value per sensor, a slow wave over many sensors.
    private static SampleInfo Sensors() => new(
        "sensors",
        "A value per sensor",
        DataShape.Rows,
        200,
        n => new[] { Enumerable.Range(0, n).Select(i => 1 + Math.Sin(i * 0.1)).ToArray() },
        "double[][]",
        method => SampleCatalog.Header("a value per sensor", method, "double[][]", "The number of sensors.") + """
                {
                    return [[.. Enumerable.Range(0, count).Select(i => 1 + Math.Sin(i * 0.1))]];
                }

            """)
    { MinimumCount = 1 };

    // Three layers on top of each other, the upper one faster.
    private static SampleInfo Layers() => new(
        "threeLayers",
        "Three layers, the upper one faster",
        DataShape.Layers,
        300,
        n =>
        {
            var layers = new[] { new DataSource<double, double>(n), new DataSource<double, double>(n), new DataSource<double, double>(n) };
            for (var i = 0; i < n; i++)
            {
                var x = i * 10.0 / (n - 1);
                layers[0].Append(x, 2 + Math.Sin(x));
                layers[1].Append(x, 2 + Math.Cos(x * 0.7));
                layers[2].Append(x, 1.5 + (0.5 * Math.Sin(x * 2)));
            }

            return layers;
        },
        "DataSource<double, double>[]",
        method => SampleCatalog.Header("three layers, the upper one faster", method, "DataSource<double, double>[]") + """
                {
                    var layers = new[] { new DataSource<double, double>(count), new DataSource<double, double>(count), new DataSource<double, double>(count) };
                    for (var i = 0; i < count; i++)
                    {
                        var x = i * 10.0 / (count - 1);
                        layers[0].Append(x, 2 + Math.Sin(x));
                        layers[1].Append(x, 2 + Math.Cos(x * 0.7));
                        layers[2].Append(x, 1.5 + (0.5 * Math.Sin(x * 2)));
                    }

                    return layers;
                }

            """);

    // Measurements with errors of five sizes, the same below and above.
    private static SampleInfo Errors()
    {
        const string Type = "(DataSource<double, double> Values, double[] Lower, double[] Upper)";
        return new(
            "fiveErrors",
            "Measurements with errors of five sizes",
            DataShape.Errors,
            13,
            n =>
            {
                var values = new DataSource<double, double>(n);
                var errors = new double[n];
                for (var i = 0; i < n; i++)
                {
                    values.Append(i * 12.0 / (n - 1), 2 + Math.Sin(i * 12.0 / (n - 1) * 0.8));
                    errors[i] = 0.15 + (0.05 * (i % 5));
                }

                return (values, errors, errors.ToArray());
            },
            Type,
            method => SampleCatalog.Header("measurements with errors of five sizes", method, Type) + """
                    {
                        var values = new DataSource<double, double>(count);
                        var errors = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            var x = i * 12.0 / (count - 1);
                            values.Append(x, 2 + Math.Sin(x * 0.8));
                            errors[i] = 0.15 + (0.05 * (i % 5));
                        }

                        return (values, errors, errors.ToArray());
                    }

                """);
    }

    // A curve over x from a start over a length, as the showroom samples it.
    private static SampleInfo Curve(string id, string name, int count, double length, Func<double, double> curve, string formula, double start = 0)
    {
        var size = length.ToString(CultureInfo.InvariantCulture);
        var from = start == 0 ? string.Empty : start.ToString(CultureInfo.InvariantCulture) + " + ";
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

    // Points spread evenly by the golden ratio, the same on every run; with sizes for bubbles.
    private static SampleInfo Cloud(string id, string name, bool sizes)
    {
        const string Points = "DataSource<double, double>";
        const string WithSizes = "(DataSource<double, double> Points, float[] Sizes)";
        return new(
            id,
            name,
            sizes ? DataShape.Bubbles : DataShape.Points,
            40,
            n =>
            {
                var data = new DataSource<double, double>(n);
                var size = new float[n];
                for (var i = 0; i < n; i++)
                {
                    data.Append(0.5 + (9 * (i * 0.618 % 1)), 0.5 + (9 * (i * 0.7549 % 1)));
                    size[i] = 6f + (i % 7 * 6);
                }

                return sizes ? (data, size) : data;
            },
            sizes ? WithSizes : Points,
            method => SampleCatalog.Header(name.ToLowerInvariant(), method, sizes ? WithSizes : Points) + $$"""
                    {
                        var data = new DataSource<double, double>(count);
                        var sizes = new float[count];
                        for (var i = 0; i < count; i++)
                        {
                            data.Append(0.5 + (9 * (i * 0.618 % 1)), 0.5 + (9 * (i * 0.7549 % 1)));
                            sizes[i] = 6f + (i % 7 * 6);
                        }

                        return {{(sizes ? "(data, sizes)" : "data")}};
                    }

                """);
    }

    // Fixed rows of bars; the number of values is theirs.
    private static SampleInfo Rows(string id, string name, double[][] rows)
    {
        var literal = "[" + string.Join(", ", rows.Select(row => "[" + string.Join(", ", row.Select(value => value.ToString(CultureInfo.InvariantCulture))) + "]")) + "]";
        return new(
            id,
            name,
            DataShape.Rows,
            rows[0].Length,
            _ => rows.Select(row => row.ToArray()).ToArray(),
            "double[][]",
            method => SampleCatalog.Header(name.ToLowerInvariant(), method, "double[][]", "Not used: the values are fixed.") + $$"""
                    {
                        return {{literal}};
                    }

                """)
        { HasCount = false, MinimumCount = 1 };
    }

    // Exponentially spread measurements, the same on every run.
    private static SampleInfo Exponential() => new(
        "exponential",
        "Exponentially spread measurements",
        DataShape.Values,
        5000,
        n =>
        {
            var random = new Random(3);
            var values = new double[n];
            for (var i = 0; i < n; i++)
            {
                values[i] = -Math.Log(1 - random.NextDouble()) * 2;
            }

            return values;
        },
        "double[]",
        method => SampleCatalog.Header("exponentially spread measurements", method, "double[]") + """
                {
                    var random = new Random(3);
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = -Math.Log(1 - random.NextDouble()) * 2;
                    }

                    return values;
                }

            """);

    // Three groups: normal, skewed and with two peaks, the same on every run.
    private static SampleInfo ThreeGroups() => new(
        "threeGroups",
        "Normal, skewed and two peaks",
        DataShape.Groups,
        300,
        n =>
        {
            var random = new Random(5);
            var groups = new double[3][];
            groups[0] = [.. Enumerable.Range(0, n).Select(_ => (random.NextDouble() + random.NextDouble() + random.NextDouble()) * 2)];
            groups[1] = [.. Enumerable.Range(0, n).Select(_ => -Math.Log(1 - random.NextDouble()) + 1)];
            groups[2] = [.. Enumerable.Range(0, n).Select(_ => random.Next(2) == 0 ? 2 + random.NextDouble() : 5 + random.NextDouble())];
            return groups;
        },
        "double[][]",
        method => SampleCatalog.Header("normal, skewed and two peaks", method, "double[][]", "The number of values per group.") + """
                {
                    var random = new Random(5);
                    double[] normal = [.. Enumerable.Range(0, count).Select(_ => (random.NextDouble() + random.NextDouble() + random.NextDouble()) * 2)];
                    double[] skewed = [.. Enumerable.Range(0, count).Select(_ => -Math.Log(1 - random.NextDouble()) + 1)];
                    double[] twoPeaks = [.. Enumerable.Range(0, count).Select(_ => random.Next(2) == 0 ? 2 + random.NextDouble() : 5 + random.NextDouble())];
                    return [normal, skewed, twoPeaks];
                }

            """);
}
