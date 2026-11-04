using Tachyon.Charts.Core.Fluent;
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
    /// <exception cref="InvalidOperationException">The 3D chart was not built yet.</exception>
    public static void Companion(ChartBuilder c)
    {
        var cut = LastCut ?? throw new InvalidOperationException("Build the 3D chart first, the 2D chart shows its cut.");
        c.Title("Height at the plane")
            .XAxis(a => a.Numeric("x").Range(-1, 1).Title("x"))
            .YAxis(a => a.Numeric("z").Range(-1, 1).Title("z"))
            .AddLine(cut.Data, s => s.Thickness(2))
            .AddOverlay(chart => CutMarkerOverlay.For(cut, chart))
            .Group(CutGroup)
            .Modifiers(m => m.Custom(() => new CutMarkerModifier(cut)).ZoomPan().Crosshair());
    }
}
