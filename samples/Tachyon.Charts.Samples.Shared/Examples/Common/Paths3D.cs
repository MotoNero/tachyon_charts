
namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>The path of a particle that two 3D examples share.</summary>
public static class Paths3D
{
    /// <summary>Gets a step of the path of a particle.</summary>
    /// <param name="x">The first coordinate.</param>
    /// <param name="y">The second coordinate.</param>
    /// <param name="z">The third coordinate.</param>
    /// <returns>The next place.</returns>
    public static (double X, double Y, double Z) StepAttractor(double x, double y, double z)
    {
        const double Step = 0.002;
        return (x + (Step * 10 * (y - x)), y + (Step * ((x * (28 - z)) - y)), z + (Step * ((x * y) - (8.0 / 3 * z))));
    }
}
