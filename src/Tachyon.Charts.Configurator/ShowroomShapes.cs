using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The fields, shapes and cuts of the showroom pages as sample data: the same formulas, so that a page opens with the
/// data it shows. Each keeps its formula once for the preview and once as the code it writes.
/// </summary>
internal static class ShowroomShapes
{
    private const string Grid = "HeatmapData";
    private const string Shapes = "List<Polygon>";
    private const string Masked = "(HeatmapData Data, Polygon Region)";
    private const string Uneven = "(HeatmapData Data, double[] XEdges, double[] YEdges)";
    private const string Angles = "(double[] Angles, double[] Values)";
    private const string Slices = "(double[] Values, string[] Names)";

    private static readonly double[] _scores = [80, 60, 90, 70, 50, 75];
    private static readonly string[] _qualities = ["Speed", "Price", "Quality", "Service", "Design", "Range"];
    private static readonly double[] _mix = [32, 18, 15, 14, 9, 6, 3, 1.5, 1, 0.5];
    private static readonly string[] _sources = ["Wind", "Solar", "Gas", "Coal", "Biomass", "Hydro", "Nuclear", "Oil", "Other", "Geothermal"];

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        new("hills", "Hills and a valley over 0 to 6", DataShape.Grid, 512, Field, Grid, method => SampleCatalog.Header("hills and a valley over 0 to 6", method, Grid, "The number of cells per side.") + """
                {
                    var values = new float[count * count];
                    for (var row = 0; row < count; row++)
                    {
                        var y = row * 6.0 / count;
                        for (var column = 0; column < count; column++)
                        {
                            var x = column * 6.0 / count;
                            values[(row * count) + column] = (float)((Math.Sin(x) * Math.Cos(y * 1.3)) + (0.6 * Math.Exp(-(((x - 2) * (x - 2)) + ((y - 4) * (y - 4))))));
                        }
                    }

                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    return data;
                }

            """),
        new("hillsWithGap", "Hills and a valley with a gap", DataShape.Grid, 128, count => Field(count, gap: true), Grid, method => SampleCatalog.Header("hills and a valley over 0 to 6, missing above 4.8", method, Grid, "The number of cells per side.") + """
                {
                    var values = new float[count * count];
                    for (var row = 0; row < count; row++)
                    {
                        var y = row * 6.0 / count;
                        for (var column = 0; column < count; column++)
                        {
                            var x = column * 6.0 / count;
                            values[(row * count) + column] = x > 4.8 && y > 4.8 ? float.NaN : (float)((Math.Sin(x) * Math.Cos(y * 1.3)) + (0.6 * Math.Exp(-(((x - 2) * (x - 2)) + ((y - 4) * (y - 4))))));
                        }
                    }

                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    return data;
                }

            """),
        new("hillsInRegion", "Hills measured inside a region", DataShape.MaskedGrid, 400, count => (Field(count), Region()), Masked, method => SampleCatalog.Header("hills measured inside a region", method, Masked, "The number of cells per side.") + """
                {
                    var values = new float[count * count];
                    for (var row = 0; row < count; row++)
                    {
                        var y = row * 6.0 / count;
                        for (var column = 0; column < count; column++)
                        {
                            var x = column * 6.0 / count;
                            values[(row * count) + column] = (float)((Math.Sin(x) * Math.Cos(y * 1.3)) + (0.6 * Math.Exp(-(((x - 2) * (x - 2)) + ((y - 4) * (y - 4))))));
                        }
                    }

                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    var (x0, y0) = RegionBlob(5, 5, 4.2, 0.9, 120, 3);
                    var (x1, y1) = RegionBlob(6, 5.6, 1.1, 0.25, 40, 7);
                    return (data, new Polygon(new PolygonRing(x0, y0), [new PolygonRing(x1, y1)]) { Name = "Region" });
                }

                private static (double[] X, double[] Y) RegionBlob(double cx, double cy, double radius, double wobble, int corners, int seed)
                {
                    var (x, y) = (new double[corners], new double[corners]);
                    for (var i = 0; i < corners; i++)
                    {
                        var angle = i * 2 * Math.PI / corners;
                        var r = radius + (wobble * ((0.5 * Math.Sin((angle * 3) + seed)) + (0.3 * Math.Sin((angle * 7) + (seed * 2))) + (0.2 * Math.Sin((angle * 11) + seed))));
                        (x[i], y[i]) = (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
                    }

                    return (x, y);
                }

            """) { HasCount = true },
        new("holedShapes", "A ring, a star and an island with lakes", DataShape.Polygons, 3, _ => ShapesWithHoles(), Shapes, method => SampleCatalog.Header("a ring, a star and an island with lakes", method, Shapes, "Not used: the shapes are fixed.") + """
                {
                    var (ringX, ringY) = ShapeCircle(3, 7, 2, 48);
                    var (holeX, holeY) = ShapeCircle(3, 7, 1, 32);
                    var (starX, starY) = (new double[10], new double[10]);
                    for (var i = 0; i < 10; i++)
                    {
                        var angle = (Math.PI / 2) + (i * Math.PI / 5);
                        var radius = i % 2 == 0 ? 2 : 0.9;
                        (starX[i], starY[i]) = (7.5 + (radius * Math.Cos(angle)), 7 + (radius * Math.Sin(angle)));
                    }

                    var (blobX, blobY) = (new double[80], new double[80]);
                    for (var i = 0; i < 80; i++)
                    {
                        var angle = i * 2 * Math.PI / 80;
                        var r = 2.2 + (0.6 * ((0.5 * Math.Sin((angle * 3) + 5)) + (0.3 * Math.Sin((angle * 7) + 10)) + (0.2 * Math.Sin((angle * 11) + 5))));
                        (blobX[i], blobY[i]) = (5 + (r * Math.Cos(angle)), 2.6 + (r * Math.Sin(angle)));
                    }

                    var (lakeX, lakeY) = ShapeCircle(5.6, 2.4, 0.6, 24);
                    var (lake2X, lake2Y) = ShapeCircle(4.2, 3, 0.4, 20);
                    return
                    [
                        new Polygon(new PolygonRing(ringX, ringY), [new PolygonRing(holeX, holeY)]) { Name = "Ring" },
                        new Polygon(new PolygonRing(starX, starY)) { Name = "Star" },
                        new Polygon(new PolygonRing(blobX, blobY), [new PolygonRing(lakeX, lakeY), new PolygonRing(lake2X, lake2Y)]) { Name = "Island with two lakes" },
                    ];
                }

                private static (double[] X, double[] Y) ShapeCircle(double cx, double cy, double radius, int corners)
                {
                    var (x, y) = (new double[corners], new double[corners]);
                    for (var i = 0; i < corners; i++)
                    {
                        var angle = i * 2 * Math.PI / corners;
                        (x[i], y[i]) = (cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
                    }

                    return (x, y);
                }

            """) { HasCount = false, MinimumCount = 1 },
        new("honeycomb", "Hexagons in a honeycomb", DataShape.Polygons, 10_000, Honeycomb, Shapes, method => SampleCatalog.Header("hexagons in a honeycomb", method, Shapes, "The number of hexagons, in rows of 100.") + """
                {
                    var hexagons = new List<Polygon>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var (row, column) = (i / 100, i % 100);
                        var (cx, cy) = ((column * 1.5) + (row % 2 * 0.75), row * 0.433);
                        var (x, y) = (new double[6], new double[6]);
                        for (var k = 0; k < 6; k++)
                        {
                            (x[k], y[k]) = (cx + (0.48 * Math.Cos(k * Math.PI / 3)), cy + (0.48 * Math.Sin(k * Math.PI / 3)));
                        }

                        hexagons.Add(new Polygon(new PolygonRing(x, y)) { Name = $"Cell {row}/{column}" });
                    }

                    return hexagons;
                }

            """) { MinimumCount = 1 },
        new("fineCells", "Cells growing in frequency and depth", DataShape.UnevenGrid, 4096, count => FineCells(count), Uneven, method => SampleCatalog.Header("cells growing in frequency and depth", method, Uneven, "The number of cells per side.") + """
                {
                    var values = new float[count * count];
                    Parallel.For(0, count, row =>
                    {
                        var y = row * 8.0 / count;
                        for (var column = 0; column < count; column++)
                        {
                            var x = column * 8.0 / count;
                            values[(row * count) + column] = (float)((Math.Sin(x * x * 0.6) * Math.Cos(y * 2.1)) + Math.Exp(-(((x - 3) * (x - 3)) + ((y - 5) * (y - 5)))));
                        }
                    });
                    var data = new HeatmapData(count, count);
                    data.SetValues(values);
                    double[] frequencies = [.. Enumerable.Range(0, count + 1).Select(i => Math.Pow(10, i * 4.0 / count))];
                    double[] depths = [.. Enumerable.Range(0, count + 1).Select(i => 100 * Math.Pow(i / (double)count, 1.6))];
                    return (data, frequencies, depths);
                }

            """) { MinimumCount = 2 },
        new("azimuthCut", "Antenna gain around at 20° elevation", DataShape.Polar, 361, _ => AzimuthCut(), Angles, method => SampleCatalog.Header("antenna gain around at 20° elevation, in dB above a floor 40 dB below the main lobe", method, Angles, "Not used: one value per degree.") + """
                {
                    // The floor lies 40 dB below the strongest gain in any direction.
                    var strongest = double.MinValue;
                    for (var elevation = -90; elevation <= 90; elevation++)
                    {
                        for (var azimuth = -180; azimuth < 180; azimuth++)
                        {
                            strongest = Math.Max(strongest, AntennaGain(azimuth, elevation));
                        }
                    }

                    var (angles, values) = (new double[361], new double[361]);
                    for (var i = 0; i <= 360; i++)
                    {
                        var azimuth = -180 + (i % 360);
                        (angles[i], values[i]) = ((azimuth + 360) % 360, Math.Max(0, AntennaGain(azimuth, 20) - (strongest - 40)));
                    }

                    return (angles, values);
                }

                private static double AntennaGain(double azimuth, double elevation)
                {
                    var (a, e) = ((((azimuth - 30 + 540) % 360) - 180) / 40, (elevation - 20) / 24);
                    var main = 15 - (12 * ((a * a) + (e * e)));
                    var side = -14 + (6 * Math.Cos(double.DegreesToRadians(elevation)) * Math.Cos(double.DegreesToRadians(5 * azimuth))) + (4 * Math.Cos(double.DegreesToRadians(4 * elevation)));
                    return 10 * Math.Log10(Math.Pow(10, main / 10) + Math.Pow(10, side / 10));
                }

            """) { HasCount = false },
        new("scores", "Six qualities", DataShape.Slices, 6, _ => (_scores.ToArray(), _qualities.ToArray()), Slices, method => SampleCatalog.Header("six qualities", method, Slices, "Not used: the scores are fixed.") + """
                {
                    double[] values = [80, 60, 90, 70, 50, 75];
                    string[] names = ["Speed", "Price", "Quality", "Service", "Design", "Range"];
                    return (values, names);
                }

            """) { HasCount = false },
        new("tenSources", "Power mix of ten sources", DataShape.Slices, 10, _ => (_mix.ToArray(), _sources.ToArray()), Slices, method => SampleCatalog.Header("power mix of ten sources", method, Slices, "Not used: the shares are fixed.") + """
                {
                    double[] values = [32, 18, 15, 14, 9, 6, 3, 1.5, 1, 0.5];
                    string[] names = ["Wind", "Solar", "Gas", "Coal", "Biomass", "Hydro", "Nuclear", "Oil", "Other", "Geothermal"];
                    return (values, names);
                }

            """) { HasCount = false },
    ];

    private static HeatmapData Field(int count) => Field(count, gap: false);

    private static HeatmapData Field(int count, bool gap)
    {
        var values = new float[count * count];
        for (var row = 0; row < count; row++)
        {
            var y = row * 6.0 / count;
            for (var column = 0; column < count; column++)
            {
                var x = column * 6.0 / count;
                values[(row * count) + column] = gap && x > 4.8 && y > 4.8 ? float.NaN : (float)((Math.Sin(x) * Math.Cos(y * 1.3)) + (0.6 * Math.Exp(-(((x - 2) * (x - 2)) + ((y - 4) * (y - 4))))));
            }
        }

        var data = new HeatmapData(count, count);
        data.SetValues(values);
        return data;
    }

    private static Polygon Region()
    {
        var (x, y) = Blob(5, 5, 4.2, 0.9, 120, 3);
        var (lakeX, lakeY) = Blob(6, 5.6, 1.1, 0.25, 40, 7);
        return new Polygon(new PolygonRing(x, y), [new PolygonRing(lakeX, lakeY)]) { Name = "Region" };
    }

    private static List<Polygon> ShapesWithHoles()
    {
        var (ringX, ringY) = Circle(3, 7, 2, 48);
        var (holeX, holeY) = Circle(3, 7, 1, 32);
        var (starX, starY) = (new double[10], new double[10]);
        for (var i = 0; i < 10; i++)
        {
            var angle = (Math.PI / 2) + (i * Math.PI / 5);
            var radius = i % 2 == 0 ? 2 : 0.9;
            (starX[i], starY[i]) = (7.5 + (radius * Math.Cos(angle)), 7 + (radius * Math.Sin(angle)));
        }

        var (blobX, blobY) = Blob(5, 2.6, 2.2, 0.6, 80, 5);
        var (lakeX, lakeY) = Circle(5.6, 2.4, 0.6, 24);
        var (lake2X, lake2Y) = Circle(4.2, 3, 0.4, 20);
        return
        [
            new Polygon(new PolygonRing(ringX, ringY), [new PolygonRing(holeX, holeY)]) { Name = "Ring" },
            new Polygon(new PolygonRing(starX, starY)) { Name = "Star" },
            new Polygon(new PolygonRing(blobX, blobY), [new PolygonRing(lakeX, lakeY), new PolygonRing(lake2X, lake2Y)]) { Name = "Island with two lakes" },
        ];
    }

    private static List<Polygon> Honeycomb(int count)
    {
        var hexagons = new List<Polygon>(count);
        for (var i = 0; i < count; i++)
        {
            var (row, column) = (i / 100, i % 100);
            var (x, y) = Circle((column * 1.5) + (row % 2 * 0.75), row * 0.433, 0.48, 6);
            hexagons.Add(new Polygon(new PolygonRing(x, y)) { Name = $"Cell {row}/{column}" });
        }

        return hexagons;
    }

    private static (HeatmapData Data, double[] XEdges, double[] YEdges) FineCells(int count)
    {
        var values = new float[count * count];
        Parallel.For(0, count, row =>
        {
            var y = row * 8.0 / count;
            for (var column = 0; column < count; column++)
            {
                var x = column * 8.0 / count;
                values[(row * count) + column] = (float)((Math.Sin(x * x * 0.6) * Math.Cos(y * 2.1)) + Math.Exp(-(((x - 3) * (x - 3)) + ((y - 5) * (y - 5)))));
            }
        });
        var data = new HeatmapData(count, count);
        data.SetValues(values);
        return (data, [.. Enumerable.Range(0, count + 1).Select(i => Math.Pow(10, i * 4.0 / count))], [.. Enumerable.Range(0, count + 1).Select(i => 100 * Math.Pow(i / (double)count, 1.6))]);
    }

    private static (double[] Angles, double[] Values) AzimuthCut()
    {
        var strongest = double.MinValue;
        for (var elevation = -90; elevation <= 90; elevation++)
        {
            for (var azimuth = -180; azimuth < 180; azimuth++)
            {
                strongest = Math.Max(strongest, Gain(azimuth, elevation));
            }
        }

        var (angles, values) = (new double[361], new double[361]);
        for (var i = 0; i <= 360; i++)
        {
            var azimuth = -180 + (i % 360);
            (angles[i], values[i]) = ((azimuth + 360) % 360, Math.Max(0, Gain(azimuth, 20) - (strongest - 40)));
        }

        return (angles, values);
    }

    private static double Gain(double azimuth, double elevation)
    {
        var (a, e) = ((((azimuth - 30 + 540) % 360) - 180) / 40, (elevation - 20) / 24);
        var main = 15 - (12 * ((a * a) + (e * e)));
        var side = -14 + (6 * Math.Cos(double.DegreesToRadians(elevation)) * Math.Cos(double.DegreesToRadians(5 * azimuth))) + (4 * Math.Cos(double.DegreesToRadians(4 * elevation)));
        return 10 * Math.Log10(Math.Pow(10, main / 10) + Math.Pow(10, side / 10));
    }

    private static (double[] X, double[] Y) Circle(double cx, double cy, double radius, int corners)
    {
        var (x, y) = (new double[corners], new double[corners]);
        for (var i = 0; i < corners; i++)
        {
            var angle = i * 2 * Math.PI / corners;
            (x[i], y[i]) = (cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
        }

        return (x, y);
    }

    // A wobbly round shape, the same on every run.
    private static (double[] X, double[] Y) Blob(double cx, double cy, double radius, double wobble, int corners, int seed)
    {
        var (x, y) = (new double[corners], new double[corners]);
        for (var i = 0; i < corners; i++)
        {
            var angle = i * 2 * Math.PI / corners;
            var r = radius + (wobble * ((0.5 * Math.Sin((angle * 3) + seed)) + (0.3 * Math.Sin((angle * 7) + (seed * 2))) + (0.2 * Math.Sin((angle * 11) + seed))));
            (x[i], y[i]) = (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
        }

        return (x, y);
    }
}
