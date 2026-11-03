using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.MapFromMeasurementsPage;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Map3D“ of the snapshot tests.</summary>
public static class Map3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Map3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Map(c);
}
