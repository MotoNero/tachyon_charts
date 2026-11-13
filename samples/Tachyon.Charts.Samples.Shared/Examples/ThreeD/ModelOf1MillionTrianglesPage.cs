using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Model of 1 million triangles“ of the showroom: a knot of tube, colored by height.</summary>
public static class ModelOf1MillionTrianglesPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Model of 1 million triangles";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        var model = Models3D.CreateKnot();
        var heights = new double[model.Positions.Length];
        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = model.Positions[i].Z;
        }

        var mesh = model.ToMesh(heights, Colormaps.Viridis, new DoubleRange(-1.4, 1.4), new Material3D { Gloss = 0.4, Sharpness = 48 }, "Height in m");
        model.AddToScale(c.Title("Knot of tube, colored by height"), mesh, "m");
    }
}
