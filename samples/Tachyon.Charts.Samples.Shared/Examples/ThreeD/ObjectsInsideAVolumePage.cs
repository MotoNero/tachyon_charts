using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Volumes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Objects inside a volume“ of the showroom.</summary>
public static class ObjectsInsideAVolumePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Objects inside a volume";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the white points at the top right: color and opacity | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Describes a volume with a surface and a line that run through it.</summary>
    /// <param name="chart">The chart.</param>
    public static void VolumeWithObjects(Chart3DBuilder chart)
    {
        LookThrough(chart, 128);
        var path = new Points3D(400);
        for (var i = 0; i < path.Capacity; i++)
        {
            var turn = i * 0.05;
            path.Append(Math.Cos(turn) * 0.5, Math.Sin(turn) * 0.5, (i / 200.0) - 1, new Tachyon.Charts.Rendering.Rgba8(90, 240, 120, 255));
        }

        chart.Title("Objects inside a volume").AddLine(new Line3D(path) { Width = 3 }).AddMesh(CreateSurface((x, y) => -0.3 + (0.2 * x * y), Tint, new Material3D()));
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => VolumeWithObjects(c);
}
