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
        new("Surface with light", catalog => Hill(catalog)),
        new("Video recording", catalog =>
        {
            var spec = Hill(catalog);
            spec.ExportMenu = true;
            return spec;
        }),
        new("Cut planes", catalog =>
        {
            var spec = Hill(catalog);
            spec.Scene.Set("planes", "-1 0 0 @ -0.35; -0.5 1 0 @ -0.5");
            return spec;
        }),
        new("One plane, all kinds", catalog =>
        {
            var spec = Scene(catalog, "One plane cuts every kind of data", "scatter3d", "threeClouds", 3000, [], _unitBox);
            spec.Series.Add(Series(catalog, "line3d", "spiralPath", 4000, ("width", 2.5)));
            spec.Series.Add(Series(catalog, "columns3d", "slopeRaster", 8, ("start", -1.0), ("size", 2.0), ("baseline", -1.0)));
            spec.Scene.Set("planes", "-1 -0.4 0 @ -0.1");
            return spec;
        }),
        new("Planes and limits", catalog =>
        {
            var spec = Scene(catalog, "Surface against a limit of 0.9", "surface3d", "hills", 128, [("size", 6.0), ("zFrom", -1.5), ("zTo", 2.0), ("limit", true), ("limitValue", 0.9), ("limitLine", true)], Box("0..6", "x", "0..6", "y", "-1.5..2", "z"));
            spec.Series.Add(Series(catalog, "model3d", "testZone", 1, ("color", "#3A86FF"), ("opacity", 0.25), ("gloss", 0.0)));
            spec.Series.Add(Series(catalog, "model3d", "slantedArea", 1, ("color", "#06D6A0"), ("opacity", 0.6), ("gloss", 0.2)));
            return spec;
        }),
        new("Axis box", catalog => Scene(catalog, "Axis box", "box3d", "scattered", 400, [], Box("0..10", "Time in s", "-5..5", "Offset in mm", "0..100", "Level in dB"))),
        new("Model with measuring points", catalog =>
        {
            var spec = Scene(catalog, "Pipe bend, wall temperature in °C", "model3d", "pipeBend", 96, [("gloss", 0.3), ("colorBy", "Values"), ("colormap", "Turbo"), ("colorsFollow", false), ("scaleFrom", 55.0), ("scaleTo", 115.0), ("scaleTitle", "°C")], Box("-1.25..126.25", "x in mm", "-63.75..63.75", "y in mm", "-26.25..101.25", "z in mm"));
            spec.Series.Add(Series(catalog, "scatter3d", "pipeSensors", 9));
            spec.Scene.Set("planes", "0 1 0 @ -0.05");
            return spec;
        }),
        new("Model of 1 million triangles", catalog => Scene(catalog, "Knot of tube, colored by height", "model3d", "tubeKnot", 4000, [("gloss", 0.4), ("sharpness", 48.0), ("colorBy", "Height"), ("colormap", "Viridis"), ("colorsFollow", false), ("scaleFrom", -1.4), ("scaleTo", 1.4), ("scaleTitle", "Height in m")], Box("-2.99531..3.630977", "x in m", "-3.313152..3.313136", "y in m", "-3.313141..3.313146", "z in m"))),
        new("Volume slices 512³", catalog => Volume(catalog, "Slices through a body", 512, ("sliceThreshold", 0.04))),
        new("Through a volume 512³", catalog => Volume(catalog, "Through a body", 512, ("transfer", "Tissue and spots"), ("slices", false))),
        new("Maximum projection 512³", catalog => Volume(catalog, "Highest value along each ray", 512, ("mode", "Maximum"), ("slices", false), ("sliceThreshold", 0.05), ("colormap", "Inferno"))),
        new("Surface of equal value 512³", catalog =>
        {
            var spec = Volume(catalog, "Surface of equal value", 512, ("mode", "Surface"), ("slices", false), ("isoAt", 0.45));
            spec.Scene.Set("planes", "1 1 0 @ -0.1");
            return spec;
        }),
        new("Objects inside a volume", catalog =>
        {
            var spec = Volume(catalog, "Objects inside a volume", 128, ("transfer", "Tissue and spots"), ("slices", false));
            spec.Series.Add(Series(catalog, "line3d", "greenSpiral", 400, ("width", 3.0)));
            spec.Series.Add(Series(catalog, "model3d", "saddleMesh", 96));
            return spec;
        }),
        new("Cloud of 20 million points", catalog => Scene(catalog, "Scanned landscape", "cloud3d", "landscape", 20_000_000, [], _unitBox)),
        new("Cloud of 100 million points", catalog => Scene(catalog, "Scanned landscape", "cloud3d", "landscape", 100_000_000, [], _unitBox)),
        new("Path of 200 000 points", catalog => Scene(catalog, "Path of a particle", "line3d", "particlePath", 200_000, [], Box("-25..25", "x", "-30..30", "y", "0..55", "z"))),

        // The live pages: points that keep coming, and the waterfall of a running spectrum above it.
        new("Path, live", catalog => Scene(catalog, "Path of a particle, live", "line3d", "particlePathLive", 100_000, [], Box("-25..25", "x", "-30..30", "y", "0..55", "z"))),
        new("Cloud, live", catalog => Scene(catalog, "A million points per second", "scatter3d", "sweepLive", 5_000_000, [("size", 1.5)], _unitBox)),
        new("Waterfall, live", catalog =>
        {
            var spec = catalog.CreateSpec("spectrumWaterfall");
            spec.Theme = "Instrument";
            var series = spec.Series[0];
            (series.Data.Sample, series.Data.Count) = ("noisyCarriers", 1500);
            series.Parameters.Set("layout", "Waterfall above the spectrum");
            series.Parameters.Set("maskLevel", -9.0);
            series.Parameters.Set("marker", -1.0);
            series.Parameters.Set("live", true);
            return spec;
        }),
        new("Cut, live", catalog =>
        {
            // The 3D chart brings its own theme; the chart of the cut takes that of the application, as in the showroom.
            var spec = catalog.CreateSpec("surfaceCut3d");
            (spec.Series[0].Data.Sample, spec.Series[0].Data.Count) = ("hillAcross", 256);
            return spec;
        }),
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

    // The body in the unit box, as the volume pages show it.
    private static ChartSpec Volume(ChartCatalog catalog, string title, int side, params (string Name, object Value)[] options) =>
        Scene(catalog, title, "volume3d", "layeredBody", side, [("start", -1.0), ("size", 2.0), .. options], _unitBox);

    // The hill of triangles in the unit box.
    private static ChartSpec Hill(ChartCatalog catalog) => Scene(catalog, "Surface", "model3d", "hillMesh", 96, [], _unitBox);

    // One more series of a type with its sample data and options.
    private static SeriesSpec Series(ChartCatalog catalog, string type, string sample, int count, params (string Name, object Value)[] parameters)
    {
        var spec = Scene(catalog, string.Empty, type, sample, count, parameters, []);
        return spec.Series[0];
    }

    // Columns over a raster of 0 to 16 with two hills, glossy.
    private static ChartSpec Columns(ChartCatalog catalog, int side) =>
        Scene(catalog, "Columns over a raster", "columns3d", "rasterHills", side, [("size", 16.0), ("zFrom", 0.0), ("zTo", 1.2), ("gloss", 0.4)], [(ZRange, "0..1.2"), (ZTitle, "Value")]);

    // The field of hills over 0 to 6.
    private static ChartSpec Surface(ChartCatalog catalog, int side, params (string Name, object Value)[] more) =>
        Scene(catalog, "Surface", "surface3d", "hills", side, [("size", 6.0), ("zFrom", -1.5), ("zTo", 2.0), .. more], []);
}
