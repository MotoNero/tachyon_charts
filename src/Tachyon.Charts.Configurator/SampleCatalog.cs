using System.Globalization;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>Data made up for a preview: points, rows or a grid, as the shape of the type asks.</summary>
/// <param name="X">The X of each point.</param>
/// <param name="Y">The Y of each point.</param>
/// <param name="Rows">The rows of values, one value per position.</param>
/// <param name="GridWidth">The columns of the grid.</param>
/// <param name="GridHeight">The rows of the grid.</param>
/// <param name="Grid">The values of the grid, row by row.</param>
public sealed record SampleSet(double[] X, double[] Y, IReadOnlyList<double[]> Rows, int GridWidth, int GridHeight, float[] Grid)
{
    /// <summary>Creates points.</summary>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values.</param>
    /// <returns>The set.</returns>
    public static SampleSet Points(double[] x, double[] y) => new(x, y, [], 0, 0, []);

    /// <summary>Creates rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The set.</returns>
    public static SampleSet FromRows(IReadOnlyList<double[]> rows) => new([], [], rows, 0, 0, []);

    /// <summary>Creates a grid.</summary>
    /// <param name="width">The columns.</param>
    /// <param name="height">The rows.</param>
    /// <param name="values">The values, row by row.</param>
    /// <returns>The set.</returns>
    public static SampleSet FromGrid(int width, int height, float[] values) => new([], [], [], width, height, values);
}

/// <summary>
/// A generator of sample data. It makes the data of the preview and writes the C# that makes the same data, so that the
/// generated code shows what the preview shows.
/// </summary>
/// <param name="Id">The identifier in <see cref="DataSpec.Sample"/>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Shape">The form of the data.</param>
/// <param name="DefaultCount">The number of points, positions or grid columns by default.</param>
/// <param name="Create">Makes the data for a count.</param>
/// <param name="Formula">The C# expression of the value, in terms of <c>x</c> and <c>i</c> for points and rows, <c>u</c> and <c>v</c> for a grid.</param>
public sealed record SampleInfo(string Id, string Name, DataShape Shape, int DefaultCount, Func<int, SampleSet> Create, string Formula);

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
}

/// <summary>The generators of the core: curves, a wandering signal, groups of bars and a grid with two peaks.</summary>
internal static class SampleCatalog
{
    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Curve("sine", "Two sines", 1000, x => Math.Sin(x) + (0.3 * Math.Sin(3.7 * x)), "Math.Sin(x) + (0.3 * Math.Sin(3.7 * x))"),
        Curve("damped", "Damped oscillation", 1000, x => Math.Exp(-x / 4) * Math.Cos(3 * x), "Math.Exp(-x / 4) * Math.Cos(3 * x)"),
        Curve("signal", "Wandering signal", 2000, x => (2 * Math.Sin(x * 0.9) * Math.Cos(x * 0.31)) + (0.3 * Math.Sin(x * 17.3)) + (0.15 * Math.Sin(x * 41.9)), "(2 * Math.Sin(x * 0.9) * Math.Cos(x * 0.31)) + (0.3 * Math.Sin(x * 17.3)) + (0.15 * Math.Sin(x * 41.9))"),
        new("groups", "Two groups of bars", DataShape.Rows, 6, Rows, "3 + (2 * Math.Sin(i)) | 2 + Math.Cos(i)"),
        new("peaks", "Grid with two peaks", DataShape.Grid, 128, Grid, "Math.Exp(-(((u - 0.3) * (u - 0.3)) + ((v - 0.4) * (v - 0.4))) / 0.02) + (0.6 * Math.Exp(-(((u - 0.7) * (u - 0.7)) + ((v - 0.65) * (v - 0.65))) / 0.03)) + (0.1 * Math.Sin(10 * u) * Math.Sin(8 * v))"),
    ];

    /// <summary>Gets the X of point i of n, spread from 0 to 10.</summary>
    public static double Place(int i, int count) => i * 10.0 / (count - 1);

    private static SampleInfo Curve(string id, string name, int count, Func<double, double> curve, string formula) => new(id, name, DataShape.Points, count, n =>
    {
        n = Math.Max(2, n);
        var (x, y) = (new double[n], new double[n]);
        for (var i = 0; i < n; i++)
        {
            x[i] = Place(i, n);
            y[i] = curve(x[i]);
        }

        return SampleSet.Points(x, y);
    }, formula);

    private static SampleSet Rows(int count)
    {
        count = Math.Max(1, count);
        var (first, second) = (new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            first[i] = 3 + (2 * Math.Sin(i));
            second[i] = 2 + Math.Cos(i);
        }

        return SampleSet.FromRows([first, second]);
    }

    private static SampleSet Grid(int side)
    {
        side = Math.Max(2, side);
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

        return SampleSet.FromGrid(side, side, values);
    }
}
