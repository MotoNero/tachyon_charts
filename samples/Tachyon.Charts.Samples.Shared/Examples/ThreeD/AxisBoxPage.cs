using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Axis box“ of the showroom.</summary>
public static class AxisBoxPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Axis box";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => c.Title("Axis box").XAxis(0, 10, "Time in s").YAxis(-5, 5, "Offset in mm").ZAxis(0, 100, "Level in dB");
}
