using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.ChartTypeData;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Antenna cut“ of the showroom.</summary>
public static class AntennaCutPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Antenna cut";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = Radial;

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => Polar(c, 40, Degrees, (x, y) => new PolarSeries(Common.Antenna3D.Antenna.AzimuthCut(20).Angles, Common.Antenna3D.Antenna.AzimuthCut(20).Values, x, y));
}
