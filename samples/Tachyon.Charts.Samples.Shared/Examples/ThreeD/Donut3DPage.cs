using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Donut 3D“ of the showroom: Power mix as a lit ring.</summary>
public static class Donut3DPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Donut 3D";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Click on a slice: pull it out or back | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => c.Title("Power mix").AddPie(new Pie3D(PieData.Values, PieData.Names) { InnerRadius = 0.55, Height = 0.25 });
}
