using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Cuts3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Cut, live“ of the showroom.</summary>
public static class CutLivePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Cut, live";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the orange outline: the curve below follows | Click in 3D or Ctrl+click below: marker in both, on a marker removes it | Delete: newest marker | Drag: turn";

    private const string CutGroup = "cut-3d";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => SurfaceWithCut(c);

    /// <summary>Builds the 2D chart below the 3D chart.</summary>
    /// <param name="c">The 2D chart.</param>
    public static void Companion(ChartBuilder c) => c
        .Title("Height at the plane")
        .XAxis(a => a.Numeric("x").Range(-1, 1).Title("x"))
        .YAxis(a => a.Numeric("z").Range(-1, 1).Title("z"))
        .AddLine(LastCut!.Data, s => s.Thickness(2))
        .AddOverlay(chart => CutMarkerOverlay.For(LastCut!, chart))
        .Group(CutGroup)
        .Modifiers(m => m.Custom(() => new CutMarkerModifier(LastCut!)).ZoomPan().Crosshair());
}
