using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Model with measuring points“ of the showroom: a pipe bend read from an STL file, colored by the temperature of its wall.</summary>
public static class ModelWithMeasuringPointsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Model with measuring points";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the orange outline: move the plane | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        var model = Models3D.ReadPipe();
        var values = new double[model.Positions.Length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Models3D.Temperature(model.Positions[i]);
        }

        var mesh = model.ToMesh(values, Colormaps.Turbo, new DoubleRange(55, 115), new Material3D { Gloss = 0.3 }, "°C");
        model.AddToScale(c.Title("Pipe bend, wall temperature in °C"), mesh);

        var sensors = Models3D.Sensors(9);
        var points = new Points3D(sensors.Length);
        foreach (var place in sensors)
        {
            points.Append(place.X, place.Y - 1, place.Z, new Rgba8(255, 255, 255, 255), 10);
        }

        c.AddScatter(new Scatter3D(points)).AddPlane(new ClipPlane3D(new Point3D(0, 1, 0), -0.05));
    }
}
