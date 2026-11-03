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

/// <summary>The 3D page „Volume slices 512³“ of the showroom.</summary>
public static class VolumeSlices512Page
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Volume slices 512³";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the orange outline: move the plane | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Describes a volume with its three slices.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="side">The number of values per side.</param>
    public static void Slices(Chart3DBuilder chart, int side)
    {
        var volume = new Volume3D(CreateBody(side), new DoubleRange(-1, 1), new DoubleRange(-1, 1), new DoubleRange(-1, 1)) { SliceThreshold = 0.04 };
        chart.Title("Slices through a body").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddVolume(volume);
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Slices(c, 512);
}
