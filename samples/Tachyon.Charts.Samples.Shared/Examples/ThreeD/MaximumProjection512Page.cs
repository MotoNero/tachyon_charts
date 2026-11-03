using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Volumes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Maximum projection 512³“ of the showroom.</summary>
public static class MaximumProjection512Page
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Maximum projection 512³";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    /// <summary>Describes the highest value along each ray, as a scanner shows vessels or bright spots.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="side">The number of values per side.</param>
    public static void Maximum(Chart3DBuilder chart, int side)
    {
        var volume = new Volume3D(CreateBody(side), new DoubleRange(-1, 1), new DoubleRange(-1, 1), new DoubleRange(-1, 1))
        {
            Mode = VolumeMode.Maximum,
            ShowsSlices = false,
            SliceThreshold = 0.05,
            Colormap = Tachyon.Charts.Core.Colors.Colormaps.Inferno,
        };
        chart.Title("Highest value along each ray").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddVolume(volume);
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Maximum(c, 512);
}
