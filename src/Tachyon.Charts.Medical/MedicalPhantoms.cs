using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// Synthetic CT volumes for trying the package, built from smooth shapes with typical Hounsfield units. Edges blend over
/// about one voxel as in a real scan, and a little noise is added, so the pictures look like a CT and not like a drawing.
/// They hold no data of any person and need no download, so they fit tests, demos and first steps. The values are
/// computed once per process and shared; each call returns a new volume around them.
/// </summary>
public static class MedicalPhantoms
{
    private const int HeadSide = 256;
    private const int HeadDepth = 192;
    private const double HeadSpacing = 0.75;
    private const double HeadSliceSpacing = 1.0;
    private const int ThoraxWidth = 384;
    private const int ThoraxHeight = 288;
    private const int ThoraxDepth = 200;
    private const double ThoraxSpacing = 1.0;
    private const double ThoraxSliceSpacing = 1.5;
    private const int Intercept = -1024;

    private static readonly PatientOrientation _axial = new(new Point3D(1, 0, 0), new Point3D(0, 1, 0));
    private static readonly Lazy<ushort[]> _head = new(CreateHead);
    private static readonly Lazy<ushort[]> _thorax = new(CreateThorax);

    /// <summary>
    /// Creates a head of 256 × 256 × 192 values at 0.75 mm across and 1 mm between slices: scalp, skull, a rim of fluid,
    /// grey matter, folded white matter, two ventricles and a dense round lesion. Window 40/80 HU.
    /// </summary>
    /// <returns>The volume.</returns>
    public static MedicalVolume Head() => new(
        new VolumeData(_head.Value, HeadSide, HeadSide, HeadDepth, 1, Intercept),
        new ImageSpacing(HeadSpacing, HeadSpacing),
        HeadSliceSpacing,
        default,
        _axial,
        40,
        80,
        "CT");

    /// <summary>
    /// Creates a thorax of 384 × 288 × 200 values at 1 mm across and 1.5 mm between slices: lungs with a branching tree of
    /// vessels and a nodule, heart, aorta, spine, ribs and sternum in fat and muscle. Window 40/400 HU; for the lungs
    /// −600/1500.
    /// </summary>
    /// <returns>The volume.</returns>
    public static MedicalVolume Thorax() => new(
        new VolumeData(_thorax.Value, ThoraxWidth, ThoraxHeight, ThoraxDepth, 1, Intercept),
        new ImageSpacing(ThoraxSpacing, ThoraxSpacing),
        ThoraxSliceSpacing,
        default,
        _axial,
        40,
        400,
        "CT");

    // All places in millimetres from the middle of the head; x to the left, y backwards, z upwards.
    private static ushort[] CreateHead()
    {
        var values = new ushort[HeadSide * HeadSide * HeadDepth];
        var middle = (HeadSide - 1) * HeadSpacing / 2;
        var height = (HeadDepth - 1) * HeadSliceSpacing / 2;
        Parallel.For(0, HeadDepth, slice =>
        {
            var z = (slice * HeadSliceSpacing) - height;
            for (var row = 0; row < HeadSide; row++)
            {
                var y = (row * HeadSpacing) - middle;
                for (var column = 0; column < HeadSide; column++)
                {
                    var x = (column * HeadSpacing) - middle;
                    var hu = HeadValue(x, y, z) + Noise(column, row, slice, 5);
                    values[(((slice * HeadSide) + row) * HeadSide) + column] = Store(hu);
                }
            }
        });

        return values;
    }

    private static double HeadValue(double x, double y, double z)
    {
        var p = new Point3D(x, y, z);
        const double Edge = HeadSpacing;
        var hu = Blend(-1000, 35, Ellipsoid(p, default, 72, 90, 82), Edge);
        hu = Blend(hu, 1150 + (80 * Math.Sin((x * 0.21) + (z * 0.17))), Ellipsoid(p, default, 68, 86, 78), Edge);
        hu = Blend(hu, 8, Ellipsoid(p, default, 62.5, 80.5, 72.5), Edge);
        hu = Blend(hu, 40 + (2 * Math.Sin((x * 0.4) + (y * 0.3))), Ellipsoid(p, default, 61, 79, 71), Edge);

        // White matter folds in and out under the grey matter.
        var folds = 3.5 * Math.Sin(Math.Atan2(y, x) * 9) * Math.Sin(Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * 7);
        hu = Blend(hu, 27, Ellipsoid(p, new Point3D(0, 4, 4), 48, 62, 52) + folds, Edge);
        hu = Blend(hu, 8, Math.Min(Ellipsoid(p, new Point3D(-9, -2, 8), 6, 26, 12), Ellipsoid(p, new Point3D(9, -2, 8), 6, 26, 12)), Edge);
        return Blend(hu, 68, Ellipsoid(p, new Point3D(-34, -14, 2), 9, 9, 9), Edge);
    }

    // x to the left, y backwards, both from the middle of the slice; z downwards from the top of the chest, in millimetres.
    private static ushort[] CreateThorax()
    {
        var values = new ushort[ThoraxWidth * ThoraxHeight * ThoraxDepth];
        var (middleX, middleY) = ((ThoraxWidth - 1) * ThoraxSpacing / 2, (ThoraxHeight - 1) * ThoraxSpacing / 2);
        var length = (ThoraxDepth - 1) * ThoraxSliceSpacing;
        var vessels = new VesselTree(length);
        Parallel.For(0, ThoraxDepth, slice =>
        {
            // The slices count upwards in the patient, so the last one is the top of the chest.
            var z = length - (slice * ThoraxSliceSpacing);
            for (var row = 0; row < ThoraxHeight; row++)
            {
                var y = (row * ThoraxSpacing) - middleY;
                for (var column = 0; column < ThoraxWidth; column++)
                {
                    var x = (column * ThoraxSpacing) - middleX;
                    var hu = ThoraxValue(new Point3D(x, y, z), length, vessels) + Noise(column, row, slice, 8);
                    values[(((slice * ThoraxHeight) + row) * ThoraxWidth) + column] = Store(hu);
                }
            }
        });

        return values;
    }

    private static double ThoraxValue(Point3D p, double length, VesselTree vessels)
    {
        const double Edge = ThoraxSpacing;
        var (x, y, z) = (p.X, p.Y, p.Z);
        var hu = Blend(-1000, -100, Ellipse(x, y, 0, 0, 177, 95), Edge);
        hu = Blend(hu, 45 + (6 * Math.Sin((x * 0.02) + (z * 0.015))), Ellipse(x, y, 0, 0, 164, 88), Edge);
        foreach (var side in (ReadOnlySpan<double>)[-1, 1])
        {
            hu = Lung(p, side, length, vessels, hu);
        }

        // Ribs: slabs of bone inside the wall, one every 27 mm, open at the back where the spine is.
        var wall = Math.Max(Ellipse(x, y, 0, 0, 158, 85), -Ellipse(x, y, 0, 0, 150, 80.6));
        var slab = Math.Abs((((z % 27) + 27) % 27) - 13.5) - 5;
        hu = Blend(hu, 900, Math.Max(Math.Max(wall, slab), y - 65), Edge);

        // Spine: a body with a dense rim around the spinal canal, behind; the sternum in front.
        hu = Blend(hu, 1100, Ellipse(x, y, 0, 65, 25, 17), Edge);
        hu = Blend(hu, 350, Ellipse(x, y, 0, 65, 21, 14), Edge);
        hu = Blend(hu, 15, Ellipse(x, y, 0, 72, 9.6, 5.8), Edge);
        hu = Blend(hu, 600, Math.Max(Math.Abs(x) - 11.5, Math.Abs(y + 79) - 7), Edge);

        // The heart sits low and to the left with blood inside, pressing into the left lung; the aorta descends beside the spine.
        var heart = new Point3D(23, -11.5, length * 0.7);
        hu = Blend(hu, 45, Ellipsoid(p, heart, 52, 35, length * 0.35), Edge);
        hu = Blend(hu, 180, Ellipsoid(p, heart, 31, 20, length * 0.21), Edge);
        return Blend(hu, 200, Ellipse(x, y, -23, 40, 13.4, 10), Edge);
    }

    // A dome from the apex near the top to the diaphragm, which rises under the heart; vessels branch from the hilum.
    private static double Lung(Point3D p, double side, double length, VesselTree vessels, double around)
    {
        const double Edge = ThoraxSpacing;
        var (x, y, z) = (p.X, p.Y, p.Z);
        var centre = side * 86;
        var across = Math.Clamp((x - centre) / 63, -1.5, 1.5);
        var lung = Math.Max(Ellipsoid(p, new Point3D(centre, -3, length * 0.5), 63, 66, length * 0.45), z - (length * (0.9 - (0.12 * across * across))));
        if (lung > 2 * Edge)
        {
            return around;
        }

        var inside = Blend(-860 + (20 * Math.Sin((x * 0.03) + (y * 0.025) + (z * 0.02))), 35, vessels.Distance(p), Edge);
        if (side < 0)
        {
            inside = Blend(inside, 40, Ellipsoid(p, new Point3D(-96, -14, length * 0.4), 6.5, 5.5, 6), Edge);
        }

        return Blend(around, inside, lung, Edge);
    }

    // Blends towards a value inside a shape: fully more than half a voxel inside, not at all half a voxel outside.
    private static double Blend(double outside, double inside, double distance, double voxel)
    {
        var share = Math.Clamp(0.5 - (distance / voxel), 0, 1);
        return outside + ((inside - outside) * share);
    }

    // About the distance in millimetres from the surface of an ellipsoid, negative inside: its implicit value over its slope.
    private static double Ellipsoid(Point3D p, Point3D centre, double a, double b, double c)
    {
        var (u, v, w) = ((p.X - centre.X) / a, (p.Y - centre.Y) / b, (p.Z - centre.Z) / c);
        var r = Math.Sqrt((u * u) + (v * v) + (w * w));
        var slope = Math.Sqrt(Square(u / a) + Square(v / b) + Square(w / c));
        return r > 1e-9 && slope > 1e-12 ? (r - 1) * r / slope : -Math.Min(a, Math.Min(b, c));
    }

    private static double Ellipse(double x, double y, double cx, double cy, double a, double b) =>
        Ellipsoid(new Point3D(x, y, 0), new Point3D(cx, cy, 0), a, b, double.PositiveInfinity);

    // A fixed, even spread of noise from the place alone, so that every call gives the same volume.
    private static double Noise(int x, int y, int z, double amount)
    {
        var hash = (uint)((x * 73856093) ^ (y * 19349663) ^ (z * 83492791));
        hash ^= hash >> 13;
        hash *= 0x5bd1e995;
        hash ^= hash >> 15;
        var first = (hash & 0xFFFF) / 65535.0;
        var second = (hash >> 16) / 65535.0;
        return (first + second - 1) * amount;
    }

    private static ushort Store(double hu) => (ushort)Math.Clamp(Math.Round(hu - Intercept), 0, ushort.MaxValue);

    private static double Square(double value) => value * value;

    /// <summary>The vessels of both lungs as straight pieces, found through a grid of cells for speed.</summary>
    private sealed class VesselTree
    {
        private const double Cell = 16;
        private readonly (Point3D Start, Point3D End, double Radius)[] _pieces;
        private readonly Dictionary<(int, int, int), int[]> _cells = [];

        public VesselTree(double length)
        {
            var pieces = new List<(Point3D, Point3D, double)>();
            foreach (var side in (ReadOnlySpan<double>)[-1, 1])
            {
                var hilum = new Point3D(side * 45, 0, 0.5 * length);
                foreach (var (dx, dy, dz) in (ReadOnlySpan<(double, double, double)>)[(0.4, 0, -1), (1, 0.2, 0), (0.4, 0.1, 1), (0.5, -1, 0.2), (0.5, 1, 0.2)])
                {
                    Grow(pieces, hilum, new Point3D(side * dx, dy, dz), 40, 6, 6);
                }
            }

            _pieces = [.. pieces];
            var lists = new Dictionary<(int, int, int), List<int>>();
            for (var i = 0; i < _pieces.Length; i++)
            {
                AddToCells(lists, i);
            }

            foreach (var (key, list) in lists)
            {
                _cells[key] = [.. list];
            }
        }

        /// <summary>Gets the distance from the nearest vessel wall, negative inside; large far away.</summary>
        public double Distance(Point3D p)
        {
            if (!_cells.TryGetValue((Index(p.X), Index(p.Y), Index(p.Z)), out var near))
            {
                return Cell;
            }

            var best = Cell;
            foreach (var i in near)
            {
                var (start, end, radius) = _pieces[i];
                var (d, q) = (end - start, p - start);
                var along = Math.Clamp(Point3D.Dot(q, d) / Point3D.Dot(d, d), 0, 1);
                best = Math.Min(best, (q - (d * along)).Length - radius);
            }

            return best;
        }

        private static int Index(double value) => (int)Math.Floor(value / Cell);

        // Two children per piece, turned apart by about 35° around the direction at a fixed but varied angle, each shorter
        // and thinner, down to vessels of about half a millimetre.
        private static void Grow(List<(Point3D, Point3D, double)> pieces, Point3D start, Point3D direction, double length, double radius, int levels)
        {
            var unit = direction * (1 / direction.Length);
            var end = start + (unit * length);
            pieces.Add((start, end, radius));
            if (levels == 0)
            {
                return;
            }

            // Two directions across the piece.
            var helper = Math.Abs(unit.Z) < 0.9 ? new Point3D(0, 0, 1) : new Point3D(1, 0, 0);
            var first = Point3D.Cross(unit, helper);
            first *= 1 / first.Length;
            var second = Point3D.Cross(unit, first);
            var turn = Spread(end) * Math.PI * 2;
            var across = (first * Math.Cos(turn)) + (second * Math.Sin(turn));
            const double Bend = 0.7;
            Grow(pieces, end, unit + (across * Bend), length * 0.78, radius * 0.68, levels - 1);
            Grow(pieces, end, unit - (across * Bend * 0.8), length * 0.72, radius * 0.62, levels - 1);
        }

        // A share from 0 to 1 that depends only on a place, so the tree is the same in every process.
        private static double Spread(Point3D place)
        {
            var hash = (uint)(((int)(place.X * 16) * 73856093) ^ ((int)(place.Y * 16) * 19349663) ^ ((int)(place.Z * 16) * 83492791));
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFF) / (double)0x1000000;
        }

        // Every cell the box around a piece touches, widened by its radius and a little more.
        private void AddToCells(Dictionary<(int, int, int), List<int>> lists, int i)
        {
            var (start, end, radius) = _pieces[i];
            var reach = radius + 2;
            var (low, high) = (new Point3D(Math.Min(start.X, end.X) - reach, Math.Min(start.Y, end.Y) - reach, Math.Min(start.Z, end.Z) - reach),
                new Point3D(Math.Max(start.X, end.X) + reach, Math.Max(start.Y, end.Y) + reach, Math.Max(start.Z, end.Z) + reach));
            for (var cx = Index(low.X); cx <= Index(high.X); cx++)
            {
                for (var cy = Index(low.Y); cy <= Index(high.Y); cy++)
                {
                    for (var cz = Index(low.Z); cz <= Index(high.Z); cz++)
                    {
                        if (!lists.TryGetValue((cx, cy, cz), out var list))
                        {
                            lists[(cx, cy, cz)] = list = [];
                        }

                        list.Add(i);
                    }
                }
            }
        }
    }
}
