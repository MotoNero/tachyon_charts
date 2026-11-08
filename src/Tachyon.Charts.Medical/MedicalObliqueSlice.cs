using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// A plane through the volume of a <see cref="MedicalMprDisplay"/> at any angle, sampled from the eight nearest voxels,
/// as a 2D chart beside the orthogonal planes. The plane can follow a distance measured on an orthogonal plane, standing
/// upright on it (<see cref="FollowLine"/>), or face the eye of the 3D chart (<see cref="AlignToView"/>, key V with
/// <see cref="ViewCamera"/>); the wheel moves it
/// along its normal. Places are in millimetres of the volume, as <see cref="MedicalMprDisplay.Position"/>.
/// </summary>
public sealed class MedicalObliqueSlice
{
    private static readonly Rgba8 _outlineColor = new(255, 170, 40, 255);

    private readonly MedicalReformatView _view;
    private readonly double _side;
    private readonly List<Points3D> _outlines = [];
    private MedicalMprSlice? _followed;

    /// <summary>Initializes a plane through the position of a display, at first parallel to the axial plane.</summary>
    /// <param name="display">The display whose volume, position and window the plane shares.</param>
    public MedicalObliqueSlice(MedicalMprDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var extent = MedicalReformatView.Extent(display.Source);
        _side = extent.Length;
        var (count, spacing) = MedicalReformatView.Fit(display.Source, _side);
        _side = count * spacing;
        var buffer = new MedicalReformatBuffer(display.Source, count, count, new ImageSpacing(spacing, spacing));
        _view = new MedicalReformatView(display, buffer, "Oblique", step => Move(step * spacing), OnKey);
        SetPlane(display.Position, new Point3D(0, 0, 1));
    }

    /// <summary>Occurs when the plane moved or turned.</summary>
    public event Action? Changed
    {
        add => _view.Changed += value;
        remove => _view.Changed -= value;
    }

    /// <summary>Gets the middle of the plane, which is the middle of its picture.</summary>
    public Point3D Center { get; private set; }

    /// <summary>Gets the unit normal of the plane.</summary>
    public Point3D Normal { get; private set; }

    /// <summary>Gets the unit direction of the columns of the picture.</summary>
    public Point3D Across { get; private set; }

    /// <summary>Gets the unit direction of the rows of the picture.</summary>
    public Point3D Down { get; private set; }

    /// <summary>
    /// Gets or sets the camera of a 3D chart of the same display: the key V on the chart of the plane then turns the
    /// plane to face it, see <see cref="AlignToView"/>.
    /// </summary>
    public Camera3D? ViewCamera { get; set; }

    /// <summary>Gets the number of columns and rows of the square picture, which holds the volume in any direction.</summary>
    public int Size => _view.Buffer.Width;

    /// <summary>Gets the size of a pixel in millimetres.</summary>
    public double PixelSpacing => _view.Buffer.Spacing.Column;

    /// <summary>Puts the plane through a place with a normal; the rows run downwards in the patient where they can.</summary>
    /// <param name="center">The middle of the plane.</param>
    /// <param name="normal">The normal, of any length but zero.</param>
    /// <exception cref="ArgumentException">The normal has no length or a part is not finite.</exception>
    public void SetPlane(Point3D center, Point3D normal)
    {
        var unit = Unit(normal, nameof(normal));

        // Rows go to lower z, as on coronal and sagittal planes; a plane that lies nearly flat keeps rows along y.
        var reference = Math.Abs(unit.Z) > 0.9 ? new Point3D(0, 1, 0) : new Point3D(0, 0, -1);
        var down = Unit(reference - (unit * Point3D.Dot(reference, unit)), nameof(normal));
        SetFrame(center, Point3D.Cross(down, unit), down);
    }

    /// <summary>Moves the plane along its normal.</summary>
    /// <param name="millimetres">How far; negative goes back.</param>
    public void Move(double millimetres) => SetFrame(Center + (Normal * millimetres), Across, Down);

    /// <summary>
    /// Lets the plane follow the distance measurement last touched on an orthogonal plane, or the last one on its slice:
    /// it contains the line and stands upright on that plane, with the columns along the line. It follows while the
    /// line is drawn or moved.
    /// </summary>
    /// <param name="slice">The orthogonal plane, or <see langword="null"/> to stop following.</param>
    public void FollowLine(MedicalMprSlice? slice)
    {
        if (_followed is { } previous)
        {
            previous.Measurements.Changed -= OnLineChanged;
        }

        _followed = slice;
        if (slice is not null)
        {
            slice.Measurements.Changed += OnLineChanged;
            OnLineChanged(null);
        }
    }

    /// <summary>Turns the plane to face the eye of a 3D chart of the same display, keeping its middle.</summary>
    /// <param name="camera">The camera of the 3D chart.</param>
    public void AlignToView(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        // The box of the chart spans -1 to 1 on each axis; the volume its full size in millimetres.
        var extent = MedicalReformatView.Extent(_view.Display.Source);
        var look = camera.Target - camera.Eye;
        SetPlane(Center, new Point3D(look.X * extent.X, look.Y * extent.Y, look.Z * extent.Z));
    }

    /// <summary>Adds the picture of the plane to a 2D chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure(ChartBuilder chart) => _view.Configure(chart);

    /// <summary>Adds the outline of the plane to a 3D chart description, for example the one of <see cref="MedicalMprDisplay.Configure3D"/>.</summary>
    /// <param name="chart">The 3D chart description.</param>
    public void Configure3D(Chart3DBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var points = new Points3D(5);
        _outlines.Add(points);
        WriteOutline(points);
        chart.AddLine(new Line3D(points));
    }

    /// <summary>Reads the picture at a pixel; NaN outside the volume.</summary>
    internal float Read(int column, int row) => _view.Buffer.Read(column, row);

    private bool OnKey(Core.Input.ChartKey key)
    {
        if (key != Core.Input.ChartKey.V || ViewCamera is not { } camera)
        {
            return false;
        }

        AlignToView(camera);
        return true;
    }

    private static Point3D Unit(Point3D vector, string name)
    {
        var length = vector.Length;
        if (!double.IsFinite(length) || length < 1e-12)
        {
            throw new ArgumentException("The direction must be finite and not zero.", name);
        }

        return vector * (1 / length);
    }

    private void SetFrame(Point3D center, Point3D across, Point3D down)
    {
        (Center, Across, Down, Normal) = (center, across, down, Point3D.Cross(across, down));
        var corner = center - (across * (_side / 2)) - (down * (_side / 2));
        _view.Buffer.FillPlane(corner, across, down);
        foreach (var outline in _outlines)
        {
            WriteOutline(outline);
        }

        _view.RaiseChanged();
    }

    // The four corners and the first again; the ring of points keeps the last five.
    private void WriteOutline(Points3D points)
    {
        var (half, a, d) = (_side / 2, Across, Down);
        foreach (var (x, y) in (ReadOnlySpan<(double, double)>)[(-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1)])
        {
            var corner = Center + (a * (x * half)) + (d * (y * half));
            points.Append(corner.X, corner.Y, corner.Z, _outlineColor);
        }
    }

    private void OnLineChanged(MedicalMeasurement? changed)
    {
        if (_followed is not { } slice || Pick(slice) is not { } line)
        {
            return;
        }

        var buffer = slice.Buffer;
        var current = _view.Display.Position;
        var (start, end) = (line.Snapshot[0], line.Snapshot[1]);
        var from = buffer.ToVolumePosition(start.X, start.Y, current);
        var to = buffer.ToVolumePosition(end.X, end.Y, current);
        if ((to - from).Length < 1e-9)
        {
            return;
        }

        // The rows run along the normal of the orthogonal plane, to lower z or along y.
        var normal = slice.Plane switch
        {
            MedicalMprPlane.Axial => new Point3D(0, 0, -1),
            MedicalMprPlane.Coronal => new Point3D(0, 1, 0),
            _ => new Point3D(1, 0, 0),
        };
        SetFrame((from + to) * 0.5, Unit(to - from, nameof(slice)), normal);
    }

    private static MedicalMeasurement? Pick(MedicalMprSlice slice)
    {
        var layer = slice.Measurements;
        if (layer.Selected is { Kind: MedicalMeasurementKind.Distance } selected)
        {
            return selected;
        }

        for (var i = layer.Measurements.Count - 1; i >= 0; i--)
        {
            if (layer.Measurements[i].Kind == MedicalMeasurementKind.Distance)
            {
                return layer.Measurements[i];
            }
        }

        return null;
    }
}
