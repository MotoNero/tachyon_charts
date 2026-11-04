using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>A cut through a raster along a plane, ready for a 2D chart.</summary>
/// <param name="Along">The way along the cut in box units, starting at 0.</param>
/// <param name="X">The first coordinate of each place.</param>
/// <param name="Y">The second coordinate of each place.</param>
/// <param name="Values">The value at each place, NaN where it is missing.</param>
public readonly record struct CutCurve(double[] Along, double[] X, double[] Y, double[] Values);

/// <summary>
/// A plane through the box. What lies on the far side of its normal disappears, for every kind of data. The plane
/// lives in box coordinates, where all axes span -1 to 1, so that it can stand at any angle. The chart moves it when
/// its outline is dragged.
/// </summary>
public sealed class ClipPlane3D
{
    private const double Reach = 1.7321;
    private const double Tiny = 1e-9;

    private double _offset;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="ClipPlane3D"/> class.</summary>
    /// <param name="normal">The direction to the side that stays.</param>
    /// <param name="offset">The distance of the plane from the middle of the box, along the normal.</param>
    public ClipPlane3D(Point3D normal, double offset = 0)
    {
        if (normal.Length < Tiny)
        {
            throw new ArgumentException("A plane needs a direction.", nameof(normal));
        }

        Normal = normal.Normalized();
        _offset = offset;
    }

    /// <summary>Gets the direction to the side that stays, with the length 1.</summary>
    public Point3D Normal { get; }

    /// <summary>Gets or sets the distance of the plane from the middle of the box.</summary>
    public double Offset
    {
        get => Volatile.Read(ref _offset);
        set
        {
            Volatile.Write(ref _offset, Math.Clamp(value, -Reach, Reach));
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Gets a value indicating whether the far side disappears. Without it the plane only gives its cuts.</summary>
    public bool Hides { get; init; } = true;

    /// <summary>Gets a number that changes when the plane moves.</summary>
    internal long Version => Interlocked.Read(ref _version);

    /// <summary>Cuts a surface. It works for planes that stand upright, as a height needs one place per way along the cut.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="scene">The scene with the axes.</param>
    /// <returns>The curve, empty for a plane that does not stand upright or misses the surface.</returns>
    public CutCurve Cut(Surface3D surface, Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(scene);
        var (columns, rows) = (surface.Data.Width, surface.Data.Height);
        var place = GridPlace.Create(scene, surface.X, surface.Y, columns, rows);
        return Cut(place, surface.X, surface.Y, (x, y) => Between(surface, x, y));
    }

    /// <summary>Cuts columns. It works for planes that stand upright.</summary>
    /// <param name="bars">The columns.</param>
    /// <param name="scene">The scene with the axes.</param>
    /// <returns>The curve, empty for a plane that does not stand upright or misses the columns.</returns>
    public CutCurve Cut(Bars3D bars, Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(scene);
        var place = bars.Cells(scene);
        return Cut(place, bars.X, bars.Y, (x, y) => bars.Data[Math.Min((int)x, bars.Data.Width - 1), Math.Min((int)y, bars.Data.Height - 1)]);
    }

    /// <summary>Gets the corners of the plane inside the box, in order around it.</summary>
    /// <param name="corners">Receives the corners.</param>
    internal void AddOutline(List<Point3D> corners)
    {
        corners.Clear();
        var offset = Offset;
        for (var corner = 0; corner < 8; corner++)
        {
            var a = Corner(corner);
            for (var axis = 1; axis <= 4; axis *= 2)
            {
                // Each edge of the box once: from the corner on its low side.
                if ((corner & axis) == 0)
                {
                    AddCrossing(corners, a, Corner(corner | axis), offset);
                }
            }
        }

        SortAround(corners);
    }

    // The corners of the box from -1 to 1, one bit per axis.
    private static Point3D Corner(int bits) => new(Side(bits, 1), Side(bits, 2), Side(bits, 4));

    private static double Side(int bits, int axis) => (bits & axis) == 0 ? -1 : 1;

    private static double Value(Surface3D surface, int column, int row) => surface.Data[column, row];

    private static double Between(Surface3D surface, double x, double y)
    {
        var (column, row) = (Math.Min((int)x, surface.Data.Width - 2), Math.Min((int)y, surface.Data.Height - 2));
        var (right, back) = (x - column, y - row);
        var front = (Value(surface, column, row) * (1 - right)) + (Value(surface, column + 1, row) * right);
        var rear = (Value(surface, column, row + 1) * (1 - right)) + (Value(surface, column + 1, row + 1) * right);
        return (front * (1 - back)) + (rear * back);
    }

    private void AddCrossing(List<Point3D> corners, in Point3D a, in Point3D b, double offset)
    {
        var (first, second) = (Point3D.Dot(a, Normal) - offset, Point3D.Dot(b, Normal) - offset);
        if ((first < 0) != (second < 0))
        {
            corners.Add(a + ((b - a) * (first / (first - second))));
        }
    }

    private void SortAround(List<Point3D> corners)
    {
        if (corners.Count < 3)
        {
            return;
        }

        var middle = new Point3D(0, 0, 0);
        foreach (var corner in corners)
        {
            middle += corner * (1.0 / corners.Count);
        }

        var side = Math.Abs(Normal.X) < 0.9 ? new Point3D(1, 0, 0) : new Point3D(0, 1, 0);
        var u = Point3D.Cross(Normal, side).Normalized();
        var v = Point3D.Cross(Normal, u);
        corners.Sort((a, b) => Angle(a, middle, u, v).CompareTo(Angle(b, middle, u, v)));
    }

    private static double Angle(in Point3D corner, in Point3D middle, in Point3D u, in Point3D v) =>
        Math.Atan2(Point3D.Dot(corner - middle, v), Point3D.Dot(corner - middle, u));

    private CutCurve Cut(in GridPlace place, DoubleRange x, DoubleRange y, Func<double, double, double> read)
    {
        CutCurve empty = new([], [], [], []);
        if (Math.Abs(Normal.Z) > Tiny)
        {
            return empty;
        }

        var (startX, startY) = ((Normal.X * Offset) - place.Left, (Normal.Y * Offset) - place.Front);
        (startX, startY) = (startX / place.StepX, startY / place.StepY);
        var (wayX, wayY) = (-Normal.Y / place.StepX, Normal.X / place.StepY);

        // The curve runs along rising coordinates, which is what a 2D line needs.
        if (wayX < -Tiny || (Math.Abs(wayX) <= Tiny && wayY < 0))
        {
            (wayX, wayY) = (-wayX, -wayY);
        }
        var (near, far) = (double.NegativeInfinity, double.PositiveInfinity);
        if (!Ray3D.Limit1(startX, wayX, 0, place.Columns - 1, ref near, ref far) || !Ray3D.Limit1(startY, wayY, 0, place.Rows - 1, ref near, ref far))
        {
            return empty;
        }

        var count = Math.Max(place.Columns + place.Rows, 2);
        var curve = new CutCurve(new double[count], new double[count], new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            var distance = near + ((far - near) * i / (count - 1));
            var (column, row) = (Math.Clamp(startX + (wayX * distance), 0, place.Columns - 1), Math.Clamp(startY + (wayY * distance), 0, place.Rows - 1));
            curve.Along[i] = distance - near;
            curve.X[i] = x.Min + ((x.Max - x.Min) * column / (place.Columns - 1));
            curve.Y[i] = y.Min + ((y.Max - y.Min) * row / (place.Rows - 1));
            curve.Values[i] = read(column, row);
        }

        return curve;
    }
}
