using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The data of the gallery pages of the showroom as sample data: the curves of its line scenes over 0 to 10, a circle,
/// gaps, random points, error bars and bars with a missing value, and the columns of its parallel coordinates.
/// </summary>
internal static class ShowroomGallery
{
    private const string Holes = "x switch { > 2 and < 3 => double.NaN, > 4.5 and < 5.0 => double.PositiveInfinity, > 5.1 and < 5.6 => double.NegativeInfinity, > 7 and < 7.2 => double.NaN, _ => 0.9 * Math.Sin(x * 1.3) }";

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Curve("wave13", "A wave", 240, x => 0.9 * Math.Sin(x * 1.3), "0.9 * Math.Sin(x * 1.3)"),
        Curve("wave13Cosine", "The same wave, shifted", 240, x => 0.9 * Math.Cos(x * 1.3), "0.9 * Math.Cos(x * 1.3)"),
        Curve("wave13Raised", "The wave, a little higher", 240, x => (0.9 * Math.Sin(x * 1.3)) + 0.05, "(0.9 * Math.Sin(x * 1.3)) + 0.05"),
        Curve("wave11", "A slower wave", 25, x => Math.Sin(x * 1.1), "Math.Sin(x * 1.1)"),
        Curve("slowCosine", "A slow, smaller wave", 240, x => 0.6 * Math.Cos(x * 0.8), "0.6 * Math.Cos(x * 0.8)"),
        Curve("slowCosineHalf", "A slow, half wave", 240, x => 0.5 * Math.Cos(x * 0.8), "0.5 * Math.Cos(x * 0.8)"),
        Curve("fastCosineHalf", "A fast, half wave", 240, x => 0.5 * Math.Cos(x * 2), "0.5 * Math.Cos(x * 2)"),
        Curve("ramp", "A straight ramp", 240, x => (x * 0.6) - 3.5, "(x * 0.6) - 3.5"),
        Curve("shortCosine", "A shifted, smaller wave", 300, x => Math.Cos(x * 1.3) * 0.8, "Math.Cos(x * 1.3) * 0.8"),
        Curve("wavesWithHoles", "A wave with gaps and infinite values", 240, Hole, Holes),
        Curve("wavesWithHolesTurned", "The gaps, turned and lower", 60, x => Hole(x) * -0.8, "-0.8 * (" + Holes + ")"),
        Curve("vibration", "A slow wave with a fast vibration", 200_000, x => (0.3 * Math.Sin(x * 2)) - 0.6 + (Math.Sin(x * 4000) * 0.1), "(0.3 * Math.Sin(x * 2)) - 0.6 + (Math.Sin(x * 4000) * 0.1)"),
        Circle(),
        RandomPoints(),
        GappedErrors(),
        Rows(),
        Columns(),
        SeriesCircuit(),
        Loads(),
    ];

    // A series circuit of 20 ohms, its reactance from capacitive to inductive.
    private static SampleInfo SeriesCircuit()
    {
        const string Type = "(double[] Resistance, double[] Reactance)";
        return new(
            "seriesCircuit",
            "A series circuit over a sweep",
            DataShape.Smith,
            200,
            n =>
            {
                var (resistance, reactance) = (new double[n], new double[n]);
                for (var i = 0; i < n; i++)
                {
                    var detuning = (i - (n / 2.0)) / (n / 2.0);
                    (resistance[i], reactance[i]) = (20 + (10 * detuning * detuning), 120 * detuning);
                }

                return (resistance, reactance);
            },
            Type,
            method => SampleCatalog.Header("a series circuit over a sweep", method, Type, "The number of frequencies.") + """
                    {
                        var (resistance, reactance) = (new double[count], new double[count]);
                        for (var i = 0; i < count; i++)
                        {
                            var detuning = (i - (count / 2.0)) / (count / 2.0);
                            (resistance[i], reactance[i]) = (20 + (10 * detuning * detuning), 120 * detuning);
                        }

                        return (resistance, reactance);
                    }

                """);
    }

    // Six measured values per row of a machine under load, the same on every run.
    private static SampleInfo Loads() => new(
        "loads",
        "A machine under load",
        DataShape.Groups,
        20_000,
        n =>
        {
            var random = new Random(1); // NOSONAR: makes sample data, no security
            var columns = new double[6][];
            for (var column = 0; column < columns.Length; column++)
            {
                columns[column] = new double[n];
            }

            for (var row = 0; row < n; row++)
            {
                var load = random.NextDouble();
                columns[0][row] = 3 + (2 * load) + (random.NextDouble() * 0.3);
                columns[1][row] = 10 * load * load;
                columns[2][row] = 25 + (40 * load) + (random.NextDouble() * 5);
                columns[3][row] = 1000 + (random.NextDouble() * 50);
                columns[4][row] = 50 + (random.NextDouble() * 0.2);
                columns[5][row] = -60 + (30 * load);
            }

            return columns;
        },
        "double[][]",
        method => SampleCatalog.Header("a machine under load", method, "double[][]", "The number of rows.") + """
                {
                    var random = new Random(1);
                    var columns = new double[6][];
                    for (var column = 0; column < columns.Length; column++)
                    {
                        columns[column] = new double[count];
                    }

                    for (var row = 0; row < count; row++)
                    {
                        var load = random.NextDouble();
                        columns[0][row] = 3 + (2 * load) + (random.NextDouble() * 0.3);
                        columns[1][row] = 10 * load * load;
                        columns[2][row] = 25 + (40 * load) + (random.NextDouble() * 5);
                        columns[3][row] = 1000 + (random.NextDouble() * 50);
                        columns[4][row] = 50 + (random.NextDouble() * 0.2);
                        columns[5][row] = -60 + (30 * load);
                    }

                    return columns;
                }

            """);

    private static double Hole(double x) => x switch
    {
        > 2 and < 3 => double.NaN,
        > 4.5 and < 5.0 => double.PositiveInfinity,
        > 5.1 and < 5.6 => double.NegativeInfinity,
        > 7 and < 7.2 => double.NaN,
        _ => 0.9 * Math.Sin(x * 1.3),
    };

    // A curve over x from 0 to 10.
    private static SampleInfo Curve(string id, string name, int count, Func<double, double> curve, string formula) => new(
        id,
        name,
        DataShape.Points,
        count,
        n =>
        {
            var data = new DataSource<double, double>(n);
            for (var i = 0; i < n; i++)
            {
                var x = i * 10.0 / (n - 1);
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
                        var x = i * 10.0 / (count - 1);
                        data.Append(x, {{formula}});
                    }

                    return data;
                }

            """)
    { Curve = curve, CurveCode = formula };

    // A circle as one line, whose points run back over the X axis.
    private static SampleInfo Circle() => new(
        "circle",
        "A circle around 5",
        DataShape.Points,
        121,
        n =>
        {
            var data = new DataSource<double, double>(n);
            for (var i = 0; i < n; i++)
            {
                var angle = i * Math.Tau / (n - 1);
                data.Append(5 + (2 * Math.Cos(angle)), Math.Sin(angle));
            }

            return data;
        },
        "DataSource<double, double>",
        method => SampleCatalog.Header("a circle around 5", method, "DataSource<double, double>") + """
                {
                    var data = new DataSource<double, double>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var angle = i * Math.Tau / (count - 1);
                        data.Append(5 + (2 * Math.Cos(angle)), Math.Sin(angle));
                    }

                    return data;
                }

            """);

    // Points spread at random over 0 to 10 and -1.5 to 1.5, the same on every run.
    private static SampleInfo RandomPoints() => new(
        "randomPoints",
        "Random points",
        DataShape.Points,
        400,
        n =>
        {
            var random = new Random(5); // NOSONAR: makes sample data, no security
            var data = new DataSource<double, double>(n);
            for (var i = 0; i < n; i++)
            {
                data.Append(random.NextDouble() * 10, (random.NextDouble() * 3) - 1.5);
            }

            return data;
        },
        "DataSource<double, double>",
        method => SampleCatalog.Header("random points", method, "DataSource<double, double>") + """
                {
                    var random = new Random(5);
                    var data = new DataSource<double, double>(count);
                    for (var i = 0; i < count; i++)
                    {
                        data.Append(random.NextDouble() * 10, (random.NextDouble() * 3) - 1.5);
                    }

                    return data;
                }

            """);

    // Measurements with errors of five sizes and one missing in the middle.
    private static SampleInfo GappedErrors()
    {
        const string Type = "(DataSource<double, double> Values, double[] Lower, double[] Upper)";
        return new(
            "gappedErrors",
            "Measurements with errors and a gap",
            DataShape.Errors,
            13,
            n =>
            {
                var values = new DataSource<double, double>(n);
                var errors = new double[n];
                for (var i = 0; i < n; i++)
                {
                    var x = i * 12.0 / (n - 1);
                    values.Append(x, x is > 5.5 and < 6.5 ? double.NaN : 2 + Math.Sin(x * 0.8));
                    errors[i] = 0.15 + (0.05 * (i % 5));
                }

                return (values, errors, errors.ToArray());
            },
            Type,
            method => SampleCatalog.Header("measurements with errors and a gap", method, Type) + """
                    {
                        var values = new DataSource<double, double>(count);
                        var errors = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            var x = i * 12.0 / (count - 1);
                            values.Append(x, x is > 5.5 and < 6.5 ? double.NaN : 2 + Math.Sin(x * 0.8));
                            errors[i] = 0.15 + (0.05 * (i % 5));
                        }

                        return (values, errors, errors.ToArray());
                    }

                """);
    }

    // Three rows of six bars, one value missing.
    private static SampleInfo Rows() => new(
        "barRowsGap",
        "Three rows of bars, one missing",
        DataShape.Rows,
        6,
        _ => new[] { new double[] { 3, 5, 2, 6, 4, 7 }, [2, 1, 4, 3, 5, 2], [1, 3, double.NaN, 2, 2, 3] },
        "double[][]",
        method => SampleCatalog.Header("three rows of bars, one missing", method, "double[][]", "Not used: the values are fixed.") + """
                {
                    return [[3, 5, 2, 6, 4, 7], [2, 1, 4, 3, 5, 2], [1, 3, double.NaN, 2, 2, 3]];
                }

            """)
    { HasCount = false, MinimumCount = 1 };

    // Six columns of 2000 rows; the second falls as the first rises.
    private static SampleInfo Columns() => new(
        "sixColumns",
        "Six columns, the second against the first",
        DataShape.Groups,
        2000,
        n =>
        {
            var columns = new double[6][];
            for (var column = 0; column < columns.Length; column++)
            {
                columns[column] = new double[n];
                for (var row = 0; row < n; row++)
                {
                    var noise = row * 2654435761L * (column + 3) % 1000 / 1000.0;
                    columns[column][row] = column == 1 ? 100 - (columns[0][row] * 0.8) - (noise * 20) : noise * 100 * (column + 1);
                }
            }

            return columns;
        },
        "double[][]",
        method => SampleCatalog.Header("six columns, the second against the first", method, "double[][]", "The number of rows.") + """
                {
                    var columns = new double[6][];
                    for (var column = 0; column < columns.Length; column++)
                    {
                        columns[column] = new double[count];
                        for (var row = 0; row < count; row++)
                        {
                            var noise = row * 2654435761L * (column + 3) % 1000 / 1000.0;
                            columns[column][row] = column == 1 ? 100 - (columns[0][row] * 0.8) - (noise * 20) : noise * 100 * (column + 1);
                        }
                    }

                    return columns;
                }

            """);
}
