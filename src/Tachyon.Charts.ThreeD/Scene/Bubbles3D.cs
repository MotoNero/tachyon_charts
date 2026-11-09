using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>How the diameters of bubbles are given.</summary>
public enum BubbleSizeUnit
{
    /// <summary>In units of the first axis, so that a bubble grows when the view zooms in. Bubbles stay round when the other axes have other scales.</summary>
    Data,

    /// <summary>In device-independent units on the screen, so that a bubble keeps its size when the view zooms.</summary>
    Screen,
}

/// <summary>
/// Points in space as lit spheres, each with a diameter of its own. A million bubbles turn at the frame rate of the
/// screen: each one is a square that faces the eye, and the graphics card finds the sphere inside it per pixel. Bubbles
/// are always opaque. A bubble whose place or diameter is not finite is left out.
/// </summary>
public sealed class Bubbles3D
{
    /// <summary>Gets a value indicating whether the values show as labels above their points; labels that would cover others are left out.</summary>
    public bool ShowsValues { get; init; }

    /// <summary>Initializes a new instance of the <see cref="Bubbles3D"/> class.</summary>
    /// <param name="x">The first coordinates.</param>
    /// <param name="y">The second coordinates.</param>
    /// <param name="z">The third coordinates.</param>
    /// <param name="diameters">The diameters, in the unit of <see cref="SizeUnit"/>. <see cref="ScaleToVolume"/> makes them from values.</param>
    /// <exception cref="ArgumentException">The lists differ in length.</exception>
    public Bubbles3D(double[] x, double[] y, double[] z, double[] diameters)
    {
        X = x ?? throw new ArgumentNullException(nameof(x));
        Y = y ?? throw new ArgumentNullException(nameof(y));
        Z = z ?? throw new ArgumentNullException(nameof(z));
        Diameters = diameters ?? throw new ArgumentNullException(nameof(diameters));
        if (y.Length != x.Length || z.Length != x.Length || diameters.Length != x.Length)
        {
            throw new ArgumentException($"The lists hold {x.Length}, {y.Length}, {z.Length} and {diameters.Length} entries, they need the same number.", nameof(diameters));
        }
    }

    /// <summary>Gets the first coordinates.</summary>
    public double[] X { get; }

    /// <summary>Gets the second coordinates.</summary>
    public double[] Y { get; }

    /// <summary>Gets the third coordinates.</summary>
    public double[] Z { get; }

    /// <summary>Gets the diameters.</summary>
    public double[] Diameters { get; }

    /// <summary>Gets the unit of the diameters.</summary>
    public BubbleSizeUnit SizeUnit { get; init; } = BubbleSizeUnit.Data;

    /// <summary>Gets the color of the bubbles without one of their own.</summary>
    public Rgba8 Color { get; init; } = new(90, 170, 255, 255);

    /// <summary>Gets a color per bubble, or <see langword="null"/> for <see cref="Color"/>.</summary>
    /// <exception cref="ArgumentException">The number of colors differs from the number of bubbles.</exception>
    public Rgba8[]? Colors
    {
        get;
        init => field = value is null || value.Length == X.Length ? value : throw new ArgumentException($"There are {value.Length} colors for {X.Length} bubbles.", nameof(value));
    }

    /// <summary>Gets a value per bubble that the pointer shows, or <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException">The number of values differs from the number of bubbles.</exception>
    public double[]? Values
    {
        get;
        init => field = value is null || value.Length == X.Length ? value : throw new ArgumentException($"There are {value.Length} values for {X.Length} bubbles.", nameof(value));
    }

    /// <summary>Gets how the bubbles take the light. The opacity is not used, bubbles are always opaque.</summary>
    public Material3D Material { get; init; } = new() { Gloss = 0.35, Sharpness = 40 };

    /// <summary>Gets the number of bubbles.</summary>
    public int Count => X.Length;

    /// <summary>
    /// Makes diameters from values so that the volume of a bubble grows with its value: the largest value gets
    /// <paramref name="largest"/>, and no bubble gets smaller than <paramref name="smallest"/>. Values that are zero,
    /// negative or not finite give no bubble.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <param name="smallest">The smallest diameter.</param>
    /// <param name="largest">The diameter of the largest value.</param>
    /// <returns>The diameters.</returns>
    public static double[] ScaleToVolume(ReadOnlySpan<double> values, double smallest, double largest)
    {
        var most = 0.0;
        foreach (var value in values)
        {
            if (double.IsFinite(value) && value > most)
            {
                most = value;
            }
        }

        var diameters = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            diameters[i] = double.IsFinite(value) && value > 0 ? Math.Max(smallest, largest * Math.Cbrt(value / most)) : double.NaN;
        }

        return diameters;
    }
}
