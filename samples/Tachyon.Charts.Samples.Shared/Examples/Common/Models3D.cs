using System.Buffers.Binary;
using Tachyon.Charts.ThreeD.Files;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Models for the 3D pages: a pipe bend with flanges, made as a binary STL file and read back like one from disk, and a large knot.</summary>
public static class Models3D
{
    /// <summary>The length of the straight piece before and after the bend, in mm.</summary>
    private const double Leg = 40;

    /// <summary>The radius of the bend, in mm.</summary>
    private const double Bend = 60;

    private const double Outer = 15;
    private const double Inner = 12;
    private const double Flange = 25;
    private const double FlangeThickness = 5;

    /// <summary>Gets the length of the pipe along its middle, in mm.</summary>
    public static double PipeLength { get; } = (2 * Leg) + (Bend * Math.PI / 2);

    /// <summary>Creates the pipe bend as the bytes of a binary STL file.</summary>
    /// <param name="around">The number of steps around the pipe.</param>
    /// <returns>The file.</returns>
    public static byte[] CreatePipeStl(int around = 96)
    {
        var length = PipeLength;
        var bendSteps = 48;

        // Places along the middle, with the outer radius there: a flange at each end, the bend in fine steps.
        var stations = new List<(double S, double Radius)> { (0, Flange), (FlangeThickness, Flange), (FlangeThickness, Outer), (Leg, Outer) };
        for (var i = 1; i <= bendSteps; i++)
        {
            stations.Add((Leg + (Bend * Math.PI / 2 * i / bendSteps), Outer));
        }

        stations.AddRange([(length - FlangeThickness, Outer), (length - FlangeThickness, Flange), (length, Flange)]);

        var corners = new List<Point3D>();
        for (var i = 0; i + 1 < stations.Count; i++)
        {
            Band(corners, stations[i].S, stations[i].Radius, stations[i + 1].S, stations[i + 1].Radius, around, outward: true);
        }

        Band(corners, 0, Inner, length, Inner, around, outward: false, steps: bendSteps + 2);
        Band(corners, 0, Inner, 0, Flange, around, outward: true);
        Band(corners, length, Flange, length, Inner, around, outward: true);
        return ToStl(corners);
    }

    /// <summary>Reads the pipe bend from its STL bytes.</summary>
    /// <returns>The model.</returns>
    public static ModelFileData ReadPipe() => ModelFile.ReadStl(new MemoryStream(CreatePipeStl()));

    /// <summary>Gets the temperature of the wall at a place: hot water enters at the first flange and cools down, with a hot spot on the outside of the bend.</summary>
    /// <param name="place">The place in mm.</param>
    /// <returns>The temperature in °C.</returns>
    public static double Temperature(Point3D place)
    {
        var along = Along(place);
        var spot = Middle(Leg + (Bend * Math.PI / 4)) + (new Point3D(Math.Sin(Math.PI / 4), 0, -Math.Cos(Math.PI / 4)) * Outer);
        var distance = (place - spot).Length;
        return 92 - (34 * along / PipeLength) + (26 * Math.Exp(-distance * distance / 500));
    }

    /// <summary>Gets places of sensors along the inner side of the pipe.</summary>
    /// <param name="count">The number of sensors.</param>
    /// <returns>The places in mm.</returns>
    public static Point3D[] Sensors(int count)
    {
        var places = new Point3D[count];
        for (var i = 0; i < count; i++)
        {
            var s = FlangeThickness + 6 + ((PipeLength - (2 * FlangeThickness) - 12) * i / (count - 1));
            places[i] = Middle(s) + (Point3D.Cross(Tangent(s), new Point3D(0, 1, 0)) * (Outer + 3));
        }

        return places;
    }

    /// <summary>Creates a smooth knot of tube with about a million triangles, from points shared by the triangles around them.</summary>
    /// <param name="along">The number of steps along the knot.</param>
    /// <param name="around">The number of steps around the tube.</param>
    /// <returns>The model.</returns>
    public static ModelFileData CreateKnot(int along = 4000, int around = 125)
    {
        // Carry a frame along so that the tube does not twist, then spread the turn it has after one round over the
        // whole way, so that the end meets the start without a seam.
        var frames = new Point3D[along + 1];
        frames[0] = Normal(Knot(0), Knot(1e-3));
        for (var i = 1; i <= along; i++)
        {
            var tangent = KnotTangent(2 * Math.PI * i / along);
            frames[i] = (frames[i - 1] - (tangent * Point3D.Dot(frames[i - 1], tangent))).Normalized();
        }

        var start = KnotTangent(0);
        var turn = Math.Atan2(Point3D.Dot(Point3D.Cross(frames[0], frames[along]), start), Point3D.Dot(frames[0], frames[along]));
        var positions = new Point3D[along * around];
        for (var i = 0; i < along; i++)
        {
            var t = 2 * Math.PI * i / along;
            var middle = Knot(t);
            var tangent = KnotTangent(t);
            var frame = frames[i];
            var side = Point3D.Cross(tangent, frame);
            for (var j = 0; j < around; j++)
            {
                var angle = (2 * Math.PI * j / around) - (turn * i / along);
                positions[(i * around) + j] = middle + (((frame * Math.Cos(angle)) + (side * Math.Sin(angle))) * 0.42);
            }
        }

        var triangles = new int[along * around * 6];
        var at = 0;
        for (var i = 0; i < along; i++)
        {
            for (var j = 0; j < around; j++)
            {
                var (a, b) = ((i * around) + j, (i * around) + ((j + 1) % around));
                var (c, d) = ((((i + 1) % along) * around) + ((j + 1) % around), (((i + 1) % along) * around) + j);
                (triangles[at], triangles[at + 1], triangles[at + 2]) = (a, b, c);
                (triangles[at + 3], triangles[at + 4], triangles[at + 5]) = (a, c, d);
                at += 6;
            }
        }

        return new ModelFileData(positions, triangles, null);
    }

    private static Point3D KnotTangent(double t) => (Knot(t + 1e-4) - Knot(t - 1e-4)).Normalized();

    // A knot that winds three times around and twice through a ring.
    private static Point3D Knot(double t) =>
        new((2 + Math.Cos(3 * t)) * Math.Cos(2 * t), (2 + Math.Cos(3 * t)) * Math.Sin(2 * t), Math.Sin(3 * t));

    private static Point3D Normal(Point3D a, Point3D b)
    {
        var tangent = (b - a).Normalized();
        return Point3D.Cross(tangent, new Point3D(0, 0, 1)).Normalized();
    }

    // The middle of the pipe: along x, a quarter circle upward, then along z.
    private static Point3D Middle(double s)
    {
        if (s <= Leg)
        {
            return new Point3D(s, 0, 0);
        }

        var angle = (s - Leg) / Bend;
        if (angle <= Math.PI / 2)
        {
            return new Point3D(Leg + (Bend * Math.Sin(angle)), 0, Bend - (Bend * Math.Cos(angle)));
        }

        return new Point3D(Leg + Bend, 0, Bend + (s - Leg - (Bend * Math.PI / 2)));
    }

    private static Point3D Tangent(double s)
    {
        var angle = Math.Clamp((s - Leg) / Bend, 0, Math.PI / 2);
        return new Point3D(Math.Cos(angle), 0, Math.Sin(angle));
    }

    private static double Along(Point3D place)
    {
        if (place.X <= Leg)
        {
            return place.X;
        }

        if (place.Z <= Bend)
        {
            return Leg + (Bend * Math.Atan2(place.X - Leg, Bend - place.Z));
        }

        return Leg + (Bend * Math.PI / 2) + (place.Z - Bend);
    }

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
                if (outward)
                {
                    corners.AddRange([p0, q1, p1, p0, q0, q1]);
                }
                else
                {
                    corners.AddRange([p0, p1, q1, p0, q1, q0]);
                }
            }
        }
    }

    private static Point3D Ring(double s, double radius, int step, int around)
    {
        var angle = 2 * Math.PI * step / around;
        var tangent = Tangent(s);
        var up = new Point3D(0, 1, 0);
        var side = Point3D.Cross(tangent, up);
        return Middle(s) + (((up * Math.Cos(angle)) + (side * Math.Sin(angle))) * radius);
    }

    private static byte[] ToStl(List<Point3D> corners)
    {
        var count = corners.Count / 3;
        var bytes = new byte[84 + (count * 50)];
        "pipe bend, made in code"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), (uint)count);
        for (var i = 0; i < corners.Count; i++)
        {
            var at = 84 + ((i / 3) * 50) + 12 + ((i % 3) * 12);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), (float)corners[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), (float)corners[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), (float)corners[i].Z);
        }

        return bytes;
    }
}
