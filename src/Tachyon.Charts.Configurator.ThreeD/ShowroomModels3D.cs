using System.Buffers.Binary;
using System.Globalization;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Files;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.ThreeD;

/// <summary>
/// The models and shapes of the 3D pages of the showroom as sample data: a hill, a pipe bend colored by its wall
/// temperature, a knot of tube colored by height, a test zone and a slanted area with a hole, the sensors on the pipe and
/// a spiral path. Each formula stands once for the preview and once as code.
/// </summary>
internal static class ShowroomModels3D
{
    private const string Model = "ModelFileData";
    private const string PipeType = "(ModelFileData Model, double[] Values)";

    /// <summary>Gets a value indicating whether the data of a series bring a value per corner besides the model.</summary>
    /// <param name="series">The series.</param>
    /// <returns>Whether its sample makes the model with values.</returns>
    public static bool HasValues(SeriesSpec series) => series.Data.Kind == DataKind.Sample && series.Data.Sample == "pipeBend";
    private const double Leg = 40;
    private const double Bend = 60;
    private const double Outer = 15;
    private const double Inner = 12;
    private const double Flange = 25;
    private const double FlangeThickness = 5;
    private static readonly double _pipeLength = (2 * Leg) + (Bend * Math.PI / 2);

    /// <summary>Gets the samples whose data are the finished points.</summary>
    public static IReadOnlyCollection<string> Ready { get; } = ["pipeSensors", "spiralPath"];

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        new("hillMesh", "A hill as triangles, tinted by height", DataShape.Model, 96, _ => Hill(), Model, method => SampleCatalog.Header("a hill as triangles, tinted by height", method, Model, "Not used: 96 by 96 corners.") + """
                {
                    const int Side = 96;
                    var (positions, colors, triangles) = (new Point3D[Side * Side], new Rgba8[Side * Side], new List<int>(Side * Side * 6));
                    for (var row = 0; row < Side; row++)
                    {
                        for (var column = 0; column < Side; column++)
                        {
                            var (x, y) = ((column * 2.0 / (Side - 1)) - 1, (row * 2.0 / (Side - 1)) - 1);
                            var z = 0.7 * Math.Sin(x * 3) * Math.Cos(y * 2.5);
                            var share = (z + 1) / 2;
                            positions[(row * Side) + column] = new Point3D(x, y, z);
                            colors[(row * Side) + column] = new Rgba8((byte)(40 + (215 * share)), (byte)(110 + (60 * share)), (byte)(230 - (170 * share)), 255);
                            if (row > 0 && column > 0)
                            {
                                var corner = (row * Side) + column;
                                triangles.AddRange([corner - Side - 1, corner - Side, corner, corner - Side - 1, corner, corner - 1]);
                            }
                        }
                    }

                    return new ModelFileData(positions, [.. triangles], colors);
                }

            """) { HasCount = false },
        new("pipeBend", "A pipe bend with the temperature of its wall", DataShape.Model, 96, _ => Pipe(), PipeType, method => SampleCatalog.Header("a pipe bend with flanges and the temperature of its wall in °C at each corner", method, PipeType, "Not used: 96 corners around.") + PipeCode) { HasCount = false },
        new("tubeKnot", "A knot of tube", DataShape.Model, 4000, _ => Knot(), Model, method => SampleCatalog.Header("a knot of tube of a million triangles", method, Model, "Not used: 4000 rings of 125 corners.") + KnotCode) { HasCount = false },
        new("testZone", "A zone where a test runs", DataShape.Model, 1, _ => Zone(), Model, method => SampleCatalog.Header("an upright zone where a test runs", method, Model, "Not used.") + """
                {
                    var zone = Shapes3D.Rectangle(new Point3D(4.5, 0, -1.5), new Point3D(0, 6, 0), new Point3D(0, 0, 3.5), new Rgba8(58, 134, 255, 255));
                    return new ModelFileData([.. zone.Vertices.Select(corner => corner.Position)], [.. zone.Indices], null);
                }

            """) { HasCount = false },
        new("slantedArea", "A slanted area with a hole", DataShape.Model, 1, _ => Area(), Model, method => SampleCatalog.Header("a slanted area with a hole above the left part", method, Model, "Not used.") + """
                {
                    var outline = new Polygon(new PolygonRing([0.5, 3.0, 4.2, 2.6, 0.8], [0.6, 0.4, 2.2, 3.6, 2.8]), [new PolygonRing([1.6, 2.6, 2.2], [1.4, 1.5, 2.4])]);
                    var area = Shapes3D.Polygon(outline, new Point3D(0, 0, 1.45), new Point3D(1, 0, 0), new Point3D(0, 1, 0.12), new Rgba8(6, 214, 160, 255));
                    return new ModelFileData([.. area.Vertices.Select(corner => corner.Position)], [.. area.Indices], null);
                }

            """) { HasCount = false },
        new("pipeSensors", "Nine sensors on the pipe bend", DataShape.Points3D, 9, _ => Sensors(), "Points3D", method => SampleCatalog.Header("nine sensors on the outside of the pipe bend", method, "Points3D", "Not used: nine sensors.") + $$"""
                {
                    Point3D[] sensors = [{{string.Join(", ", SensorPlaces().Select(place => $"new({Number(place.X)}, {Number(place.Y)}, {Number(place.Z)})"))}}];
                    var points = new Points3D(sensors.Length);
                    foreach (var place in sensors)
                    {
                        points.Append(place.X, place.Y - 1, place.Z, new Rgba8(255, 255, 255, 255), 10);
                    }

                    return points;
                }

            """) { HasCount = false },
        new("spiralPath", "A spiral upward", DataShape.Points3D, 4000, Spiral, "Points3D", method => SampleCatalog.Header("a spiral upward, colored along its way", method, "Points3D", "The number of points.") + """
                {
                    var path = new Points3D(count);
                    for (var i = 0; i < count; i++)
                    {
                        var turn = i * 0.01;
                        path.Append(Math.Cos(turn) * 0.8, Math.Sin(turn) * 0.8, (i / (count / 2.0)) - 1, Colormaps.Inferno.Sample(i / (double)count));
                    }

                    return path;
                }

            """) { MinimumCount = 2 },
        new("slopeRaster", "A gentle slope of 8 by 8 cells", DataShape.Grid, 8, _ => Slope(), "HeatmapData", method => SampleCatalog.Header("a gentle slope of 8 by 8 cells", method, "HeatmapData", "Not used: 8 by 8 cells.") + """
                {
                    var values = new float[64];
                    for (var i = 0; i < 64; i++)
                    {
                        values[i] = -1 + (0.05f * (i % 8)) + (0.05f * (i / 8));
                    }

                    var data = new HeatmapData(8, 8);
                    data.SetValues(values);
                    return data;
                }

            """) { HasCount = false },
    ];

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static ModelFileData Hill()
    {
        const int Side = 96;
        var (positions, colors, triangles) = (new Point3D[Side * Side], new Rgba8[Side * Side], new List<int>(Side * Side * 6));
        for (var row = 0; row < Side; row++)
        {
            for (var column = 0; column < Side; column++)
            {
                var (x, y) = ((column * 2.0 / (Side - 1)) - 1, (row * 2.0 / (Side - 1)) - 1);
                var z = 0.7 * Math.Sin(x * 3) * Math.Cos(y * 2.5);
                var share = (z + 1) / 2;
                positions[(row * Side) + column] = new Point3D(x, y, z);
                colors[(row * Side) + column] = new Rgba8((byte)(40 + (215 * share)), (byte)(110 + (60 * share)), (byte)(230 - (170 * share)), 255);
                if (row > 0 && column > 0)
                {
                    var corner = (row * Side) + column;
                    triangles.AddRange([corner - Side - 1, corner - Side, corner, corner - Side - 1, corner, corner - 1]);
                }
            }
        }

        return new ModelFileData(positions, [.. triangles], colors);
    }

    private static ModelFileData Zone()
    {
        var zone = Shapes3D.Rectangle(new Point3D(4.5, 0, -1.5), new Point3D(0, 6, 0), new Point3D(0, 0, 3.5), new Rgba8(58, 134, 255, 255));
        return new ModelFileData([.. zone.Vertices.Select(corner => corner.Position)], [.. zone.Indices], null);
    }

    private static ModelFileData Area()
    {
        var outline = new Polygon(new PolygonRing([0.5, 3.0, 4.2, 2.6, 0.8], [0.6, 0.4, 2.2, 3.6, 2.8]), [new PolygonRing([1.6, 2.6, 2.2], [1.4, 1.5, 2.4])]);
        var area = Shapes3D.Polygon(outline, new Point3D(0, 0, 1.45), new Point3D(1, 0, 0), new Point3D(0, 1, 0.12), new Rgba8(6, 214, 160, 255));
        return new ModelFileData([.. area.Vertices.Select(corner => corner.Position)], [.. area.Indices], null);
    }

    private static Points3D Spiral(int count)
    {
        var path = new Points3D(count);
        for (var i = 0; i < count; i++)
        {
            var turn = i * 0.01;
            path.Append(Math.Cos(turn) * 0.8, Math.Sin(turn) * 0.8, (i / (count / 2.0)) - 1, Colormaps.Inferno.Sample(i / (double)count));
        }

        return path;
    }

    private static HeatmapData Slope()
    {
        var values = new float[64];
        for (var i = 0; i < 64; i++)
        {
            values[i] = -1 + (0.05f * (i % 8)) + (0.05f * (i / 8));
        }

        var data = new HeatmapData(8, 8);
        data.SetValues(values);
        return data;
    }

    private static Points3D Sensors()
    {
        var sensors = SensorPlaces();
        var points = new Points3D(sensors.Length);
        foreach (var place in sensors)
        {
            points.Append(place.X, place.Y - 1, place.Z, new Rgba8(255, 255, 255, 255), 10);
        }

        return points;
    }

    // Nine places on the outside of the pipe, 3 mm off its wall, from flange to flange.
    private static Point3D[] SensorPlaces()
    {
        var places = new Point3D[9];
        for (var i = 0; i < places.Length; i++)
        {
            var s = FlangeThickness + 6 + ((_pipeLength - (2 * FlangeThickness) - 12) * i / (places.Length - 1));
            places[i] = Middle(s) + (Point3D.Cross(Tangent(s), new Point3D(0, 1, 0)) * (Outer + 3));
        }

        return places;
    }

    // The pipe: flanges and the bend as rings of triangles, written and read as STL like a file, colored by the
    // temperature of the wall with a hot spot on the outside of the bend.
    private static (ModelFileData Model, double[] Values) Pipe()
    {
        const int Around = 96;
        const int BendSteps = 48;
        var stations = new List<(double S, double Radius)> { (0, Flange), (FlangeThickness, Flange), (FlangeThickness, Outer), (Leg, Outer) };
        for (var i = 1; i <= BendSteps; i++)
        {
            stations.Add((Leg + (Bend * Math.PI / 2 * i / BendSteps), Outer));
        }

        stations.AddRange([(_pipeLength - FlangeThickness, Outer), (_pipeLength - FlangeThickness, Flange), (_pipeLength, Flange)]);
        var corners = new List<Point3D>();
        for (var i = 0; i + 1 < stations.Count; i++)
        {
            Band(corners, stations[i].S, stations[i].Radius, stations[i + 1].S, stations[i + 1].Radius, Around, outward: true);
        }

        Band(corners, 0, Inner, _pipeLength, Inner, Around, outward: false, steps: BendSteps + 2);
        Band(corners, 0, Inner, 0, Flange, Around, outward: true);
        Band(corners, _pipeLength, Flange, _pipeLength, Inner, Around, outward: true);
        var count = corners.Count / 3;
        var bytes = new byte[84 + (count * 50)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), (uint)count);
        for (var i = 0; i < corners.Count; i++)
        {
            var at = 84 + ((i / 3) * 50) + 12 + ((i % 3) * 12);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), (float)corners[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), (float)corners[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), (float)corners[i].Z);
        }

        var model = ModelFile.ReadStl(new MemoryStream(bytes));
        var spot = Middle(Leg + (Bend * Math.PI / 4)) + (new Point3D(Math.Sin(Math.PI / 4), 0, -Math.Cos(Math.PI / 4)) * Outer);
        var temperatures = new double[model.Positions.Length];
        for (var i = 0; i < temperatures.Length; i++)
        {
            var place = model.Positions[i];
            var distance = (place - spot).Length;
            temperatures[i] = 92 - (34 * Along(place) / _pipeLength) + (26 * Math.Exp(-distance * distance / 500));
        }

        return (model, temperatures);
    }

    private static Point3D Middle(double s)
    {
        if (s <= Leg)
        {
            return new Point3D(s, 0, 0);
        }

        var angle = (s - Leg) / Bend;
        return angle <= Math.PI / 2
            ? new Point3D(Leg + (Bend * Math.Sin(angle)), 0, Bend - (Bend * Math.Cos(angle)))
            : new Point3D(Leg + Bend, 0, Bend + (s - Leg - (Bend * Math.PI / 2)));
    }

    private static Point3D Tangent(double s)
    {
        var angle = Math.Clamp((s - Leg) / Bend, 0, Math.PI / 2);
        return new Point3D(Math.Cos(angle), 0, Math.Sin(angle));
    }

    private static double Along(Point3D place) =>
        place.X <= Leg ? place.X
        : place.Z <= Bend ? Leg + (Bend * Math.Atan2(place.X - Leg, Bend - place.Z))
        : Leg + (Bend * Math.PI / 2) + (place.Z - Bend);

    private static void Band(List<Point3D> corners, double s0, double r0, double s1, double r1, int around, bool outward, int steps = 1)
    {
        for (var k = 0; k < steps; k++)
        {
            var (a, b) = (s0 + ((s1 - s0) * k / steps), s0 + ((s1 - s0) * (k + 1) / steps));
            for (var j = 0; j < around; j++)
            {
                var (p0, p1) = (Ring(a, r0, j, around), Ring(a, r0, j + 1, around));
                var (q0, q1) = (Ring(b, r1, j, around), Ring(b, r1, j + 1, around));
                corners.AddRange(outward ? [p0, q1, p1, p0, q0, q1] : [p0, p1, q1, p0, q1, q0]);
            }
        }
    }

    private static Point3D Ring(double s, double radius, int step, int around)
    {
        var angle = 2 * Math.PI * step / around;
        var side = Point3D.Cross(Tangent(s), new Point3D(0, 1, 0));
        return Middle(s) + (((new Point3D(0, 1, 0) * Math.Cos(angle)) + (side * Math.Sin(angle))) * radius);
    }

    private const string PipeCode = """
            {
                const int Around = 96;
                const int BendSteps = 48;
                var stations = new List<(double S, double Radius)> { (0, Flange), (FlangeThickness, Flange), (FlangeThickness, Outer), (Leg, Outer) };
                for (var i = 1; i <= BendSteps; i++)
                {
                    stations.Add((Leg + (Bend * Math.PI / 2 * i / BendSteps), Outer));
                }

                stations.AddRange([(PipeLength - FlangeThickness, Outer), (PipeLength - FlangeThickness, Flange), (PipeLength, Flange)]);
                var corners = new List<Point3D>();
                for (var i = 0; i + 1 < stations.Count; i++)
                {
                    Band(corners, stations[i].S, stations[i].Radius, stations[i + 1].S, stations[i + 1].Radius, Around, outward: true);
                }

                Band(corners, 0, Inner, PipeLength, Inner, Around, outward: false, steps: BendSteps + 2);
                Band(corners, 0, Inner, 0, Flange, Around, outward: true);
                Band(corners, PipeLength, Flange, PipeLength, Inner, Around, outward: true);

                // Written and read as STL, as a model from a file is.
                var count = corners.Count / 3;
                var bytes = new byte[84 + (count * 50)];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), (uint)count);
                for (var i = 0; i < corners.Count; i++)
                {
                    var at = 84 + ((i / 3) * 50) + 12 + ((i % 3) * 12);
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), (float)corners[i].X);
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), (float)corners[i].Y);
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), (float)corners[i].Z);
                }

                var model = ModelFile.ReadStl(new MemoryStream(bytes));
                // The temperature of the wall: hot water enters at the first flange and cools down, with a hot spot on the
                // outside of the bend.
                var spot = Middle(Leg + (Bend * Math.PI / 4)) + (new Point3D(Math.Sin(Math.PI / 4), 0, -Math.Cos(Math.PI / 4)) * Outer);
                var temperatures = new double[model.Positions.Length];
                for (var i = 0; i < temperatures.Length; i++)
                {
                    var place = model.Positions[i];
                    var distance = (place - spot).Length;
                    temperatures[i] = 92 - (34 * Along(place) / PipeLength) + (26 * Math.Exp(-distance * distance / 500));
                }

                return (model, temperatures);
            }

            // The pipe in mm: two legs of 40 joined by a bend of radius 60, outside 15, inside 12, flanges of 25.
            private const double Leg = 40;
            private const double Bend = 60;
            private const double Outer = 15;
            private const double Inner = 12;
            private const double Flange = 25;
            private const double FlangeThickness = 5;
            private static readonly double PipeLength = (2 * Leg) + (Bend * Math.PI / 2);

            // The middle of the pipe: along x, a quarter circle upward, then along z.
            private static Point3D Middle(double s)
            {
                if (s <= Leg)
                {
                    return new Point3D(s, 0, 0);
                }

                var angle = (s - Leg) / Bend;
                return angle <= Math.PI / 2
                    ? new Point3D(Leg + (Bend * Math.Sin(angle)), 0, Bend - (Bend * Math.Cos(angle)))
                    : new Point3D(Leg + Bend, 0, Bend + (s - Leg - (Bend * Math.PI / 2)));
            }

            private static Point3D Tangent(double s)
            {
                var angle = Math.Clamp((s - Leg) / Bend, 0, Math.PI / 2);
                return new Point3D(Math.Cos(angle), 0, Math.Sin(angle));
            }

            // The way along the middle to the place of a point of the wall.
            private static double Along(Point3D place) =>
                place.X <= Leg ? place.X
                : place.Z <= Bend ? Leg + (Bend * Math.Atan2(place.X - Leg, Bend - place.Z))
                : Leg + (Bend * Math.PI / 2) + (place.Z - Bend);

            // A band of triangles between two rings around the middle.
            private static void Band(List<Point3D> corners, double s0, double r0, double s1, double r1, int around, bool outward, int steps = 1)
            {
                for (var k = 0; k < steps; k++)
                {
                    var (a, b) = (s0 + ((s1 - s0) * k / steps), s0 + ((s1 - s0) * (k + 1) / steps));
                    for (var j = 0; j < around; j++)
                    {
                        var (p0, p1) = (Ring(a, r0, j, around), Ring(a, r0, j + 1, around));
                        var (q0, q1) = (Ring(b, r1, j, around), Ring(b, r1, j + 1, around));
                        corners.AddRange(outward ? [p0, q1, p1, p0, q0, q1] : [p0, p1, q1, p0, q1, q0]);
                    }
                }
            }

            private static Point3D Ring(double s, double radius, int step, int around)
            {
                var angle = 2 * Math.PI * step / around;
                var side = Point3D.Cross(Tangent(s), new Point3D(0, 1, 0));
                return Middle(s) + (((new Point3D(0, 1, 0) * Math.Cos(angle)) + (side * Math.Sin(angle))) * radius);
            }

        """;

    // A knot that winds three times around and twice through a ring, as a tube of 4000 rings of 125 corners. A frame is
    // carried along so that the tube does not twist, and the turn it has after one round is spread over the way.
    private static ModelFileData Knot()
    {
        const int Along = 4000;
        const int Around = 125;
        var frames = new Point3D[Along + 1];
        frames[0] = Point3D.Cross((KnotAt(1e-3) - KnotAt(0)).Normalized(), new Point3D(0, 0, 1)).Normalized();
        for (var i = 1; i <= Along; i++)
        {
            var tangent = KnotTangent(2 * Math.PI * i / Along);
            frames[i] = (frames[i - 1] - (tangent * Point3D.Dot(frames[i - 1], tangent))).Normalized();
        }

        var start = KnotTangent(0);
        var turn = Math.Atan2(Point3D.Dot(Point3D.Cross(frames[0], frames[Along]), start), Point3D.Dot(frames[0], frames[Along]));
        var positions = new Point3D[Along * Around];
        for (var i = 0; i < Along; i++)
        {
            var t = 2 * Math.PI * i / Along;
            var (middle, tangent, frame) = (KnotAt(t), KnotTangent(t), frames[i]);
            var side = Point3D.Cross(tangent, frame);
            for (var j = 0; j < Around; j++)
            {
                var angle = (2 * Math.PI * j / Around) - (turn * i / Along);
                positions[(i * Around) + j] = middle + (((frame * Math.Cos(angle)) + (side * Math.Sin(angle))) * 0.42);
            }
        }

        var triangles = new int[Along * Around * 6];
        var at = 0;
        for (var i = 0; i < Along; i++)
        {
            for (var j = 0; j < Around; j++)
            {
                var (a, b) = ((i * Around) + j, (i * Around) + ((j + 1) % Around));
                var (c, d) = ((((i + 1) % Along) * Around) + ((j + 1) % Around), (((i + 1) % Along) * Around) + j);
                (triangles[at], triangles[at + 1], triangles[at + 2]) = (a, b, c);
                (triangles[at + 3], triangles[at + 4], triangles[at + 5]) = (a, c, d);
                at += 6;
            }
        }

        return new ModelFileData(positions, triangles, null);
    }

    private static Point3D KnotTangent(double t) => (KnotAt(t + 1e-4) - KnotAt(t - 1e-4)).Normalized();

    private static Point3D KnotAt(double t) => new((2 + Math.Cos(3 * t)) * Math.Cos(2 * t), (2 + Math.Cos(3 * t)) * Math.Sin(2 * t), Math.Sin(3 * t));

    private const string KnotCode = """
            {
                const int Along = 4000;
                const int Around = 125;
                var frames = new Point3D[Along + 1];
                frames[0] = Point3D.Cross((KnotAt(1e-3) - KnotAt(0)).Normalized(), new Point3D(0, 0, 1)).Normalized();
                for (var i = 1; i <= Along; i++)
                {
                    var tangent = KnotTangent(2 * Math.PI * i / Along);
                    frames[i] = (frames[i - 1] - (tangent * Point3D.Dot(frames[i - 1], tangent))).Normalized();
                }

                var start = KnotTangent(0);
                var turn = Math.Atan2(Point3D.Dot(Point3D.Cross(frames[0], frames[Along]), start), Point3D.Dot(frames[0], frames[Along]));
                var positions = new Point3D[Along * Around];
                for (var i = 0; i < Along; i++)
                {
                    var t = 2 * Math.PI * i / Along;
                    var (middle, tangent, frame) = (KnotAt(t), KnotTangent(t), frames[i]);
                    var side = Point3D.Cross(tangent, frame);
                    for (var j = 0; j < Around; j++)
                    {
                        var angle = (2 * Math.PI * j / Around) - (turn * i / Along);
                        positions[(i * Around) + j] = middle + (((frame * Math.Cos(angle)) + (side * Math.Sin(angle))) * 0.42);
                    }
                }

                var triangles = new int[Along * Around * 6];
                var at = 0;
                for (var i = 0; i < Along; i++)
                {
                    for (var j = 0; j < Around; j++)
                    {
                        var (a, b) = ((i * Around) + j, (i * Around) + ((j + 1) % Around));
                        var (c, d) = ((((i + 1) % Along) * Around) + ((j + 1) % Around), (((i + 1) % Along) * Around) + j);
                        (triangles[at], triangles[at + 1], triangles[at + 2]) = (a, b, c);
                        (triangles[at + 3], triangles[at + 4], triangles[at + 5]) = (a, c, d);
                        at += 6;
                    }
                }

                return new ModelFileData(positions, triangles, null);
            }

            private static Point3D KnotTangent(double t) => (KnotAt(t + 1e-4) - KnotAt(t - 1e-4)).Normalized();

            // A knot that winds three times around and twice through a ring.
            private static Point3D KnotAt(double t) => new((2 + Math.Cos(3 * t)) * Math.Cos(2 * t), (2 + Math.Cos(3 * t)) * Math.Sin(2 * t), Math.Sin(3 * t));

        """;
}
