using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Geo;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „World map“ of the showroom: the countries of the world without the internet, colored by how far
/// they lie from the equator, and capitals as a layer of points. The map becomes finer as the view comes closer.
/// </summary>
public static class WorldMapPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "World map";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all";

    /// <summary>Gets the capitals of the point layer: name, longitude and latitude in degrees.</summary>
    public static IReadOnlyList<(string Name, double Longitude, double Latitude)> Capitals { get; } =
    [
        ("Berlin", 13.40, 52.52), ("Paris", 2.35, 48.86), ("Madrid", -3.70, 40.42), ("Rome", 12.50, 41.90),
        ("Moscow", 37.62, 55.76), ("Cairo", 31.24, 30.04), ("Nairobi", 36.82, -1.29), ("Pretoria", 28.19, -25.75),
        ("New Delhi", 77.21, 28.61), ("Beijing", 116.41, 39.90), ("Tokyo", 139.69, 35.69), ("Canberra", 149.13, -35.28),
        ("Brasília", -47.88, -15.79), ("Buenos Aires", -58.38, -34.60), ("Mexico City", -99.13, 19.43), ("Washington", -77.04, 38.91),
        ("Ottawa", -75.70, 45.42), ("Jakarta", 106.85, -6.21), ("Wellington", 174.78, -41.29), ("Reykjavík", -21.94, 64.15),
    ];

    /// <summary>Gets the color of each country: how far the middle of its box lies from the equator, on Viridis.</summary>
    public static IReadOnlyDictionary<string, Rgba8> Colors { get; } = WorldMap.Countries(MapDetail.Coarse).Regions
        .Where(region => region.Code.Length > 0)
        .GroupBy(region => region.Code)
        .ToDictionary(group => group.Key, group => Colormaps.Viridis.Sample(Math.Abs((group.First().Bounds.North + group.First().Bounds.South) / 2) / 70));

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var projection = GeoProjection.EqualEarth;
        var (x, y) = projection.Project([.. Capitals.Select(capital => capital.Longitude)], [.. Capitals.Select(capital => capital.Latitude)]);
        var capitals = new DataSource<double, double>(x.Length);
        capitals.Append(x, y);
        c.Title(SampleText.T("Countries by distance from the equator"))
            .Map(projection, map => map.SetColors(Colors))
            .AddScatter(capitals, s => s.Name(SampleText.T("Capitals")).Fill(new Rgba8(230, 60, 60, 255)).Size(7))
            .Modifiers(m => m.ZoomPan().RubberBand().Tooltip());
    }
}
