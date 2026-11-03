using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Paths3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Path of 200 000 points“ of the showroom.</summary>
public static class PathOf200000PointsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Path of 200 000 points";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    /// <summary>Describes the path of a particle with a color along the way.</summary>
    /// <param name="chart">The chart.</param>
    public static void Trajectory(Chart3DBuilder chart)
    {
        const int Count = 200_000;
        var points = new Points3D(Count);
        var (x, y, z) = (1.0, 1.0, 20.0);
        for (var i = 0; i < Count; i++)
        {
            (x, y, z) = StepAttractor(x, y, z);
            points.Append(x, y, z, Tachyon.Charts.Core.Colors.Colormaps.Viridis.Sample(i / (double)Count));
        }

        chart.Title("Path of a particle").XAxis(-25, 25, "x").YAxis(-30, 30, "y").ZAxis(0, 55, "z").AddLine(new Line3D(points));
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Trajectory(c);
}
