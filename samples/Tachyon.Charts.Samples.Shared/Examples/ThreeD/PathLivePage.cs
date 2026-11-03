using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Paths3D;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Path, live“ of the showroom.</summary>
public static class PathLivePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Path, live";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    private const int PathStep = 200;
    private static readonly Points3D _livePath = new(100_000);
    private static Timer? _pathTimer;
    private static (double X, double Y, double Z) _pathPlace = (1, 1, 20);

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => c.Title("Path of a particle, live").XAxis(-25, 25, "x").YAxis(-30, 30, "y").ZAxis(0, 55, "z").AddLine(new Line3D(_livePath));

    /// <summary>Starts or stops the live data.</summary>
    /// <param name="isRunning">Whether the page is shown.</param>
    public static void Run(bool isRunning) => RunPath(isRunning);

    private static void RunPath(bool isRunning)
    {
        _pathTimer?.Dispose();
        _pathTimer = isRunning ? new Timer(_ => AddPath(), null, 0, 30) : null;
    }
    private static void AddPath()
    {
        for (var i = 0; i < PathStep; i++)
        {
            _pathPlace = StepAttractor(_pathPlace.X, _pathPlace.Y, _pathPlace.Z);
            _livePath.Append(_pathPlace.X, _pathPlace.Y, _pathPlace.Z, Tachyon.Charts.Core.Colors.Colormaps.Viridis.Sample((_livePath.Total % 100_000) / 100_000.0));
        }
    }
}
