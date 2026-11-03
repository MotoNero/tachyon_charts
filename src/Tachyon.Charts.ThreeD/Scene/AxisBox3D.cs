using System.Numerics;
using Tachyon.Charts.Core.Axes;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>The planes of the box that lie behind the data, each as -1 or 1.</summary>
/// <param name="X">The plane of the wall across the X axis.</param>
/// <param name="Y">The plane of the wall across the Y axis.</param>
/// <param name="Z">The plane of the floor.</param>
internal readonly record struct BoxWalls(int X, int Y, int Z);

/// <summary>Where the labels of an axis sit.</summary>
/// <param name="Start">The start of the edge.</param>
/// <param name="End">The end of the edge.</param>
/// <param name="Outward">The direction from the edge away from the box.</param>
internal readonly record struct LabelEdge(Point3D Start, Point3D End, Point3D Outward);

/// <summary>The geometry of the axis box. All of it follows from the eye, so nothing here touches the GPU.</summary>
internal static class AxisBox3D
{
    /// <summary>The length that decides the number of ticks. It is fixed, so the grid stays when the view turns.</summary>
    public const double TickLength = 260;

    private const double CharacterWidth = 7;
    private const double TextHeight = 14;

    /// <summary>Selects the walls: each one lies on the far side of the center.</summary>
    /// <param name="eye">The position of the eye.</param>
    /// <returns>The walls.</returns>
    public static BoxWalls SelectWalls(Point3D eye) => new(eye.X >= 0 ? -1 : 1, eye.Y >= 0 ? -1 : 1, eye.Z >= 0 ? -1 : 1);

    /// <summary>Gets the ticks of an axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The ticks.</returns>
    public static IReadOnlyList<AxisTick> GetTicks(AxisBase axis) => axis.GenerateTicks(TickLength, new FixedMeasurer()).Major;

    /// <summary>Maps a value of an axis to the box.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="value">The value.</param>
    /// <returns>The place between -1 and 1.</returns>
    public static double ToBox(AxisBase axis, double value)
    {
        var place = axis.CreateTransform(-1, 2).ToPixel(value);
        return axis.IsHorizontal ? place : -place;
    }

    /// <summary>Adds the lines of the walls.</summary>
    /// <param name="grid">Receives two points per grid line.</param>
    /// <param name="frame">Receives two points per edge of a wall.</param>
    /// <param name="walls">The walls.</param>
    /// <param name="x">The places of the ticks on the X axis.</param>
    /// <param name="y">The places of the ticks on the Y axis.</param>
    /// <param name="z">The places of the ticks on the Z axis.</param>
    public static void AddLines(List<Vector3> grid, List<Vector3> frame, BoxWalls walls, ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<double> z)
    {
        foreach (var value in x)
        {
            AddLine(grid, new Point3D(value, -1, walls.Z), new Point3D(value, 1, walls.Z));
            AddLine(grid, new Point3D(value, walls.Y, -1), new Point3D(value, walls.Y, 1));
        }

        foreach (var value in y)
        {
            AddLine(grid, new Point3D(-1, value, walls.Z), new Point3D(1, value, walls.Z));
            AddLine(grid, new Point3D(walls.X, value, -1), new Point3D(walls.X, value, 1));
        }

        foreach (var value in z)
        {
            AddLine(grid, new Point3D(walls.X, -1, value), new Point3D(walls.X, 1, value));
            AddLine(grid, new Point3D(-1, walls.Y, value), new Point3D(1, walls.Y, value));
        }

        AddRectangle(frame, new Point3D(-1, -1, walls.Z), new Point3D(2, 0, 0), new Point3D(0, 2, 0));
        AddRectangle(frame, new Point3D(walls.X, -1, -1), new Point3D(0, 2, 0), new Point3D(0, 0, 2));
        AddRectangle(frame, new Point3D(-1, walls.Y, -1), new Point3D(2, 0, 0), new Point3D(0, 0, 2));
    }

    /// <summary>Gets the edge of the X axis: the floor edge next to the eye.</summary>
    /// <param name="walls">The walls.</param>
    /// <returns>The edge.</returns>
    public static LabelEdge XEdge(BoxWalls walls) =>
        new(new Point3D(-1, -walls.Y, walls.Z), new Point3D(1, -walls.Y, walls.Z), new Point3D(0, -walls.Y, 0));

    /// <summary>Gets the edge of the Y axis: the floor edge next to the eye.</summary>
    /// <param name="walls">The walls.</param>
    /// <returns>The edge.</returns>
    public static LabelEdge YEdge(BoxWalls walls) =>
        new(new Point3D(-walls.X, -1, walls.Z), new Point3D(-walls.X, 1, walls.Z), new Point3D(-walls.X, 0, 0));

    /// <summary>Gets the edge of the Z axis: of the two outer vertical edges the one further left in the picture.</summary>
    /// <param name="walls">The walls.</param>
    /// <param name="matrix">The matrix of the camera.</param>
    /// <returns>The edge.</returns>
    public static LabelEdge ZEdge(BoxWalls walls, Matrix3D matrix)
    {
        var first = new Point3D(-walls.X, walls.Y, 0);
        var second = new Point3D(walls.X, -walls.Y, 0);
        var corner = matrix.Project(first).X <= matrix.Project(second).X ? first : second;
        return new LabelEdge(corner with { Z = -1 }, corner with { Z = 1 }, new Point3D(corner.X, corner.Y, 0).Normalized());
    }

    /// <summary>Marks the spans that keep a distance to the one before them.</summary>
    /// <param name="spans">The start and end of each text along one direction, in order of the ticks.</param>
    /// <param name="gap">The smallest distance.</param>
    /// <param name="shown">Receives whether each text is shown.</param>
    public static void KeepApart(ReadOnlySpan<(double Start, double End)> spans, double gap, Span<bool> shown)
    {
        var last = (Start: double.NaN, End: double.NaN);
        for (var i = 0; i < spans.Length; i++)
        {
            var (start, end) = (Math.Min(spans[i].Start, spans[i].End), Math.Max(spans[i].Start, spans[i].End));
            shown[i] = double.IsNaN(last.Start) || start >= last.End + gap || end <= last.Start - gap;
            if (shown[i])
            {
                last = (start, end);
            }
        }
    }

    private static void AddLine(List<Vector3> lines, Point3D from, Point3D to)
    {
        lines.Add(ToVector(from));
        lines.Add(ToVector(to));
    }

    private static void AddRectangle(List<Vector3> lines, Point3D corner, Point3D side, Point3D other)
    {
        AddLine(lines, corner, corner + side);
        AddLine(lines, corner + side, corner + side + other);
        AddLine(lines, corner + side + other, corner + other);
        AddLine(lines, corner + other, corner);
    }

    private static Vector3 ToVector(Point3D point) => new((float)point.X, (float)point.Y, (float)point.Z);

    private sealed class FixedMeasurer : ITextMeasurer
    {
        public double LineHeight => TextHeight;

        public double MeasureWidth(string text) => text.Length * CharacterWidth;
    }
}
