using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Volumes3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Through a volume 512³“ of the showroom.</summary>
public static class ThroughAVolume512Page
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Through a volume 512³";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the white points at the top right: color and opacity | Drag: turn | Wheel: zoom | Pointer: values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => LookThrough(c, 512);
}
