using System.Globalization;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// A generator of sample data. It makes the data of the preview and writes the C# method that makes the same data, so
/// that the generated code shows what the preview shows.
/// </summary>
/// <param name="Id">The identifier in <see cref="DataSpec.Sample"/>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Shape">The form of the data, which the types ask for.</param>
/// <param name="DefaultCount">The number of values by default.</param>
/// <param name="Create">Makes the data for a count; the object has the type <paramref name="ReturnType"/> names.</param>
/// <param name="ReturnType">The C# type of the data, such as <c>DataSource&lt;double, double&gt;</c>.</param>
/// <param name="Method">Writes the C# method with a name that takes the count and returns the data, indented by four spaces.</param>
public sealed record SampleInfo(string Id, string Name, DataShape Shape, int DefaultCount, Func<int, object> Create, string ReturnType, Func<string, string> Method)
{
    /// <summary>Gets the smallest count that makes sense for the shape.</summary>
    public int MinimumCount { get; init; } = 2;

    /// <summary>Gets a value indicating whether the count changes the data; a phantom of fixed size has none to choose.</summary>
    public bool HasCount { get; init; } = true;

    /// <summary>Gets the value of a curve at an X, for live data, or <see langword="null"/> for data that are no curve.</summary>
    public Func<double, double>? Curve { get; init; }

    /// <summary>Gets the same as a C# expression of <c>x</c>.</summary>
    public string? CurveCode { get; init; }
}

/// <summary>A color of a parameter: #RRGGBB or #RRGGBBAA.</summary>
public static class SpecColor
{
    /// <summary>Reads a color.</summary>
    /// <param name="text">The text.</param>
    /// <param name="color">The color.</param>
    /// <returns><see langword="true"/> when the text is a color.</returns>
    public static bool TryParse(string? text, out Rgba8 color)
    {
        color = default;
        if (text is not { Length: 7 or 9 } || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var rgba = text.Length == 7 ? (value << 8) | 0xFF : value;
        color = new Rgba8((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        return true;
    }

    /// <summary>Writes a color as the fields take it: #RRGGBB when opaque, else #RRGGBBAA.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The text.</returns>
    public static string Format(Rgba8 color) =>
        color.A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}");

    /// <summary>
    /// Gets the colors the color picker offers at a click: the strong colors of the themes and the showroom, then
    /// grays from white to black.
    /// </summary>
    public static IReadOnlyList<Rgba8> Palette { get; } =
    [
        new(58, 134, 255, 255), new(80, 170, 240, 255), new(76, 201, 240, 255), new(6, 214, 160, 255),
        new(124, 252, 154, 255), new(255, 190, 11, 255), new(240, 190, 90, 255), new(240, 130, 90, 255),
        new(251, 86, 7, 255), new(230, 57, 70, 255), new(255, 0, 110, 255), new(247, 37, 133, 255),
        new(131, 56, 236, 255), new(114, 9, 183, 255), new(255, 255, 255, 255), new(200, 200, 200, 255),
        new(150, 160, 175, 255), new(100, 110, 125, 255), new(50, 55, 65, 255), new(0, 0, 0, 255),
    ];
}

/// <summary>The generators of the core. Each keeps its formula once and uses it for the data and for the code.</summary>
internal static class SampleCatalog
{
    private static readonly double[] _mix = [38, 22, 17, 12, 7, 4];
    private static readonly string[] _mixNames = ["Solar", "Wind", "Hydro", "Gas", "Coal", "Other"];

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Curve("sine", "Two sines", 1000, x => Math.Sin(x) + (0.3 * Math.Sin(3.7 * x)), "Math.Sin(x) + (0.3 * Math.Sin(3.7 * x))"),
        Curve("damped", "Damped oscillation", 1000, x => Math.Exp(-x / 4) * Math.Cos(3 * x), "Math.Exp(-x / 4) * Math.Cos(3 * x)"),
        Curve("signal", "Wandering signal", 2000, x => (2 * Math.Sin(x * 0.9) * Math.Cos(x * 0.31)) + (0.3 * Math.Sin(x * 17.3)) + (0.15 * Math.Sin(x * 41.9)), "(2 * Math.Sin(x * 0.9) * Math.Cos(x * 0.31)) + (0.3 * Math.Sin(x * 17.3)) + (0.15 * Math.Sin(x * 41.9))"),
        Curve("liveFixed", "Two sines, fast", 2000, x => Math.Sin(x * 2) + (0.3 * Math.Sin(x * 14)), "Math.Sin(x * 2) + (0.3 * Math.Sin(x * 14))"),
        Curve("liveNoisy", "Slow wave with noise", 2000, x => (Math.Sin(x * 2) * Math.Cos(x * 0.3)) + ((Random.Shared.NextDouble() - 0.5) * 0.2), "(Math.Sin(x * 2) * Math.Cos(x * 0.3)) + ((Random.Shared.NextDouble() - 0.5) * 0.2)"),
        Curve("decay", "Decaying pulses", 41, x => 2.5 * Math.Exp(-0.25 * x) * Math.Abs(Math.Cos(x * 1.5)), "2.5 * Math.Exp(-0.25 * x) * Math.Abs(Math.Cos(x * 1.5))"),
        Bubbles(),
        Band(),
        Layers(),
        Errors(),
        Rows(),
        Groups(),
        Skewed(),
        Mix(),
        Grid(),
    ];

    /// <summary>Gets the X of point i of n, spread from 0 to 10.</summary>
    public static double Place(int i, int count) => i * 10.0 / (count - 1);

    private static DataSource<double, double> Points(int count, Func<double, double> curve)
    {
        var data = new DataSource<double, double>(count);
        for (var i = 0; i < count; i++)
        {
            var x = Place(i, count);
            data.Append(x, curve(x));
        }

        return data;
    }

    internal static string Header(string summary, string name, string type, string count = "The number of values.") => $$"""
            /// <summary>Makes the sample data: {{summary}}.</summary>
            /// <param name="count">{{count}}</param>
            /// <returns>The data.</returns>
            public static {{type}} {{name}}(int count)

        """;

    private static SampleInfo Curve(string id, string name, int count, Func<double, double> curve, string formula) => new(
        id,
        name,
        DataShape.Points,
        count,
        n => Points(n, curve),
        "DataSource<double, double>",
        method => Header(name.ToLowerInvariant(), method, "DataSource<double, double>") + $$"""
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

    private static SampleInfo Bubbles()
    {
        const string Type = "(DataSource<double, double> Points, float[] Sizes)";
        return new(
            "bubbles",
            "Bubbles of different sizes",
            DataShape.Bubbles,
            60,
            n =>
            {
                var sizes = new float[n];
                for (var i = 0; i < n; i++)
                {
                    sizes[i] = (float)(6 + (12 * (0.5 + (0.5 * Math.Sin(i * 1.3)))));
                }

                return (Points(n, x => 5 + (3 * Math.Sin(x * 0.7) * Math.Cos(x * 0.3))), sizes);
            },
            Type,
            method => Header("bubbles of different sizes", method, Type) + """
                    {
                        var points = new DataSource<double, double>(count);
                        var sizes = new float[count];
                        for (var i = 0; i < count; i++)
                        {
                            var x = i * 10.0 / (count - 1);
                            points.Append(x, 5 + (3 * Math.Sin(x * 0.7) * Math.Cos(x * 0.3)));
                            sizes[i] = (float)(6 + (12 * (0.5 + (0.5 * Math.Sin(i * 1.3)))));
                        }

                        return (points, sizes);
                    }

                """);
    }

    private static SampleInfo Band()
    {
        const string Type = "(DataSource<double, double> First, DataSource<double, double> Second)";
        return new(
            "band",
            "Two crossing curves",
            DataShape.Band,
            300,
            n => (Points(n, Math.Sin), Points(n, x => 0.8 * Math.Cos(x * 0.8))),
            Type,
            method => Header("two crossing curves", method, Type) + """
                    {
                        var (first, second) = (new DataSource<double, double>(count), new DataSource<double, double>(count));
                        for (var i = 0; i < count; i++)
                        {
                            var x = i * 10.0 / (count - 1);
                            first.Append(x, Math.Sin(x));
                            second.Append(x, 0.8 * Math.Cos(x * 0.8));
                        }

                        return (first, second);
                    }

                """);
    }

    private static SampleInfo Layers() => new(
        "layers",
        "Three layers",
        DataShape.Layers,
        300,
        n => new[] { Points(n, x => 2 + Math.Sin(x)), Points(n, x => 2 + Math.Cos(x * 0.7)), Points(n, x => 1.5 + (0.5 * Math.Sin(x * 1.3))) },
        "DataSource<double, double>[]",
        method => Header("three layers", method, "DataSource<double, double>[]") + """
                {
                    var layers = new[] { new DataSource<double, double>(count), new DataSource<double, double>(count), new DataSource<double, double>(count) };
                    for (var i = 0; i < count; i++)
                    {
                        var x = i * 10.0 / (count - 1);
                        layers[0].Append(x, 2 + Math.Sin(x));
                        layers[1].Append(x, 2 + Math.Cos(x * 0.7));
                        layers[2].Append(x, 1.5 + (0.5 * Math.Sin(x * 1.3)));
                    }

                    return layers;
                }

            """);

    private static SampleInfo Errors()
    {
        const string Type = "(DataSource<double, double> Values, double[] Lower, double[] Upper)";
        return new(
            "errors",
            "Measurements with errors",
            DataShape.Errors,
            13,
            n =>
            {
                var values = new DataSource<double, double>(n);
                var (lower, upper) = (new double[n], new double[n]);
                for (var i = 0; i < n; i++)
                {
                    values.Append(i, 2 + Math.Sin(i * 0.8));
                    lower[i] = 0.2 + (0.15 * Math.Abs(Math.Sin(i * 1.7)));
                    upper[i] = 0.25 + (0.2 * Math.Abs(Math.Cos(i * 1.1)));
                }

                return (values, lower, upper);
            },
            Type,
            method => Header("measurements with errors", method, Type) + """
                    {
                        var values = new DataSource<double, double>(count);
                        var (lower, upper) = (new double[count], new double[count]);
                        for (var i = 0; i < count; i++)
                        {
                            values.Append(i, 2 + Math.Sin(i * 0.8));
                            lower[i] = 0.2 + (0.15 * Math.Abs(Math.Sin(i * 1.7)));
                            upper[i] = 0.25 + (0.2 * Math.Abs(Math.Cos(i * 1.1)));
                        }

                        return (values, lower, upper);
                    }

                """)
        { MinimumCount = 1 };
    }

    private static SampleInfo Rows() => new(
        "groups",
        "Two groups of bars",
        DataShape.Rows,
        6,
        n =>
        {
            var (first, second) = (new double[n], new double[n]);
            for (var i = 0; i < n; i++)
            {
                first[i] = 3 + (2 * Math.Sin(i));
                second[i] = 2 + Math.Cos(i);
            }

            return new[] { first, second };
        },
        "double[][]",
        method => Header("two groups of bars", method, "double[][]") + """
                {
                    var (first, second) = (new double[count], new double[count]);
                    for (var i = 0; i < count; i++)
                    {
                        first[i] = 3 + (2 * Math.Sin(i));
                        second[i] = 2 + Math.Cos(i);
                    }

                    return [first, second];
                }

            """)
    { MinimumCount = 1 };

    // Five groups of measured values with different middles and spreads, the same on every run.
    private static SampleInfo Groups() => new(
        "samples",
        "Five groups of measurements",
        DataShape.Groups,
        200,
        n =>
        {
            var groups = new double[5][];
            for (var g = 0; g < 5; g++)
            {
                groups[g] = new double[n];
                for (var i = 0; i < n; i++)
                {
                    groups[g][i] = 2 + (0.6 * g) + ((0.4 + (0.1 * g)) * (Math.Sin((i * 1.37) + g) + Math.Sin((i * 0.41) + (2 * g)) + Math.Sin(i * 2.93)));
                }
            }

            return groups;
        },
        "double[][]",
        method => Header("five groups of measurements", method, "double[][]") + """
                {
                    var groups = new double[5][];
                    for (var g = 0; g < 5; g++)
                    {
                        groups[g] = new double[count];
                        for (var i = 0; i < count; i++)
                        {
                            groups[g][i] = 2 + (0.6 * g) + ((0.4 + (0.1 * g)) * (Math.Sin((i * 1.37) + g) + Math.Sin((i * 0.41) + (2 * g)) + Math.Sin(i * 2.93)));
                        }
                    }

                    return groups;
                }

            """);

    private static SampleInfo Skewed() => new(
        "skewed",
        "Skewed measurements",
        DataShape.Values,
        5000,
        n =>
        {
            var values = new double[n];
            for (var i = 0; i < n; i++)
            {
                values[i] = Math.Exp(1.2 + (0.35 * (Math.Sin(i * 1.37) + Math.Sin(i * 0.731) + Math.Sin(i * 2.311))));
            }

            return values;
        },
        "double[]",
        method => Header("skewed measurements", method, "double[]") + """
                {
                    var values = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = Math.Exp(1.2 + (0.35 * (Math.Sin(i * 1.37) + Math.Sin(i * 0.731) + Math.Sin(i * 2.311))));
                    }

                    return values;
                }

            """);

    private static SampleInfo Mix()
    {
        const string Type = "(double[] Values, string[] Names)";
        return new(
            "mix",
            "Power mix",
            DataShape.Slices,
            6,
            n => (_mix[..Math.Min(n, _mix.Length)], _mixNames[..Math.Min(n, _mix.Length)]),
            Type,
            method => Header("power mix", method, Type) + """
                    {
                        double[] values = [38, 22, 17, 12, 7, 4];
                        string[] names = ["Solar", "Wind", "Hydro", "Gas", "Coal", "Other"];
                        var take = Math.Min(count, values.Length);
                        return (values[..take], names[..take]);
                    }

                """)
        { MinimumCount = 1 };
    }

    private static SampleInfo Grid() => new(
        "peaks",
        "Grid with two peaks",
        DataShape.Grid,
        128,
        side =>
        {
            var values = new float[side * side];
            for (var row = 0; row < side; row++)
            {
                var v = row / (double)(side - 1);
                for (var column = 0; column < side; column++)
                {
                    var u = column / (double)(side - 1);
                    values[(row * side) + column] = (float)(Math.Exp(-(((u - 0.3) * (u - 0.3)) + ((v - 0.4) * (v - 0.4))) / 0.02)
                        + (0.6 * Math.Exp(-(((u - 0.7) * (u - 0.7)) + ((v - 0.65) * (v - 0.65))) / 0.03))
                        + (0.1 * Math.Sin(10 * u) * Math.Sin(8 * v)));
                }
            }

            var data = new HeatmapData(side, side);
            data.SetValues(values);
            return data;
        },
        "HeatmapData",
        method => Header("a grid with two peaks", method, "HeatmapData", "The columns and rows of the grid.") + """
                {
                    var values = new float[count * count];
                    for (var row = 0; row < count; row++)
                    {
                        var v = row / (double)(count - 1);
                        for (var column = 0; column < count; column++)
                        {
                            var u = column / (double)(count - 1);
                            values[(row * count) + column] = (float)(Math.Exp(-(((u - 0.3) * (u - 0.3)) + ((v - 0.4) * (v - 0.4))) / 0.02)
                                + (0.6 * Math.Exp(-(((u - 0.7) * (u - 0.7)) + ((v - 0.65) * (v - 0.65))) / 0.03))
                                + (0.1 * Math.Sin(10 * u) * Math.Sin(8 * v)));
                        }
                    }

                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    return data;
                }

            """);
}
