using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>Lines of equal height on a surface, and their shadows on the walls of the box.</summary>
/// <param name="Levels">The heights of the lines, in units of the third axis.</param>
public sealed record Contours3D(IReadOnlyList<double> Levels)
{
    /// <summary>The distance of the cuts on the walls, in shares of the half box.</summary>
    internal const float WallDistance = 0.25f;

    /// <summary>Gets the color.</summary>
    public Rgba8 Color { get; init; } = new(255, 255, 255, 200);

    /// <summary>Gets the width in pixels.</summary>
    public double Width { get; init; } = 1.25;

    /// <summary>Gets a value indicating whether the lines also appear on the floor.</summary>
    public bool OnFloor { get; init; } = true;

    /// <summary>Gets a value indicating whether the walls show cuts through the surface.</summary>
    public bool OnWalls { get; init; }

    /// <summary>Creates lines at even distances.</summary>
    /// <param name="range">The range of the heights.</param>
    /// <param name="count">The number of lines.</param>
    /// <returns>The lines, none if the range is empty or the count is not positive.</returns>
    public static Contours3D Even(DoubleRange range, int count)
    {
        var levels = new double[Math.Max(range.IsEmpty ? 0 : count, 0)];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = range.Min + ((range.Max - range.Min) * (i + 1) / (levels.Length + 1));
        }

        return new Contours3D(levels);
    }
}

/// <summary>
/// A surface over a raster: the value of a cell is the height and picks the color. <see cref="float.NaN"/> leaves a hole.
/// Changing the raster from any thread updates the picture.
/// </summary>
/// <param name="data">The raster with one value per place.</param>
/// <param name="x">The range of the columns on the first axis.</param>
/// <param name="y">The range of the rows on the second axis.</param>
public sealed class Surface3D(HeatmapData data, DoubleRange x, DoubleRange y)
{
    /// <summary>The height that marks a hole for the GPU.</summary>
    internal const float MissingHeight = 1e30f;

    /// <summary>Gets the raster.</summary>
    public HeatmapData Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets the range of the columns.</summary>
    public DoubleRange X { get; } = x;

    /// <summary>Gets the range of the rows.</summary>
    public DoubleRange Y { get; } = y;

    /// <summary>Gets the colors.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Viridis;

    /// <summary>Gets the values at the ends of the colormap, or <see langword="null"/> for the range of the data.</summary>
    public DoubleRange? ColorRange { get; init; }

    /// <summary>Gets how the surface takes the light.</summary>
    public Material3D Material { get; init; } = new();

    /// <summary>Gets the height lines, or <see langword="null"/> for none.</summary>
    public Contours3D? Contours { get; init; }

    /// <summary>Maps the raster to heights in the box.</summary>
    /// <param name="axis">The third axis.</param>
    /// <param name="heights">Receives one height per place.</param>
    internal void WriteHeights(AxisBase axis, float[] heights)
    {
        var transform = axis.CreateTransform(-1, 2);
        var (raster, sign, columns) = (Data, axis.IsHorizontal ? 1 : -1, Data.Width);
        Parallel.For(0, Data.Height, row =>
        {
            for (var column = 0; column < columns; column++)
            {
                var value = raster[column, row];
                var place = sign * transform.ToPixel(value);
                heights[(row * columns) + column] = double.IsFinite(value) && double.IsFinite(place) ? (float)place : MissingHeight;
            }
        });
    }
}
