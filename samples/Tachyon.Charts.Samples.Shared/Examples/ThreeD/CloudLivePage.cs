using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Cloud, live“ of the showroom.</summary>
public static class CloudLivePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Cloud, live";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    private const int CloudStep = 30_000;
    private static readonly Points3D _liveCloud = new(5_000_000);
    private static readonly double[][] _cloudBatch = [new double[CloudStep], new double[CloudStep], new double[CloudStep]];
    private static readonly Rgba8[] _cloudColors = new Rgba8[CloudStep];
    private static readonly Random _cloudRandom = new(2);
    private static Timer? _cloudTimer;
    // A sweep that scans along the first axis, so the oldest points visibly fall out at the far end.
    private static void AddCloud()
    {
        var sweep = _liveCloud.Total / 5_000_000.0 % 1;
        for (var i = 0; i < CloudStep; i++)
        {
            var (a, b) = ((sweep * 2) - 1 + (_cloudRandom.NextDouble() * 0.02), (_cloudRandom.NextDouble() * 2) - 1);
            var height = 0.5 * Math.Sin(a * 4) * Math.Cos(b * 3);
            (_cloudBatch[0][i], _cloudBatch[1][i], _cloudBatch[2][i]) = (a, b, height);
            _cloudColors[i] = Tachyon.Charts.Core.Colors.Colormaps.Viridis.Sample(height + 0.5);
        }

        _liveCloud.Append(_cloudBatch[0], _cloudBatch[1], _cloudBatch[2], _cloudColors);
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => c.Title("A million points per second").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddScatter(new Scatter3D(_liveCloud) { Size = 1.5 });

    /// <summary>Starts or stops the live data.</summary>
    /// <param name="isRunning">Whether the page is shown.</param>
    public static void Run(bool isRunning) => RunCloud(isRunning);

    private static void RunCloud(bool isRunning)
    {
        _cloudTimer?.Dispose();
        _cloudTimer = isRunning ? new Timer(_ => AddCloud(), null, 0, 30) : null;
    }
}
