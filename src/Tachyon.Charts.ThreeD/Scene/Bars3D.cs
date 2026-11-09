using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>A column as the GPU reads it. The order follows bars.wgsl.</summary>
/// <param name="Place">The middle in the box and half the width and depth.</param>
/// <param name="Low">The lower end in the box.</param>
/// <param name="High">The upper end in the box. It equals the lower end for a column that is not there.</param>
/// <param name="Color">The color.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct BarInstance(Vector4 Place, float Low, float High, Rgba8 Color);

/// <summary>Columns over a raster, with a color by value.</summary>
/// <param name="data">The values, one column per cell.</param>
/// <param name="x">The range that the cells fill from left to right.</param>
/// <param name="y">The range that the cells fill from front to back.</param>
public sealed class Bars3D(HeatmapData data, DoubleRange x, DoubleRange y)
{
    /// <summary>Gets a value indicating whether the values show as labels above their points; labels that would cover others are left out.</summary>
    public bool ShowsValues { get; init; }

    /// <summary>Gets the values.</summary>
    public HeatmapData Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets the range of the cells from left to right.</summary>
    public DoubleRange X { get; } = x;

    /// <summary>Gets the range of the cells from front to back.</summary>
    public DoubleRange Y { get; } = y;

    /// <summary>Gets the colors.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Viridis;

    /// <summary>Gets the values at the ends of the colormap, or <see langword="null"/> for the range of the data.</summary>
    public DoubleRange? ColorRange { get; init; }

    /// <summary>Gets how the columns take the light.</summary>
    public Material3D Material { get; init; } = new();

    /// <summary>Gets how much of a cell a column fills, from above 0 up to 1.</summary>
    public double Width { get; init; } = 0.8;

    /// <summary>Gets the value from which the columns rise.</summary>
    public double Baseline { get; init; }

    /// <summary>Gets the cells in the box.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The raster, whose lines are the borders of the cells.</returns>
    internal GridPlace Cells(Scene3D scene) => GridPlace.Create(scene, X, Y, Data.Width + 1, Data.Height + 1);

    /// <summary>Writes the columns.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="bars">Receives one column per value.</param>
    internal void Write(Scene3D scene, BarInstance[] bars)
    {
        var (place, columns) = (Cells(scene), Data.Width);
        var range = ColorRange ?? Data.ValueRange;
        var size = new Vector2((float)(place.StepX * Width / 2), (float)(place.StepY * Width / 2));
        var map = new BoxMap(scene);
        Parallel.For(0, Data.Height, row =>
        {
            for (var column = 0; column < columns; column++)
            {
                var center = new Vector2((float)(place.Left + ((column + 0.5) * place.StepX)), (float)(place.Front + ((row + 0.5) * place.StepY)));
                bars[(row * columns) + column] = Create(center, size, Data[column, row], range, map);
            }
        });
    }

    /// <summary>Gets the ends of a column in the box, cut off at the box.</summary>
    /// <param name="value">The value.</param>
    /// <param name="map">The way into the box.</param>
    /// <returns>Both ends. They are equal when there is nothing to see.</returns>
    internal (double Low, double High) GetEnds(double value, in BoxMap map)
    {
        if (!double.IsFinite(value))
        {
            return (0, 0);
        }

        var (a, b) = (map.ToBox(new Point3D(0, 0, value)).Z, map.ToBox(new Point3D(0, 0, Baseline)).Z);
        return (Math.Clamp(Math.Min(a, b), -1, 1), Math.Clamp(Math.Max(a, b), -1, 1));
    }

    private BarInstance Create(Vector2 center, Vector2 size, double value, DoubleRange range, in BoxMap map)
    {
        var (low, high) = GetEnds(value, map);
        var share = range.Max > range.Min ? (value - range.Min) / (range.Max - range.Min) : 0.5;
        return new BarInstance(new Vector4(center, size.X, size.Y), (float)low, (float)high, Colormap.Sample(share));
    }
}

/// <summary>The columns seen as a raster of cells.</summary>
/// <param name="bars">The columns.</param>
/// <param name="place">The cells in the box.</param>
/// <param name="map">The way into the box.</param>
internal readonly struct BarCells(Bars3D bars, in GridPlace place, in BoxMap map) : IGridCells
{
    private readonly GridPlace _place = place;
    private readonly BoxMap _map = map;

    /// <inheritdoc />
    public bool Hit(in Ray3D ray, int i, int j, double from, double to, out double distance)
    {
        distance = from;
        var (low, high) = bars.GetEnds(bars.Data[i, j], _map);
        if (high <= low)
        {
            return false;
        }

        var (halfX, halfY) = (Math.Abs(_place.StepX) * bars.Width / 2, Math.Abs(_place.StepY) * bars.Width / 2);
        var (x, y) = (_place.Left + ((i + 0.5) * _place.StepX), _place.Front + ((j + 0.5) * _place.StepY));
        var far = to;
        return Ray3D.Limit1(ray.Start.X, ray.Direction.X, x - halfX, x + halfX, ref distance, ref far)
            && Ray3D.Limit1(ray.Start.Y, ray.Direction.Y, y - halfY, y + halfY, ref distance, ref far)
            && Ray3D.Limit1(ray.Start.Z, ray.Direction.Z, low, high, ref distance, ref far);
    }
}
