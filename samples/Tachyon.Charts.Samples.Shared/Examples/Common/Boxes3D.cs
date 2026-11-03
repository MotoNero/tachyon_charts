using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>The empty axis box that several 3D pictures show.</summary>
public static class Boxes3D
{
    public static void Box(Chart3DBuilder chart) => chart
        .Title("Axis box")
        .XAxis(0, 10, "Time in s")
        .YAxis(-5, 5, "Offset in mm")
        .ZAxis(0, 100, "Level in dB");
}
