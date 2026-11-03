using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Clouds3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Cloud of 100 million points“ of the showroom.</summary>
public static class CloudOf100MillionPointsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Cloud of 100 million points";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Landscape(c, 100_000_000);
}
