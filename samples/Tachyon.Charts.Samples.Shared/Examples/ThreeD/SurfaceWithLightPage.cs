using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Surface with light“ of the showroom.</summary>
public static class SurfaceWithLightPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Surface with light";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Hill(c, new Tachyon.Charts.ThreeD.Scene.Material3D());
}
