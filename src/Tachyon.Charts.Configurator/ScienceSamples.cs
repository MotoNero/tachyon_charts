using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The sample data of the scientific types and rasters. Each keeps its formula twice, as C# for the preview and as
/// text for the code, side by side.
/// </summary>
internal static class ScienceSamples
{
    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Waves(),
        Uneven(),
        Region(),
        Polar("lobe", "Petals", 73, "5 + (3 * Math.Sin(angles[i] * Math.PI / 45))", angle => 5 + (3 * Math.Sin(angle * Math.PI / 45))),
        Polar("pattern", "Antenna cut in dB", 181, "Math.Max(0, 40 + (10 * Math.Log10((Math.Cos(angles[i] * Math.PI / 180) * Math.Cos(angles[i] * Math.PI / 180)) + 0.001)))", angle => Math.Max(0, 40 + (10 * Math.Log10((Math.Cos(angle * Math.PI / 180) * Math.Cos(angle * Math.PI / 180)) + 0.001)))),
        Mixture(),
        Impedance(),
        Shapes(),
    ];

    // A spectrum of 64 rows: a peak that wanders, a fixed one and a little noise.
    private static SampleInfo Waves() => new(
        "waves",
        "Spectrum with a wandering peak",
        DataShape.Spectrogram,
        128,
        count =>
        {
            var data = new SpectrogramData(count, 64);
            var row = new float[count];
            for (var r = 0; r < 64; r++)
            {
                for (var c = 0; c < count; c++)
                {
                    var u = c / (count - 1.0);
                    var peak = 0.3 + (0.2 * Math.Sin(r * 0.1));
                    row[c] = (float)(Math.Exp(-((u - peak) * (u - peak)) / 0.002) + (0.3 * Math.Exp(-((u - 0.7) * (u - 0.7)) / 0.01)) + (0.05 * (1 + Math.Sin((c * 1.7) + r))));
                }

                data.AppendRow(row);
            }

            return data;
        },
        "SpectrogramData",
        method => SampleCatalog.Header("a spectrum with a wandering peak", method, "SpectrogramData", "The number of values in a row.") + """
                {
                    var data = new SpectrogramData(count, 64);
                    var row = new float[count];
                    for (var r = 0; r < 64; r++)
                    {
                        for (var c = 0; c < count; c++)
                        {
                            var u = c / (count - 1.0);
                            var peak = 0.3 + (0.2 * Math.Sin(r * 0.1));
                            row[c] = (float)(Math.Exp(-((u - peak) * (u - peak)) / 0.002) + (0.3 * Math.Exp(-((u - 0.7) * (u - 0.7)) / 0.01)) + (0.05 * (1 + Math.Sin((c * 1.7) + r))));
                        }

                        data.AppendRow(row);
                    }

                    return data;
                }

            """)
    { MinimumCount = 2 };

    // A grid whose cells grow from left to right and from bottom to top, over 0 to 10.
    private static SampleInfo Uneven()
    {
        const string Type = "(HeatmapData Data, double[] XEdges, double[] YEdges)";
        return new(
            "uneven",
            "Cells that grow",
            DataShape.UnevenGrid,
            40,
            count =>
            {
                var (data, x, y) = (new HeatmapData(count, count), new double[count + 1], new double[count + 1]);
                for (var i = 0; i <= count; i++)
                {
                    (x[i], y[i]) = (10 * Math.Pow(i / (double)count, 2), 10 * Math.Pow(i / (double)count, 1.5));
                }

                var values = new float[count * count];
                for (var row = 0; row < count; row++)
                {
                    for (var column = 0; column < count; column++)
                    {
                        var (u, v) = ((x[column] + x[column + 1]) / 2, (y[row] + y[row + 1]) / 2);
                        values[(row * count) + column] = (float)(Math.Sin(u * 0.8) * Math.Cos(v * 0.6));
                    }
                }

                data.SetValues(values);
                return (data, x, y);
            },
            Type,
            method => SampleCatalog.Header("cells that grow", method, Type, "The columns and rows of the grid.") + """
                    {
                        var (data, x, y) = (new HeatmapData(count, count), new double[count + 1], new double[count + 1]);
                        for (var i = 0; i <= count; i++)
                        {
                            (x[i], y[i]) = (10 * Math.Pow(i / (double)count, 2), 10 * Math.Pow(i / (double)count, 1.5));
                        }

                        var values = new float[count * count];
                        for (var row = 0; row < count; row++)
                        {
                            for (var column = 0; column < count; column++)
                            {
                                var (u, v) = ((x[column] + x[column + 1]) / 2, (y[row] + y[row + 1]) / 2);
                                values[(row * count) + column] = (float)(Math.Sin(u * 0.8) * Math.Cos(v * 0.6));
                            }
                        }

                        data.SetValues(values);
                        return (data, x, y);
                    }

                """)
        { MinimumCount = 1 };
    }

    // A field of two peaks over 0 to 10 and the region it was measured in, a pentagon.
    private static SampleInfo Region()
    {
        const string Type = "(HeatmapData Data, Polygon Region)";
        return new(
            "region",
            "Measured inside a region",
            DataShape.MaskedGrid,
            200,
            count =>
            {
                var values = new float[count * count];
                for (var row = 0; row < count; row++)
                {
                    for (var column = 0; column < count; column++)
                    {
                        var (u, v) = (column * 10.0 / (count - 1), row * 10.0 / (count - 1));
                        values[(row * count) + column] = (float)(Math.Exp(-(((u - 4) * (u - 4)) + ((v - 5) * (v - 5))) / 4) + (0.6 * Math.Exp(-(((u - 7) * (u - 7)) + ((v - 3) * (v - 3))) / 2)));
                    }
                }

                var data = new HeatmapData(count, count);
                data.SetValues(values);
                return (data, new Polygon(new PolygonRing([1.0, 6, 9.5, 8, 2], [2.0, 0.5, 4, 9, 8.5])));
            },
            Type,
            method => SampleCatalog.Header("measured inside a region", method, Type, "The columns and rows of the grid.") + """
                    {
                        var values = new float[count * count];
                        for (var row = 0; row < count; row++)
                        {
                            for (var column = 0; column < count; column++)
                            {
                                var (u, v) = (column * 10.0 / (count - 1), row * 10.0 / (count - 1));
                                values[(row * count) + column] = (float)(Math.Exp(-(((u - 4) * (u - 4)) + ((v - 5) * (v - 5))) / 4) + (0.6 * Math.Exp(-(((u - 7) * (u - 7)) + ((v - 3) * (v - 3))) / 2)));
                            }
                        }

                        var data = new HeatmapData(count, count);
                        data.SetValues(values);
                        return (data, new Polygon(new PolygonRing([1.0, 6, 9.5, 8, 2], [2.0, 0.5, 4, 9, 8.5])));
                    }

                """)
        { MinimumCount = 2 };
    }

    // Values over the angle, from 0 to 360 degrees.
    private static SampleInfo Polar(string id, string name, int count, string formula, Func<double, double> value)
    {
        const string Type = "(double[] Angles, double[] Values)";
        return new(
            id,
            name,
            DataShape.Polar,
            count,
            n =>
            {
                var (angles, values) = (new double[n], new double[n]);
                for (var i = 0; i < n; i++)
                {
                    angles[i] = i * 360.0 / (n - 1);
                    values[i] = value(angles[i]);
                }

                return (angles, values);
            },
            Type,
            method => SampleCatalog.Header(name.ToLowerInvariant(), method, Type, "The number of angles.") + $$"""
                    {
                        var (angles, values) = (new double[count], new double[count]);
                        for (var i = 0; i < count; i++)
                        {
                            angles[i] = i * 360.0 / (count - 1);
                            values[i] = {{formula}};
                        }

                        return (angles, values);
                    }

                """);
    }

    // Mixtures of three parts, spread like samples of an alloy, the same on every run.
    private static SampleInfo Mixture()
    {
        const string Type = "(double[] A, double[] B, double[] C)";
        return new(
            "mixture",
            "Mixtures of three parts",
            DataShape.Ternary,
            300,
            count =>
            {
                var (a, b, c) = (new double[count], new double[count], new double[count]);
                for (var i = 0; i < count; i++)
                {
                    (a[i], b[i], c[i]) = (1.2 + Math.Sin(i * 1.37), 1.1 + Math.Sin((i * 0.731) + 1), 0.6 + (0.5 * Math.Sin((i * 2.311) + 2)));
                }

                return (a, b, c);
            },
            Type,
            method => SampleCatalog.Header("mixtures of three parts", method, Type) + """
                    {
                        var (a, b, c) = (new double[count], new double[count], new double[count]);
                        for (var i = 0; i < count; i++)
                        {
                            (a[i], b[i], c[i]) = (1.2 + Math.Sin(i * 1.37), 1.1 + Math.Sin((i * 0.731) + 1), 0.6 + (0.5 * Math.Sin((i * 2.311) + 2)));
                        }

                        return (a, b, c);
                    }

                """)
        { MinimumCount = 1 };
    }

    // The impedance of a resonator over a sweep: resistance and reactance in ohm.
    private static SampleInfo Impedance()
    {
        const string Type = "(double[] Resistance, double[] Reactance)";
        return new(
            "impedance",
            "Impedance over a sweep",
            DataShape.Smith,
            201,
            count =>
            {
                var (resistance, reactance) = (new double[count], new double[count]);
                for (var i = 0; i < count; i++)
                {
                    var t = (i / (count - 1.0) * 4) - 2;
                    (resistance[i], reactance[i]) = (50 + (40 / (1 + (t * t * 4))), 60 * t / (1 + (t * t)));
                }

                return (resistance, reactance);
            },
            Type,
            method => SampleCatalog.Header("the impedance over a sweep", method, Type, "The number of frequencies.") + """
                    {
                        var (resistance, reactance) = (new double[count], new double[count]);
                        for (var i = 0; i < count; i++)
                        {
                            var t = (i / (count - 1.0) * 4) - 2;
                            (resistance[i], reactance[i]) = (50 + (40 / (1 + (t * t * 4))), 60 * t / (1 + (t * t)));
                        }

                        return (resistance, reactance);
                    }

                """);
    }

    // Hexagons in rows over 0 to 10; every second one has a square hole.
    private static SampleInfo Shapes() => new(
        "shapes",
        "Hexagons, some with holes",
        DataShape.Polygons,
        16,
        count =>
        {
            var side = (int)Math.Ceiling(Math.Sqrt(count));
            var shapes = new List<Polygon>(count);
            for (var i = 0; i < count; i++)
            {
                var (cx, cy, r) = (((i % side) + 0.5) * 10.0 / side, ((i / side) + 0.5) * 10.0 / side, 4.0 / side);
                var (x, y) = (new double[6], new double[6]);
                for (var k = 0; k < 6; k++)
                {
                    (x[k], y[k]) = (cx + (r * Math.Cos(k * Math.PI / 3)), cy + (r * Math.Sin(k * Math.PI / 3)));
                }

                var hole = new PolygonRing([cx - (r / 3), cx + (r / 3), cx + (r / 3), cx - (r / 3)], [cy - (r / 3), cy - (r / 3), cy + (r / 3), cy + (r / 3)]);
                shapes.Add(new Polygon(new PolygonRing(x, y), i % 2 == 1 ? [hole] : null) { Name = "Shape " + (i + 1) });
            }

            return shapes;
        },
        "List<Polygon>",
        method => SampleCatalog.Header("hexagons, some with holes", method, "List<Polygon>", "The number of shapes.") + """
                {
                    var side = (int)Math.Ceiling(Math.Sqrt(count));
                    var shapes = new List<Polygon>(count);
                    for (var i = 0; i < count; i++)
                    {
                        var (cx, cy, r) = (((i % side) + 0.5) * 10.0 / side, ((i / side) + 0.5) * 10.0 / side, 4.0 / side);
                        var (x, y) = (new double[6], new double[6]);
                        for (var k = 0; k < 6; k++)
                        {
                            (x[k], y[k]) = (cx + (r * Math.Cos(k * Math.PI / 3)), cy + (r * Math.Sin(k * Math.PI / 3)));
                        }

                        var hole = new PolygonRing([cx - (r / 3), cx + (r / 3), cx + (r / 3), cx - (r / 3)], [cy - (r / 3), cy - (r / 3), cy + (r / 3), cy + (r / 3)]);
                        shapes.Add(new Polygon(new PolygonRing(x, y), i % 2 == 1 ? [hole] : null) { Name = "Shape " + (i + 1) });
                    }

                    return shapes;
                }

            """)
    { MinimumCount = 1 };
}
