using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Geo;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator.Geo;

/// <summary>
/// Maps of the geo package: the countries of the world or the areas of a shapefile in a projection, with colors for
/// land, sea and borders or by the distance from the equator; and the capitals as a layer of points.
/// </summary>
public sealed class GeoModule : IChartModule, IDataLoaders
{
    /// <summary>The version of the geo package the code refers to: the version of the 2D packages.</summary>
    public const string PackageVersion = "0.15.0";

    private const string ByLatitude = "Distance from the equator";
    private const string OneColor = "One color";

    private static readonly (string Name, double Longitude, double Latitude)[] _capitals =
    [
        ("Berlin", 13.40, 52.52), ("Paris", 2.35, 48.86), ("Madrid", -3.70, 40.42), ("Rome", 12.50, 41.90),
        ("Moscow", 37.62, 55.76), ("Cairo", 31.24, 30.04), ("Nairobi", 36.82, -1.29), ("Pretoria", 28.19, -25.75),
        ("New Delhi", 77.21, 28.61), ("Beijing", 116.41, 39.90), ("Tokyo", 139.69, 35.69), ("Canberra", 149.13, -35.28),
        ("Brasília", -47.88, -15.79), ("Buenos Aires", -58.38, -34.60), ("Mexico City", -99.13, 19.43), ("Washington", -77.04, 38.91),
        ("Ottawa", -75.70, 45.42), ("Jakarta", 106.85, -6.21), ("Wellington", 174.78, -41.29), ("Reykjavík", -21.94, 64.15),
    ];

    /// <inheritdoc />
    public IReadOnlyList<ChartTypeInfo> Types { get; } =
    [
        new("geoMap", "World map", ChartCategory.TwoD, DataShape.GeoMap, "world",
            [
                new("projection", "Projection", ParameterKind.Choice, "EqualEarth") { Choices = ["EqualEarth", "WebMercator", "Equirectangular"] },
                new("colors", "Colors of the countries", ParameterKind.Choice, OneColor) { Choices = [OneColor, ByLatitude] },
                new("colormap", "Colormap", ParameterKind.Choice, "Viridis") { Choices = ["Viridis", "Inferno", "Magma", "Plasma", "Turbo"] },
                new("land", "Land", ParameterKind.Color, string.Empty) { Description = "Empty for the usual pale green." },
                new("sea", "Sea", ParameterKind.Color, string.Empty) { Description = "Empty for the usual pale blue." },
                new("border", "Borders", ParameterKind.Color, string.Empty) { Description = "Empty for the usual gray." },
                new("borderThickness", "Border thickness", ParameterKind.Number, 0.6) { Minimum = 0, Maximum = 5 },
            ], Apply, Code)
        {
            Usings = ["Tachyon.Charts.Geo", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.Rendering"],
            Packages = [("Tachyon.Charts.Geo", PackageVersion)],
        },
    ];

    /// <inheritdoc />
    public IReadOnlyList<SampleInfo> Samples { get; } =
    [
        new("world", "The countries of the world (Natural Earth)", DataShape.GeoMap, 1, _ => WorldMap.CountryLevels, "IReadOnlyList<GeoMap>", WorldCode) { HasCount = false },
        new("capitals", "Twenty capitals on the Equal Earth map", DataShape.Points, 20, _ => Capitals(), "DataSource<double, double>", CapitalsCode) { HasCount = false },
    ];

    /// <inheritdoc />
    public IReadOnlyList<PageInfo> Pages { get; } = [new("World map", WorldPage)];

    /// <inheritdoc />
    IReadOnlyList<DataLoader> IDataLoaders.Loaders { get; } =
    [
        new(DataKind.File, DataShape.GeoMap,
            series => (IReadOnlyList<GeoMap>)[GeoMap.FromShapefile(ShapefileReader.Read(series.Data.Path!))],
            ShapefileCode,
            series => series.Data.Path is { Length: > 0 } ? null : $"Choose the shapefile (.shp) of {series.Name}.",
            [("Tachyon.Charts.Geo", true)]),
    ];

    private static string WorldCode(string method) => SampleCatalog.Header("the countries of the world from Natural Earth in three levels of detail", method, "IReadOnlyList<GeoMap>", "Not used.") + """
                => WorldMap.CountryLevels;

        """;

    private static string CapitalsCode(string method) => SampleCatalog.Header("twenty capitals, projected for the Equal Earth map", method, "DataSource<double, double>", "Not used.") +
        "        {\n" +
        $"            double[] longitudes = [{string.Join(", ", _capitals.Select(capital => CodeText.Number(capital.Longitude)))}];\n" +
        $"            double[] latitudes = [{string.Join(", ", _capitals.Select(capital => CodeText.Number(capital.Latitude)))}];\n" +
        "            var (x, y) = GeoProjection.EqualEarth.Project(longitudes, latitudes);\n" +
        "            var data = new DataSource<double, double>(x.Length);\n" +
        "            data.Append(x, y);\n" +
        "            return data;\n" +
        "        }\n\n";

    private static DataCode ShapefileCode(SeriesSpec series, string method) => new(
        "IReadOnlyList<GeoMap>",
        method,
        method + "()",
        "\n" +
        $"        /// <summary>Reads the areas of {System.Security.SecurityElement.Escape(series.Name)} from a shapefile; the table beside it gives the names.</summary>\n" +
        "        /// <returns>The map, one level of detail.</returns>\n" +
        $"        public static IReadOnlyList<GeoMap> {method}() => [GeoMap.FromShapefile(ShapefileReader.Read({CodeText.String(series.Data.Path ?? string.Empty)}))];\n\n",
        ["Tachyon.Charts.Geo"]);

    private static DataSource<double, double> Capitals()
    {
        var (x, y) = GeoProjection.EqualEarth.Project([.. _capitals.Select(capital => capital.Longitude)], [.. _capitals.Select(capital => capital.Latitude)]);
        var data = new DataSource<double, double>(x.Length);
        data.Append(x, y);
        return data;
    }

    private static GeoProjection ProjectionOf(SpecValues values) => values.GetText("projection", "EqualEarth") switch
    {
        "WebMercator" => GeoProjection.WebMercator,
        "Equirectangular" => GeoProjection.Equirectangular,
        _ => GeoProjection.EqualEarth,
    };

    private static Colormap ColormapOf(string name) => typeof(Colormaps).GetProperty(name)?.GetValue(null) as Colormap ?? Colormaps.Viridis;

    // Each country by how far the middle of its box lies from the equator, on the colormap.
    private static Dictionary<string, Rgba8> ByDistance(GeoMap map, Colormap colormap) => map.Regions
        .Where(region => region.Code.Length > 0)
        .GroupBy(region => region.Code)
        .ToDictionary(group => group.Key, group => colormap.Sample(Math.Abs((group.First().Bounds.North + group.First().Bounds.South) / 2) / 70));

    private static void Apply(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var levels = (IReadOnlyList<GeoMap>)data;
        chart.AddSeries((x, y) => Style(new GeoMapSeries(levels, ProjectionOf(values), x, y), levels, values), axes.X, axes.Y);
    }

    private static GeoMapSeries Style(GeoMapSeries map, IReadOnlyList<GeoMap> levels, SpecValues values)
    {
        {
            if (values.GetText("colors", OneColor) == ByLatitude)
            {
                map.SetColors(ByDistance(levels[0], ColormapOf(values.GetText("colormap", "Viridis"))));
            }

            if (SpecColor.TryParse(values.GetText("land", string.Empty), out var land))
            {
                map.Land = land;
            }

            if (SpecColor.TryParse(values.GetText("sea", string.Empty), out var sea))
            {
                map.Sea = sea;
            }

            if (SpecColor.TryParse(values.GetText("border", string.Empty), out var border))
            {
                map.Border = border;
            }

            map.BorderThickness = values.GetNumber("borderThickness", 0.6);
        }

        return map;
    }

    private static string Code(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var settings = new List<string>();
        if (values.GetText("colors", OneColor) == ByLatitude)
        {
            settings.Add($"map.SetColors({data}[0].Regions.Where(region => region.Code.Length > 0).GroupBy(region => region.Code).ToDictionary(group => group.Key, group => Colormaps.{values.GetText("colormap", "Viridis")}.Sample(Math.Abs((group.First().Bounds.North + group.First().Bounds.South) / 2) / 70)));");
        }

        foreach (var (key, property) in new[] { ("land", "Land"), ("sea", "Sea"), ("border", "Border") })
        {
            if (SpecColor.TryParse(values.GetText(key, string.Empty), out var color))
            {
                settings.Add($"map.{property} = {CodeText.Color(color)};");
            }
        }

        settings.Add($"map.BorderThickness = {CodeText.Number(values.GetNumber("borderThickness", 0.6))};");
        return $".AddSeries((x, y) => {{ var map = new GeoMapSeries({data}, GeoProjection.{values.GetText("projection", "EqualEarth")}, x, y); {string.Join(" ", settings)} return map; }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    // The page of the showroom: the countries by their distance from the equator and the capitals.
    private static ChartSpec WorldPage(ChartCatalog catalog)
    {
        var spec = catalog.CreateSpec("geoMap");
        spec.Title = "Countries by distance from the equator";

        // The axes of the map: hidden, over the world on the plane of the projection.
        var (west, south, east, north) = GeoProjection.EqualEarth.WorldBounds;
        (spec.XAxes[0].IsHidden, spec.XAxes[0].Minimum, spec.XAxes[0].Maximum, spec.XAxes[0].Title) = (true, west, east, string.Empty);
        (spec.YAxes[0].IsHidden, spec.YAxes[0].Minimum, spec.YAxes[0].Maximum, spec.YAxes[0].Title) = (true, south, north, string.Empty);
        spec.Modifiers.Clear();
        spec.Series[0].Name = string.Empty;
        spec.Series[0].Parameters.Set("colors", ByLatitude);
        var capitals = new SeriesSpec { Type = "scatter", Name = "Capitals", Data = new DataSpec { Sample = "capitals", Count = 20 } };
        capitals.Parameters.Set("color", "#E63C3C");
        capitals.Parameters.Set("size", 7.0);
        spec.Series.Add(capitals);
        foreach (var type in new[] { "zoompan", "rubberband", "tooltip" })
        {
            spec.Modifiers.Add(new ModifierSpec { Type = type });
        }

        return spec;
    }
}
