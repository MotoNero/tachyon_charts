namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>The 3D pages of the showroom as descriptions: the same data, axes, titles and options.</summary>
internal static class ShowroomPages3D
{
    private static readonly (string, string)[] _unitBox = Box("-1..1", "x", "-1..1", "y", "-1..1", "z");

    public static IReadOnlyList<PageInfo> All { get; } =
    [
        new("Scatter of 3000 points", catalog => Scene(catalog, "Three clouds", "scatter3d", "threeClouds", 3000, [], _unitBox)),
        new("Scatter of 10 million points", catalog => Scene(catalog, "Three clouds", "scatter3d", "threeClouds", 10_000_000, [], _unitBox)),
        new("Columns", catalog => Columns(catalog, 16)),
        new("1 million columns", catalog => Columns(catalog, 1000)),
        new("Pie 3D", catalog => Scene(catalog, "Power mix", "pie3d", "tenSources", 10, [("exploded", 1.0)], [])),
        new("Donut 3D", catalog => Scene(catalog, "Power mix", "donut3d", "tenSources", 10, [("innerRadius", 0.55), ("height", 0.25)], [])),
        new("Bubbles", catalog => Scene(catalog, "Wells, volume by yield in m³ per day", "bubbles3d", "wells", 180, [], Box("0..10", "East in km", "0..10", "North in km", "0..8", "Depth in 100 m"))),
        new("1 million bubbles", catalog => Scene(catalog, "A million bubbles, size in screen points", "bubbles3d", "spiralBubbles", 1_000_000, [("sizeUnit", "Screen")], Box("-1.2..1.2", "x", "-1.2..1.2", "y", "-1.2..1.2", "z"))),
        new("Antenna pattern", catalog => Scene(catalog, "Antenna gain", "antenna3d", "mainLobe", 361, [], [])),
        new("Map from measurements", catalog => Scene(catalog, "Temperature over speed and load", "map3d", "engineMap", 160, [], Box("0..6000", "Speed in rpm", "0..100", "Load in %", "60..120", "Temperature in °C"))),
        new("Height lines", catalog => Surface(catalog, 512, ("heightLines", 13.0), ("onWalls", true))),
        new("Surface 4096 × 4096", catalog => Surface(catalog, 4096)),
        new("Path of 200 000 points", catalog => Scene(catalog, "Path of a particle", "line3d", "particlePath", 200_000, [], Box("-25..25", "x", "-30..30", "y", "0..55", "z"))),
    ];

    private const string ZRange = "zRange";
    private const string ZTitle = "zTitle";

    // The ranges and titles of the axes of the box.
    private static (string, string)[] Box(string x, string xTitle, string y, string yTitle, string z, string zTitle) =>
        [("xRange", x), ("xTitle", xTitle), ("yRange", y), ("yTitle", yTitle), (ZRange, z), (ZTitle, zTitle)];

    // A scene in the instrument theme with one series of a type, its sample data and options, and the axes of the box.
    private static ChartSpec Scene(ChartCatalog catalog, string title, string type, string sample, int count, (string Name, object Value)[] parameters, (string Name, string Value)[] scene)
    {
        var spec = catalog.CreateSpec(type);
        (spec.Title, spec.Theme) = (title, "Instrument");
        var series = spec.Series[0];
        (series.Data.Sample, series.Data.Count) = (sample, count);
        foreach (var (name, value) in parameters)
        {
            switch (value)
            {
                case double number:
                    series.Parameters.Set(name, number);
                    break;
                case bool flag:
                    series.Parameters.Set(name, flag);
                    break;
                default:
                    series.Parameters.Set(name, (string)value);
                    break;
            }
        }

        foreach (var (name, value) in scene)
        {
            spec.Scene.Set(name, value);
        }

        return spec;
    }

    // Columns over a raster of 0 to 16 with two hills, glossy.
    private static ChartSpec Columns(ChartCatalog catalog, int side) =>
        Scene(catalog, "Columns over a raster", "columns3d", "rasterHills", side, [("size", 16.0), ("zFrom", 0.0), ("zTo", 1.2), ("gloss", 0.4)], [(ZRange, "0..1.2"), (ZTitle, "Value")]);

    // The field of hills over 0 to 6.
    private static ChartSpec Surface(ChartCatalog catalog, int side, params (string Name, object Value)[] more) =>
        Scene(catalog, "Surface", "surface3d", "hills", side, [("size", 6.0), ("zFrom", -1.5), ("zTo", 2.0), .. more], []);
}
