using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Volumes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Surface of equal value 512³“ of the showroom.</summary>
public static class SurfaceOfEqualValue512Page
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Surface of equal value 512³";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the white bar at the top right: value of the surface | Drag the orange outline: move the plane | Drag: turn | Wheel: zoom";

    /// <summary>Describes the surface of equal value inside a volume, cut open by a plane.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="side">The number of values per side.</param>
    public static void IsoSurface(Chart3DBuilder chart, int side)
    {
        var data = CreateBody(side);
        var volume = new Volume3D(data, new DoubleRange(-1, 1), new DoubleRange(-1, 1), new DoubleRange(-1, 1))
        {
            Mode = VolumeMode.Surface,
            ShowsSlices = false,
            Colormap = Tachyon.Charts.Core.Colors.Colormaps.Viridis,
        };
        volume.IsoValue = data.ValueRange.Min + (0.45 * (data.ValueRange.Max - data.ValueRange.Min));
        chart.Title("Surface of equal value").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
            .AddVolume(volume)
            .AddPlane(new ClipPlane3D(new Point3D(1, 1, 0), -0.1));
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => IsoSurface(c, 512);
}
