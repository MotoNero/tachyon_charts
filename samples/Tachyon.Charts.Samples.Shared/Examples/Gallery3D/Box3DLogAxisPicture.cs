using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Boxes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Box3DLogAxis“ of the snapshot tests.</summary>
public static class Box3DLogAxisPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Box3DLogAxis";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        Box(c);
        c.ZAxis(new LogAxis("z3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(1, 10000), Title = "Power in W" });
    }
}
