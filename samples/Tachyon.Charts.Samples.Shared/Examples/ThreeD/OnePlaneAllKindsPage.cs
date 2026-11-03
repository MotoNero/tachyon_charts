using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Clouds3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „One plane, all kinds“ of the showroom.</summary>
public static class OnePlaneAllKindsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "One plane, all kinds";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the orange outline: move the plane | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Describes a plane that cuts a line, a cloud and columns at the same time.</summary>
    /// <param name="chart">The chart.</param>
    public static void CutTypes(Chart3DBuilder chart)
    {
        var path = new Points3D(4000);
        for (var i = 0; i < path.Capacity; i++)
        {
            var turn = i * 0.01;
            path.Append(Math.Cos(turn) * 0.8, Math.Sin(turn) * 0.8, (i / 2000.0) - 1, Tachyon.Charts.Core.Colors.Colormaps.Inferno.Sample(i / 4000.0));
        }

        Cloud(chart, 3000);
        var data = new Tachyon.Charts.Core.Data.HeatmapData(8, 8);
        var values = new float[64];
        for (var i = 0; i < 64; i++)
        {
            values[i] = -1 + (0.05f * (i % 8)) + (0.05f * (i / 8));
        }

        data.SetValues(values);
        chart.Title("One plane cuts every kind of data")
            .AddLine(new Line3D(path) { Width = 2.5 })
            .AddBars(new Bars3D(data, new DoubleRange(-1, 1), new DoubleRange(-1, 1)) { Baseline = -1 })
            .AddPlane(new ClipPlane3D(new Point3D(-1, -0.4, 0), -0.1));
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => CutTypes(c);
}
