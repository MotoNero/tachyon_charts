using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// The measurements of the planes of a display as lines in 3D, each on the slice it was drawn on: the selected one in
/// its own color. The lines are written anew when a measurement changes; drawing a frame takes nothing from here.
/// </summary>
internal sealed class MedicalMeasurements3D
{
    // The points of all lines; the points buffer keeps a ring of this many, so a write of exactly as many replaces it.
    internal const int Capacity = 8192;

    private static readonly Rgba8 _color = new(255, 214, 10, 255);
    private static readonly Rgba8 _selectedColor = new(80, 220, 255, 255);

    private readonly MedicalMprDisplay _display;
    private readonly Lock _gate = new();

    public MedicalMeasurements3D(MedicalMprDisplay display)
    {
        _display = display;
        Lines = new Line3D(new Points3D(Capacity)) { Color = _color, Width = 2, DrawsOnTop = true };
        Selected = new Line3D(new Points3D(Capacity)) { Color = _selectedColor, Width = 2.5, DrawsOnTop = true };
        foreach (var slice in Slices)
        {
            slice.Measurements.Changed += _ => Refresh();
            slice.Measurements.SelectionChanged += Refresh;
        }

        Refresh();
    }

    /// <summary>Gets the lines of the measurements.</summary>
    public Line3D Lines { get; }

    /// <summary>Gets the line of the selected measurement.</summary>
    public Line3D Selected { get; }

    /// <summary>Gets the points of all measurements on their slices, gaps as NaN, as last written; for tests.</summary>
    internal Point3D[] Written { get; private set; } = [];

    private MedicalMprSlice[] Slices => [_display.Axial, _display.Coronal, _display.Sagittal];

    /// <summary>Writes the lines anew from the measurements of the three planes.</summary>
    public void Refresh()
    {
        var (written, all, selected) = (new List<Point3D>(), new List<Point3D>(), new List<Point3D>());
        foreach (var slice in Slices)
        {
            var layer = slice.Measurements;
            foreach (var measurement in layer.Measurements)
            {
                var points = measurement.IsRegion ? measurement.OutlineSnapshot : measurement.Snapshot;
                var at = slice.SlicePosition(measurement.Slice);
                var line = new List<Point3D>(points.Length + 1);
                foreach (var point in points)
                {
                    line.Add(slice.ToVolumePosition(point.X, point.Y, at));
                }

                // Regions close their outline.
                if (measurement.IsRegion && points.Length > 2)
                {
                    line.Add(line[0]);
                }

                written.AddRange(line);
                written.Add(Gap);

                // Over everything, like the outlines of the planes: a measurement behind a plane stays in sight.
                var target = measurement == layer.Selected ? selected : all;
                target.AddRange(line);
                target.Add(Gap);
            }
        }

        lock (_gate)
        {
            Write(Lines.Points, all);
            Write(Selected.Points, selected);
            Written = [.. written];
        }
    }

    private static Point3D Gap => new(double.NaN, double.NaN, double.NaN);

    // All points of the ring at once: the measurements, then gaps up to the capacity, so nothing old stays.
    private static void Write(Points3D target, List<Point3D> points)
    {
        var (x, y, z) = (new double[Capacity], new double[Capacity], new double[Capacity]);
        var count = Math.Min(points.Count, Capacity);
        for (var i = 0; i < Capacity; i++)
        {
            var point = i < count ? points[i] : new Point3D(double.NaN, double.NaN, double.NaN);
            (x[i], y[i], z[i]) = (point.X, point.Y, point.Z);
        }

        target.Append(x, y, z);
    }
}
