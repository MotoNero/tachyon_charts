using Tachyon.Charts.Core.Fluent;

namespace Tachyon.Charts.Geo;

/// <summary>Maps for the fluent description of a chart.</summary>
public static class GeoCharts
{
    /// <summary>
    /// Describes a map: X and Y axes over the world on the plane of the projection, without ticks, and the regions,
    /// by default the countries of <see cref="WorldMap"/> in three levels of detail. Data layers go on the same axes,
    /// their places projected with <see cref="GeoProjection.Project(ReadOnlySpan{double}, ReadOnlySpan{double})"/>.
    /// </summary>
    /// <param name="builder">The chart.</param>
    /// <param name="projection">The projection.</param>
    /// <param name="configure">Sets colors and borders.</param>
    /// <param name="levels">The levels of detail, coarse to fine; <see langword="null"/> for the countries of the world.</param>
    /// <returns>The chart.</returns>
    public static ChartBuilder Map(this ChartBuilder builder, GeoProjection projection, Action<GeoMapSeries>? configure = null, IReadOnlyList<GeoMap>? levels = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(projection);
        var (west, south, east, north) = projection.WorldBounds;
        return builder
            .XAxis(a => a.Numeric("x").Range(west, east).Hidden())
            .YAxis(a => a.Numeric("y").Range(south, north).Hidden())
            .AddSeries((x, y) =>
            {
                var map = new GeoMapSeries(levels ?? WorldMap.CountryLevels, projection, x, y);
                configure?.Invoke(map);
                return map;
            }, "x", "y");
    }
}
