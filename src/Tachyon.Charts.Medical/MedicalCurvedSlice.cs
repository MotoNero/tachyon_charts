using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// A section along a line drawn on an orthogonal plane, stretched flat: across goes the length along the line, as for
/// a vessel or a tooth arch, down goes the depth of the volume across the plane, through all of its slices. It follows a
/// distance, an angle or a freehand line while it is drawn or moved, and shares the window of the display.
/// </summary>
public sealed class MedicalCurvedSlice
{
    private readonly MedicalMprSlice _slice;
    private readonly MedicalReformatView _view;
    private readonly Point3D _down;
    private readonly double _top;
    private MedicalMeasurement? _path;

    /// <summary>Initializes a section that follows the lines on an orthogonal plane.</summary>
    /// <param name="slice">The plane the lines are drawn on.</param>
    public MedicalCurvedSlice(MedicalMprSlice slice)
    {
        ArgumentNullException.ThrowIfNull(slice);
        _slice = slice;
        var display = slice.Display;
        var extent = MedicalReformatView.Extent(display.Source);

        // Down runs across the plane: to lower z under an axial plane, along y and x under the others.
        (_down, var depth, _top) = slice.Plane switch
        {
            MedicalMprPlane.Axial => (new Point3D(0, 0, -1), extent.Z, -extent.Z),
            MedicalMprPlane.Coronal => (new Point3D(0, 1, 0), extent.Y, 0.0),
            _ => (new Point3D(1, 0, 0), extent.X, 0.0),
        };
        var (columns, spacing) = MedicalReformatView.Fit(display.Source, extent.Length);
        var rows = Math.Max(2, (int)Math.Ceiling(depth / spacing) + 1);
        var buffer = new MedicalReformatBuffer(display.Source, columns, rows, new ImageSpacing(spacing, spacing));

        // The first row lies half a pixel above the plane's first slice, so the middle of each row meets a slice place.
        _top -= spacing / 2;
        _view = new MedicalReformatView(display, buffer, "Curved", null);
        slice.Measurements.Changed += _ => Update();
        Update();
    }

    /// <summary>Occurs when the section changed.</summary>
    public event Action? Changed
    {
        add => _view.Changed += value;
        remove => _view.Changed -= value;
    }

    /// <summary>Gets or sets the line to follow, or <see langword="null"/> for the line last touched on the plane.</summary>
    /// <exception cref="ArgumentException">The measurement is no distance, angle or freehand line.</exception>
    public MedicalMeasurement? Path
    {
        get => _path;
        set
        {
            if (value is not null && !IsLine(value))
            {
                throw new ArgumentException("A curved section follows a distance, an angle or a freehand line.", nameof(value));
            }

            _path = value;
            Update();
        }
    }

    /// <summary>Gets the line shown now, or <see langword="null"/> when there is none.</summary>
    public MedicalMeasurement? Shown { get; private set; }

    /// <summary>Gets the length of the line shown in millimetres, which is the width of the section that has values.</summary>
    public double Length { get; private set; }

    /// <summary>Gets the size of a pixel in millimetres.</summary>
    public double PixelSpacing => _view.Buffer.Spacing.Column;

    /// <summary>Adds the section to a 2D chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure(ChartBuilder chart) => _view.Configure(chart);

    /// <summary>Reads the picture at a pixel; NaN outside the volume and past the end of the line.</summary>
    internal float Read(int column, int row) => _view.Buffer.Read(column, row);

    /// <summary>Gets the number of rows.</summary>
    internal int Rows => _view.Buffer.Height;

    private static bool IsLine(MedicalMeasurement measurement) =>
        measurement.Kind is MedicalMeasurementKind.Distance or MedicalMeasurementKind.Angle or MedicalMeasurementKind.Freehand;

    private void Update()
    {
        Shown = _path ?? Pick();
        if (Shown is not { } line)
        {
            Length = 0;
            _view.Buffer.Clear();
            _view.RaiseChanged();
            return;
        }

        // On the plane, at the depth where the rows start; the rows then run through the whole volume.
        var buffer = _slice.Buffer;
        var start = _view.Display.Position;
        var path = new Point3D[line.Snapshot.Length];
        for (var i = 0; i < path.Length; i++)
        {
            var place = buffer.ToVolumePosition(line.Snapshot[i].X, line.Snapshot[i].Y, start);
            path[i] = place - (_down * Point3D.Dot(place, _down));
        }

        Length = _view.Buffer.FillCurve(path, _down, _top);
        _view.RaiseChanged();
    }

    private MedicalMeasurement? Pick()
    {
        var layer = _slice.Measurements;
        if (layer.Selected is { } selected && IsLine(selected))
        {
            return selected;
        }

        for (var i = layer.Measurements.Count - 1; i >= 0; i--)
        {
            if (IsLine(layer.Measurements[i]))
            {
                return layer.Measurements[i];
            }
        }

        return null;
    }
}
