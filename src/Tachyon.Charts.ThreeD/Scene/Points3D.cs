using System.Numerics;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>
/// Points in space in a ring: when it is full, the oldest point gives way. Any thread may write, and the chart reads
/// only what is new. A NaN in a coordinate is a gap.
/// </summary>
public sealed class Points3D
{
    private const int FineBlock = 256;
    private const int CoarseBlock = 65_536;
    private const int ParallelCount = 100_000;
    private const byte OpaqueCode = 255;
    private const double SizeStep = 0.25;

    private readonly Lock _gate = new();
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _z;
    private readonly Rgba8[] _colors;
    private readonly PointBlocks _fine;
    private readonly PointBlocks _coarse;
    private long _total;

    /// <summary>Initializes a new instance of the <see cref="Points3D"/> class.</summary>
    /// <param name="capacity">How many points it keeps.</param>
    public Points3D(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        (_x, _y, _z, _colors) = (new double[capacity], new double[capacity], new double[capacity], new Rgba8[capacity]);
        _fine = new PointBlocks(capacity, FineBlock);
        _coarse = new PointBlocks(capacity, CoarseBlock);
    }

    /// <summary>Gets how many points it keeps.</summary>
    public int Capacity { get; }

    /// <summary>Gets how many points were ever added.</summary>
    public long Total => Interlocked.Read(ref _total);

    /// <summary>Gets how many points it holds.</summary>
    public int Count => (int)Math.Min(Total, Capacity);

    /// <summary>Reads a point that it holds, from the oldest at 0 to the newest at <see cref="Count"/> - 1.</summary>
    /// <param name="index">The number of the point.</param>
    /// <returns>The point.</returns>
    internal Point3D Read(int index)
    {
        lock (_gate)
        {
            var slot = (int)((Math.Max(_total - Capacity, 0) + index) % Capacity);
            return new Point3D(_x[slot], _y[slot], _z[slot]);
        }
    }

    /// <summary>Creates a list from arrays.</summary>
    /// <param name="x">The first coordinates.</param>
    /// <param name="y">The second coordinates.</param>
    /// <param name="z">The third coordinates.</param>
    /// <param name="colors">The colors, or <see langword="null"/> for the color of the line.</param>
    /// <returns>The points.</returns>
    public static Points3D From(double[] x, double[] y, double[] z, Rgba8[]? colors = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (x.Length != y.Length || x.Length != z.Length || (colors is not null && colors.Length != x.Length))
        {
            throw new ArgumentException("All lists need the same length.", nameof(y));
        }

        var points = new Points3D(Math.Max(x.Length, 1));
        for (var i = 0; i < x.Length; i++)
        {
            points.Append(x[i], y[i], z[i], colors?[i] ?? default);
        }

        return points;
    }

    /// <summary>Adds a point.</summary>
    /// <param name="x">The first coordinate.</param>
    /// <param name="y">The second coordinate.</param>
    /// <param name="z">The third coordinate.</param>
    /// <param name="color">Its color, or the default for the color of the line.</param>
    public void Append(double x, double y, double z, Rgba8 color = default)
    {
        lock (_gate)
        {
            Store(x, y, z, color);
        }
    }

    /// <summary>Adds many points at once, which suits a measuring thread: the chart waits for a whole batch only once.</summary>
    /// <param name="x">The first coordinates.</param>
    /// <param name="y">The second coordinates.</param>
    /// <param name="z">The third coordinates.</param>
    /// <param name="colors">The colors, or an empty list for the color of the cloud.</param>
    public void Append(ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<double> z, ReadOnlySpan<Rgba8> colors = default)
    {
        if (x.Length != y.Length || x.Length != z.Length || (colors.Length != 0 && colors.Length != x.Length))
        {
            throw new ArgumentException("All lists need the same length.", nameof(y));
        }

        lock (_gate)
        {
            for (var i = 0; i < x.Length; i++)
            {
                Store(x[i], y[i], z[i], colors.Length == 0 ? default : colors[i]);
            }
        }
    }

    /// <summary>Adds a point with a size of its own, for a <see cref="Scatter3D"/>.</summary>
    /// <param name="x">The first coordinate.</param>
    /// <param name="y">The second coordinate.</param>
    /// <param name="z">The third coordinate.</param>
    /// <param name="color">Its color.</param>
    /// <param name="size">Its diameter in device-independent units, up to 63.</param>
    public void Append(double x, double y, double z, Rgba8 color, double size) =>
        Append(x, y, z, new Rgba8(color.R, color.G, color.B, ToSizeCode(size)));

    /// <summary>Interrupts a line: the next point does not connect to the last one.</summary>
    public void AppendGap() => Append(double.NaN, double.NaN, double.NaN);

    /// <summary>Copies points in box coordinates. A big copy runs on all cores.</summary>
    /// <param name="first">The number of the first point. It must still be in the ring.</param>
    /// <param name="target">Receives the points.</param>
    /// <param name="count">How many.</param>
    /// <param name="map">The way into the box.</param>
    /// <param name="fallback">The color of points without one.</param>
    /// <param name="size">The code of the size of points without one. The alpha carries it, as lines and points are opaque.</param>
    internal void CopyTo(long first, LineVertex[] target, int count, in BoxMap map, Rgba8 fallback, byte size = OpaqueCode)
    {
        lock (_gate)
        {
            if (count < ParallelCount)
            {
                CopyRange(first, target, 0, count, map, fallback, size);
            }
            else
            {
                CopyParallel(first, target, count, map, fallback, size);
            }
        }
    }

    /// <summary>Finds the point of a line nearest to a ray.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="reach">How far from the ray a line still counts.</param>
    /// <param name="map">The way into the box.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance, which becomes the distance of the hit.</param>
    /// <param name="found">The point in the units of the axes and in the box.</param>
    /// <returns>Whether a line lies at the ray.</returns>
    internal bool FindOnLine(in Ray3D ray, in PickReach reach, in BoxMap map, double near, ref double far, out (Point3D Data, Point3D Box) found)
    {
        lock (_gate)
        {
            found = default;
            var first = Math.Max(_total - Capacity, 0);
            var best = new LineHit(double.PositiveInfinity, far, default, default);
            var margin = reach.At(far);
            for (var block = first - (first % CoarseBlock); block < _total; block += CoarseBlock)
            {
                if (_coarse.Meets(block, ray, map, margin, near, far))
                {
                    var end = Math.Min(block + CoarseBlock, _total);
                    for (var fine = block; fine < end; fine += FineBlock)
                    {
                        if (_fine.Meets(fine, ray, map, margin, near, far))
                        {
                            FindInBlock(ray, reach, map, Math.Max(fine, first + 1), Math.Min(fine + FineBlock, _total), (near, far), ref best);
                        }
                    }
                }
            }

            far = best.Along;
            found = (best.Data, best.Box);
            return double.IsFinite(best.Offset);
        }
    }

    /// <summary>Gets the distance between a ray and a line piece.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="a">The start of the piece.</param>
    /// <param name="b">The end of the piece.</param>
    /// <param name="along">The place on the ray.</param>
    /// <param name="share">The place on the piece from 0 to 1.</param>
    /// <returns>The distance.</returns>
    internal static double Distance(in Ray3D ray, in Point3D a, in Point3D b, out double along, out double share)
    {
        var (piece, offset) = (b - a, ray.Start - a);
        var (length, turn, side, reach) = (Point3D.Dot(piece, piece), Point3D.Dot(piece, ray.Direction), Point3D.Dot(ray.Direction, offset), Point3D.Dot(piece, offset));
        var denominator = length - (turn * turn);
        share = denominator > 1e-18 ? Math.Clamp((reach - (side * turn)) / denominator, 0, 1) : 0;
        along = (share * turn) - side;
        return ((a + (piece * share)) - ray.At(along)).Length;
    }

    /// <summary>Turns a diameter into the code for the GPU.</summary>
    /// <param name="size">The diameter.</param>
    /// <returns>A code that an opaque color never has.</returns>
    internal static byte ToSizeCode(double size) => (byte)Math.Clamp(Math.Round(size / SizeStep), 1, OpaqueCode - 1);

    /// <summary>Reads a point by its place in the ring.</summary>
    /// <param name="slot">The place.</param>
    /// <returns>The point.</returns>
    internal Point3D GetSlot(int slot) => new(_x[slot], _y[slot], _z[slot]);

    /// <summary>Gets a coordinate of a stored point.</summary>
    /// <param name="row">The number of the point.</param>
    /// <returns>The point.</returns>
    private Point3D Get(long row)
    {
        var slot = (int)(row % Capacity);
        return new Point3D(_x[slot], _y[slot], _z[slot]);
    }

    private void Store(double x, double y, double z, Rgba8 color)
    {
        var row = _total;
        var slot = (int)(row % Capacity);
        (_x[slot], _y[slot], _z[slot], _colors[slot]) = (x, y, z, color);
        var point = new Point3D(x, y, z);
        _fine.Add(row, point, this);
        _coarse.Add(row, point, this);
        Interlocked.Exchange(ref _total, row + 1);
    }

    private void CopyRange(long first, LineVertex[] target, int from, int to, in BoxMap map, Rgba8 fallback, byte size)
    {
        for (var i = from; i < to; i++)
        {
            var slot = (int)((first + i) % Capacity);
            var color = _colors[slot].A == 0 ? fallback : _colors[slot];
            color = color.A == OpaqueCode ? new Rgba8(color.R, color.G, color.B, size) : color;
            target[i] = new LineVertex(map.ToGpu(_x[slot], _y[slot], _z[slot]), color);
        }
    }

    private void CopyParallel(long first, LineVertex[] target, int count, BoxMap map, Rgba8 fallback, byte size)
    {
        var parts = Environment.ProcessorCount;
        Parallel.For(0, parts, part => CopyRange(first, target, (int)((long)count * part / parts), (int)((long)count * (part + 1) / parts), map, fallback, size));
    }

    // The line nearest to the middle of the pointer wins, not the first within reach.
    private void FindInBlock(in Ray3D ray, in PickReach reach, in BoxMap map, long from, long to, (double Near, double Far) limits, ref LineHit best)
    {
        for (var row = from; row < to; row++)
        {
            var (a, b) = (Get(row - 1), Get(row));
            var (boxA, boxB) = (map.ToBox(a), map.ToBox(b));
            var distance = Distance(ray, boxA, boxB, out var along, out var share);
            if (distance < best.Offset && distance <= reach.At(along) && along >= limits.Near && along <= limits.Far)
            {
                best = share < 0.5 ? new LineHit(distance, along, a, boxA) : new LineHit(distance, along, b, boxB);
            }
        }
    }

    private readonly record struct LineHit(double Offset, double Along, Point3D Data, Point3D Box);

    /// <summary>The bounds of runs of points, kept while writing, so that a search skips what lies far from the ray.</summary>
    private sealed class PointBlocks(int capacity, int size)
    {
        private readonly int _count = (capacity / size) + 2;
        private readonly double[] _bounds = new double[((capacity / size) + 2) * 6];

        public void Add(long row, in Point3D point, Points3D owner)
        {
            var at = (int)(row / size % _count) * 6;
            if (row % size == 0)
            {
                Array.Fill(_bounds, double.PositiveInfinity, at, 3);
                Array.Fill(_bounds, double.NegativeInfinity, at + 3, 3);
                if (row > 0)
                {
                    Grow(at, owner.Get(row - 1));
                }
            }

            Grow(at, point);
        }

        public bool Meets(long row, in Ray3D ray, in BoxMap map, double margin, double near, double far)
        {
            var at = (int)(row / size % _count) * 6;
            if (_bounds[at] > _bounds[at + 3])
            {
                return false;
            }

            var a = map.ToBox(new Point3D(_bounds[at], _bounds[at + 1], _bounds[at + 2]));
            var b = map.ToBox(new Point3D(_bounds[at + 3], _bounds[at + 4], _bounds[at + 5]));
            return Ray3D.Limit1(ray.Start.X, ray.Direction.X, Math.Min(a.X, b.X) - margin, Math.Max(a.X, b.X) + margin, ref near, ref far)
                && Ray3D.Limit1(ray.Start.Y, ray.Direction.Y, Math.Min(a.Y, b.Y) - margin, Math.Max(a.Y, b.Y) + margin, ref near, ref far)
                && Ray3D.Limit1(ray.Start.Z, ray.Direction.Z, Math.Min(a.Z, b.Z) - margin, Math.Max(a.Z, b.Z) + margin, ref near, ref far);
        }

        private void Grow(int at, in Point3D point)
        {
            if (double.IsFinite(point.X + point.Y + point.Z))
            {
                (_bounds[at], _bounds[at + 1], _bounds[at + 2]) = (Math.Min(_bounds[at], point.X), Math.Min(_bounds[at + 1], point.Y), Math.Min(_bounds[at + 2], point.Z));
                (_bounds[at + 3], _bounds[at + 4], _bounds[at + 5]) = (Math.Max(_bounds[at + 3], point.X), Math.Max(_bounds[at + 4], point.Y), Math.Max(_bounds[at + 5], point.Z));
            }
        }
    }
}

/// <summary>How far from a ray something still counts: the picture of a pixel grows with the distance.</summary>
/// <param name="Base">The reach at the start of the ray.</param>
/// <param name="Growth">How much it grows per unit of distance.</param>
internal readonly record struct PickReach(double Base, double Growth)
{
    /// <summary>The number of pixels the reach stands for.</summary>
    public const double Pixels = 6;

    /// <summary>Gets the reach at a distance.</summary>
    /// <param name="distance">The distance.</param>
    /// <returns>The reach.</returns>
    public double At(double distance) => Base + (Growth * Math.Max(distance, 0));
}

/// <summary>The way from the units of the axes into the box.</summary>
/// <param name="scene">The scene.</param>
internal readonly struct BoxMap(Scene3D scene)
{
    private readonly LinearAxisTransform _x = scene.XAxis.CreateTransform(-1, 2);
    private readonly LinearAxisTransform _y = scene.YAxis.CreateTransform(-1, 2);
    private readonly LinearAxisTransform _z = scene.ZAxis.CreateTransform(-1, 2);
    private readonly double _signX = scene.XAxis.IsHorizontal ? 1 : -1;
    private readonly double _signY = scene.YAxis.IsHorizontal ? 1 : -1;
    private readonly double _signZ = scene.ZAxis.IsHorizontal ? 1 : -1;

    /// <summary>Maps a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The place in the box.</returns>
    public Point3D ToBox(in Point3D point) => new(_signX * _x.ToPixel(point.X), _signY * _y.ToPixel(point.Y), _signZ * _z.ToPixel(point.Z));

    /// <summary>Maps a point for the GPU.</summary>
    /// <param name="x">The first coordinate.</param>
    /// <param name="y">The second coordinate.</param>
    /// <param name="z">The third coordinate.</param>
    /// <returns>The place, or the mark for a gap.</returns>
    public Vector3 ToGpu(double x, double y, double z)
    {
        var place = new Vector3((float)(_signX * _x.ToPixel(x)), (float)(_signY * _y.ToPixel(y)), (float)(_signZ * _z.ToPixel(z)));
        return float.IsFinite(place.X + place.Y + place.Z) ? place : TraceRing.Gap;
    }
}

/// <summary>A line through points in space, as wide on the screen from every distance.</summary>
/// <param name="points">The points.</param>
public sealed class Line3D(Points3D points)
{
    /// <summary>Gets a value indicating whether the values show as labels above their points; labels that would cover others are left out.</summary>
    public bool ShowsValues { get; init; }

    /// <summary>Gets the points.</summary>
    public Points3D Points { get; } = points ?? throw new ArgumentNullException(nameof(points));

    /// <summary>Gets the color of the points without one.</summary>
    public Rgba8 Color { get; init; } = new(90, 170, 255, 255);

    /// <summary>Gets the width in device-independent units.</summary>
    public double Width { get; init; } = 1.5;

    /// <summary>
    /// Gets a value indicating whether the line shows over everything else in the scene instead of hiding behind what
    /// lies in front of it, as marks and measurements do.
    /// </summary>
    public bool DrawsOnTop { get; init; }
}
