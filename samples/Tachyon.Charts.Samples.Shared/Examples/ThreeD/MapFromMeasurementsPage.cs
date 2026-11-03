using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Map from measurements“ of the showroom.</summary>
public static class MapFromMeasurementsPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Map from measurements";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | Pointer: values | Click: marker, Delete removes it";

    /// <summary>Describes a map of an engine from uneven measurements.</summary>
    /// <param name="chart">The chart.</param>
    public static void Map(Chart3DBuilder chart)
    {
        const int Count = 160;
        var random = new Random(4);
        var (speed, load, temperature) = (new double[Count], new double[Count], new double[Count]);
        for (var i = 0; i < Count; i++)
        {
            speed[i] = 800 + (5200 * random.NextDouble());
            load[i] = 10 + (90 * random.NextDouble() * random.NextDouble());
            var (a, b) = (speed[i] / 6000, load[i] / 100);
            temperature[i] = 70 + (40 * a * b) + (15 * Math.Sin(a * 5) * b);
        }

        chart.Title("Temperature over speed and load")
            .XAxis(0, 6000, "Speed in rpm")
            .YAxis(0, 100, "Load in %")
            .ZAxis(60, 120, "Temperature in °C")
            .AddMap(new MeasuredMap3D(speed, load, temperature));
    }
    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => Map(c);
}
