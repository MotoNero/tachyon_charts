using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Coupled3D“ of the snapshot tests.</summary>
public static class Coupled3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Coupled3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Coupled(c);

    /// <summary>Describes a surface whose first axis a 2D chart has narrowed, with the cursor of the group as a frame.</summary>
    /// <param name="chart">The chart.</param>
    public static void Coupled(Chart3DBuilder chart)
    {
        chart.Title("Narrowed by a 2D chart").XAxis(-0.5, 0.5, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddSurface(CreateHillSurface());
        chart.Scene.Cursor = 0.2;
    }
}
