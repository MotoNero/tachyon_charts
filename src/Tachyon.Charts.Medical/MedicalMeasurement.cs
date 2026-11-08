namespace Tachyon.Charts.Medical;

/// <summary>The kinds of measurements on a medical image.</summary>
public enum MedicalMeasurementKind
{
    /// <summary>The distance between two points.</summary>
    Distance,

    /// <summary>The angle at the second of three points.</summary>
    Angle,

    /// <summary>A circle around its first point through its second, with the statistics of the pixels inside.</summary>
    Circle,

    /// <summary>A rectangle between two corners, with the statistics of the pixels inside.</summary>
    Rectangle,

    /// <summary>A closed outline of three or more points, with the statistics of the pixels inside.</summary>
    Freehand,
}

/// <summary>A place on a medical image in millimetres: across from its left edge and down from its top edge.</summary>
/// <param name="X">The distance from the left edge in millimetres.</param>
/// <param name="Y">The distance from the top edge in millimetres.</param>
public readonly record struct MedicalPoint(double X, double Y);

/// <summary>
/// What a measurement shows. Values that do not apply to its kind are NaN: a distance has a length, an angle an angle,
/// a region an area and the statistics of the pixels whose middle lies inside.
/// </summary>
/// <param name="Length">The length in millimetres, the circumference for a region.</param>
/// <param name="Angle">The angle in degrees, from 0 to 180.</param>
/// <param name="Area">The area of the region in square millimetres.</param>
/// <param name="PixelCount">The number of pixels inside the region.</param>
/// <param name="Mean">The mean of the pixels inside, in the unit of the image.</param>
/// <param name="StandardDeviation">The standard deviation of the pixels inside.</param>
/// <param name="Minimum">The smallest pixel value inside.</param>
/// <param name="Maximum">The largest pixel value inside.</param>
public readonly record struct MedicalMeasurementResult(
    double Length,
    double Angle,
    double Area,
    int PixelCount,
    double Mean,
    double StandardDeviation,
    double Minimum,
    double Maximum);

/// <summary>
/// A distance, an angle or a region on one slice of a medical image. The points are in millimetres, so a pixel
/// spacing that differs across and down is part of every result.
/// </summary>
public sealed class MedicalMeasurement
{
    private MedicalPoint[] _points;
    private MedicalPoint[] _outline;
    private MedicalMeasurementResult? _result;

    internal MedicalMeasurement(MedicalMeasurementKind kind, IReadOnlyList<MedicalPoint> points, int slice, string name)
    {
        Kind = kind;
        _points = Validate(kind, points);
        _outline = Outline(kind, _points);
        Slice = slice;
        Name = name;
    }

    /// <summary>Gets the kind.</summary>
    public MedicalMeasurementKind Kind { get; }

    /// <summary>Gets the points: two for a distance, a circle and a rectangle, three for an angle, three or more for an outline.</summary>
    public IReadOnlyList<MedicalPoint> Points => Volatile.Read(ref _points);

    /// <summary>Gets the slice the measurement lies on; 0 for a single image.</summary>
    public int Slice { get; }

    /// <summary>Gets or sets the name. Regions with the same name on several slices form one volume.</summary>
    public string Name { get; set; }

    /// <summary>Gets the result, worked out from the image the measurement belongs to.</summary>
    public MedicalMeasurementResult Result => _result ?? Empty;

    internal static MedicalMeasurementResult Empty { get; } = new(double.NaN, double.NaN, double.NaN, 0, double.NaN, double.NaN, double.NaN, double.NaN);

    internal bool IsRegion => Kind is MedicalMeasurementKind.Circle or MedicalMeasurementKind.Rectangle or MedicalMeasurementKind.Freehand;

    internal MedicalPoint[] Snapshot => Volatile.Read(ref _points);

    /// <summary>Gets the outline that is drawn: the points, or the corners of a circle or rectangle. It is made once per change, not per frame.</summary>
    internal MedicalPoint[] OutlineSnapshot => Volatile.Read(ref _outline);

    internal void Move(MedicalPoint[] points, IMedicalMeasurementSource source)
    {
        var valid = Validate(Kind, points);
        Volatile.Write(ref _outline, Outline(Kind, valid));
        Volatile.Write(ref _points, valid);
        Update(source);
    }

    internal void Update(IMedicalMeasurementSource source) => _result = Calculate(Kind, Snapshot, Slice, source);

    /// <summary>Works out the result of a measurement on an image.</summary>
    internal static MedicalMeasurementResult Calculate(MedicalMeasurementKind kind, MedicalPoint[] points, int slice, IMedicalMeasurementSource source)
    {
        switch (kind)
        {
            case MedicalMeasurementKind.Distance:
                return Empty with { Length = Distance(points[0], points[1]) };
            case MedicalMeasurementKind.Angle:
                return Empty with { Angle = AngleAt(points[0], points[1], points[2]) };
            default:
                var outline = Outline(kind, points);
                var statistics = Statistics(outline, slice, source);
                return statistics with { Length = Circumference(kind, points, outline), Area = Area(kind, points, outline) };
        }
    }

    /// <summary>Gets the outline of a region as a closed polygon; a circle becomes a polygon of many corners.</summary>
    internal static MedicalPoint[] Outline(MedicalMeasurementKind kind, MedicalPoint[] points)
    {
        switch (kind)
        {
            case MedicalMeasurementKind.Circle:
                var radius = Distance(points[0], points[1]);
                var corners = new MedicalPoint[96];
                for (var i = 0; i < corners.Length; i++)
                {
                    var angle = 2 * Math.PI * i / corners.Length;
                    corners[i] = new MedicalPoint(points[0].X + (radius * Math.Cos(angle)), points[0].Y + (radius * Math.Sin(angle)));
                }

                return corners;
            case MedicalMeasurementKind.Rectangle:
                var (a, b) = (points[0], points[1]);
                return [a, new MedicalPoint(b.X, a.Y), b, new MedicalPoint(a.X, b.Y)];
            default:
                return points;
        }
    }

    private static double Distance(MedicalPoint a, MedicalPoint b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    private static double AngleAt(MedicalPoint a, MedicalPoint vertex, MedicalPoint c)
    {
        var (ux, uy, vx, vy) = (a.X - vertex.X, a.Y - vertex.Y, c.X - vertex.X, c.Y - vertex.Y);
        var lengths = Math.Sqrt((ux * ux) + (uy * uy)) * Math.Sqrt((vx * vx) + (vy * vy));
        return lengths > 0 ? Math.Acos(Math.Clamp(((ux * vx) + (uy * vy)) / lengths, -1, 1)) * 180 / Math.PI : double.NaN;
    }

    // Exact for a circle and a rectangle; the polygon of an outline otherwise.
    private static double Area(MedicalMeasurementKind kind, MedicalPoint[] points, MedicalPoint[] outline)
    {
        if (kind == MedicalMeasurementKind.Circle)
        {
            var radius = Distance(points[0], points[1]);
            return Math.PI * radius * radius;
        }

        var twice = 0.0;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            twice += (outline[j].X * outline[i].Y) - (outline[i].X * outline[j].Y);
        }

        return Math.Abs(twice) / 2;
    }

    private static double Circumference(MedicalMeasurementKind kind, MedicalPoint[] points, MedicalPoint[] outline)
    {
        if (kind == MedicalMeasurementKind.Circle)
        {
            return 2 * Math.PI * Distance(points[0], points[1]);
        }

        var length = 0.0;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            length += Distance(outline[j], outline[i]);
        }

        return length;
    }

    // The statistics of the pixels inside; Welford's running mean and sum of squares stay exact for large values such as Hounsfield units.
    private static MedicalMeasurementResult Statistics(MedicalPoint[] outline, int slice, IMedicalMeasurementSource source)
    {
        var (count, mean, squares, minimum, maximum) = (0, 0.0, 0.0, double.PositiveInfinity, double.NegativeInfinity);
        ForEachInside(outline, slice, source, value =>
        {
            count++;
            var delta = value - mean;
            mean += delta / count;
            squares += delta * (value - mean);
            (minimum, maximum) = (Math.Min(minimum, value), Math.Max(maximum, value));
        });

        return count == 0
            ? Empty with { PixelCount = 0 }
            : Empty with { PixelCount = count, Mean = mean, StandardDeviation = Math.Sqrt(squares / count), Minimum = minimum, Maximum = maximum };
    }

    /// <summary>Visits the finite values of the pixels whose middle lies inside an outline, row by row between the crossings with its edges.</summary>
    internal static void ForEachInside(MedicalPoint[] outline, int slice, IMedicalMeasurementSource source, Action<double> visit)
    {
        var spacing = source.Spacing;
        var (top, bottom) = (double.PositiveInfinity, double.NegativeInfinity);
        foreach (var point in outline)
        {
            (top, bottom) = (Math.Min(top, point.Y), Math.Max(bottom, point.Y));
        }

        var firstRow = Math.Max(0, (int)Math.Floor((top / spacing.Row) - 0.5));
        var lastRow = Math.Min(source.Height - 1, (int)Math.Ceiling((bottom / spacing.Row) - 0.5));
        var crossings = new List<double>();
        for (var row = firstRow; row <= lastRow; row++)
        {
            var y = (row + 0.5) * spacing.Row;
            crossings.Clear();
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                var (a, b) = (outline[j], outline[i]);
                if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                {
                    crossings.Add(a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y)));
                }
            }

            crossings.Sort();
            for (var k = 0; k + 1 < crossings.Count; k += 2)
            {
                var first = Math.Max(0, (int)Math.Ceiling((crossings[k] / spacing.Column) - 0.5));
                var last = Math.Min(source.Width - 1, (int)Math.Floor((crossings[k + 1] / spacing.Column) - 0.5));
                for (var column = first; column <= last; column++)
                {
                    var value = source.Read(slice, column, row);
                    if (double.IsFinite(value))
                    {
                        visit(value);
                    }
                }
            }
        }
    }

    private static MedicalPoint[] Validate(MedicalMeasurementKind kind, IReadOnlyList<MedicalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var needed = kind switch
        {
            MedicalMeasurementKind.Angle => 3,
            MedicalMeasurementKind.Freehand => -3,
            _ => 2,
        };

        if ((needed > 0 && points.Count != needed) || (needed < 0 && points.Count < -needed))
        {
            throw new ArgumentException($"A {kind} needs {(needed > 0 ? "exactly " + needed : "at least " + -needed)} points, not {points.Count}.", nameof(points));
        }

        var copy = new MedicalPoint[points.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y))
            {
                throw new ArgumentException("The points must be finite.", nameof(points));
            }

            copy[i] = points[i];
        }

        return copy;
    }
}

/// <summary>An image whose pixels a measurement reads: one slice of a single image or of a volume.</summary>
internal interface IMedicalMeasurementSource
{
    /// <summary>Gets the number of columns.</summary>
    int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    int Height { get; }

    /// <summary>Gets the size of a pixel in millimetres.</summary>
    ImageSpacing Spacing { get; }

    /// <summary>Gets the slice shown now; 0 for a single image.</summary>
    int Slice { get; }

    /// <summary>Gets the number of slices; 1 for a single image.</summary>
    int SliceCount { get; }

    /// <summary>Gets the distance between two slices in millimetres, or 0 for a single image.</summary>
    double SliceDistance { get; }

    /// <summary>Gets the unit of the values.</summary>
    string Unit { get; }

    /// <summary>Reads a pixel of a slice.</summary>
    double Read(int slice, int column, int row);

    /// <summary>Gets the smallest and largest value of all pixels.</summary>
    Tachyon.Charts.Core.Numerics.DoubleRange ValueRange { get; }

    /// <summary>Gets the grayscale window shown now.</summary>
    MedicalWindow Window { get; }

    /// <summary>Occurs on the thread of the chart when the window changes.</summary>
    event Action<MedicalWindow>? WindowChanged;

    /// <summary>Occurs on the thread of the chart when another slice may be shown.</summary>
    event Action? SliceChanged;
}
