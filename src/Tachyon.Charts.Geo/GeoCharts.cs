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

    /// <summary>
    /// Adds tiles of a server over a map of <see cref="GeoProjection.WebMercator"/>, with the notice of their source in
    /// the corner. Put a <see cref="Map"/> of the same projection below it: where no tile is, without a network for
    /// instance, its countries show.
    /// </summary>
    /// <param name="builder">The chart.</param>
    /// <param name="source">Where the tiles come from.</param>
    /// <returns>The chart.</returns>
    public static ChartBuilder Tiles(this ChartBuilder builder, TileSource source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);
        return builder
            .AddSeries((x, y) => new TileLayerSeries(source, x, y), "x", "y")
            .AddOverlay(_ => new AttributionOverlay(source.Attribution));
    }
}
