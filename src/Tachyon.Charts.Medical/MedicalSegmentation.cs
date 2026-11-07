using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>How a medical segmentation is shown in a 3D chart.</summary>
public enum MedicalSegmentation3DMode
{
    /// <summary>Shows all visible labels as a translucent categorical volume.</summary>
    Volume,

    /// <summary>Shows the boundary of every visible label as a lit surface.</summary>
    Surface,
}

/// <summary>The appearance and visibility of one positive integer in a medical label map.</summary>
public sealed class MedicalSegment
{
    private readonly object _gate = new();
    private string _name;
    private Rgba8 _color;
    private double _opacity = 0.55;
    private bool _isVisible = true;

    /// <summary>Initializes the description of one label.</summary>
    /// <param name="label">The positive label value.</param>
    /// <param name="name">The display name.</param>
    /// <param name="color">The display color.</param>
    public MedicalSegment(int label, string name, Rgba8 color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(label, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(label, ushort.MaxValue);
        Label = label;
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _color = color;
    }

    internal event Action? Changed;

    /// <summary>Gets the positive value represented by this segment.</summary>
    public int Label { get; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name
    {
        get { lock (_gate) { return _name; } }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                if (_name == value) return;
                _name = value;
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Gets or sets the display color.</summary>
    public Rgba8 Color
    {
        get { lock (_gate) { return _color; } }
        set
        {
            lock (_gate)
            {
                if (_color == value) return;
                _color = value;
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Gets or sets the opacity from zero to one.</summary>
    public double Opacity
    {
        get { lock (_gate) { return _opacity; } }
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Opacity must be finite and between zero and one.");
            }

            lock (_gate)
            {
                if (_opacity == value) return;
                _opacity = value;
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Gets or sets a value indicating whether the segment is shown.</summary>
    public bool IsVisible
    {
        get { lock (_gate) { return _isVisible; } }
        set
        {
            lock (_gate)
            {
                if (_isVisible == value) return;
                _isVisible = value;
            }

            Changed?.Invoke();
        }
    }
}

/// <summary>A categorical three-dimensional label map with patient-space geometry.</summary>
public sealed class MedicalSegmentation
{
    private static readonly Rgba8[] _palette =
    [
        new(230, 75, 90, 255), new(55, 180, 110, 255), new(60, 130, 235, 255), new(245, 175, 55, 255),
        new(170, 95, 225, 255), new(45, 195, 205, 255), new(240, 105, 180, 255), new(155, 190, 55, 255),
    ];

    private long _version;

    /// <summary>Initializes a label map from application-owned volume data and geometry.</summary>
    /// <param name="data">Integer labels from zero through 65,535. Zero is transparent background.</param>
    /// <param name="inPlane">The row and column spacing in millimetres.</param>
    /// <param name="sliceSpacing">The spacing between slice centres in millimetres.</param>
    /// <param name="origin">The patient-space position of the first voxel.</param>
    /// <param name="orientation">The patient-space orientation.</param>
    /// <param name="segments">Optional descriptions. Missing labels receive deterministic defaults.</param>
    public MedicalSegmentation(
        VolumeData data,
        ImageSpacing inPlane,
        double sliceSpacing,
        Point3D origin,
        PatientOrientation orientation,
        IEnumerable<MedicalSegment>? segments = null)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Geometry.Validate(inPlane, sliceSpacing, origin, orientation);
        VoxelSpacing = new Point3D(inPlane.Column, inPlane.Row, sliceSpacing);
        Origin = origin;
        Orientation = orientation;

        var present = FindLabels(data);
        var descriptions = new Dictionary<int, MedicalSegment>();
        if (segments is not null)
        {
            foreach (var segment in segments)
            {
                ArgumentNullException.ThrowIfNull(segment);
                if (!descriptions.TryAdd(segment.Label, segment))
                {
                    throw new ArgumentException($"Label {segment.Label} is described more than once.", nameof(segments));
                }
            }
        }

        foreach (var label in present)
        {
            descriptions.TryAdd(label, new MedicalSegment(label, $"Label {label}", _palette[(label - 1) % _palette.Length]));
        }

        Segments = descriptions.Values.OrderBy(segment => segment.Label).ToArray();
        foreach (var segment in Segments)
        {
            segment.Changed += OnSegmentChanged;
        }
    }

    internal event Action? Changed;

    /// <summary>Gets the integer label values.</summary>
    public VolumeData Data { get; }

    /// <summary>Gets the voxel spacing along columns, rows and slices.</summary>
    public Point3D VoxelSpacing { get; }

    /// <summary>Gets the patient-space position of the first voxel.</summary>
    public Point3D Origin { get; }

    /// <summary>Gets the patient-space orientation.</summary>
    public PatientOrientation Orientation { get; }

    /// <summary>Gets the positive labels and their appearance.</summary>
    public IReadOnlyList<MedicalSegment> Segments { get; }

    /// <summary>Gets or sets how the segmentation is shown in 3D.</summary>
    public MedicalSegmentation3DMode ThreeDMode { get; init; }

    internal long Version => Interlocked.Read(ref _version);

    private void OnSegmentChanged()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }

    private static SortedSet<int> FindLabels(VolumeData data)
    {
        var labels = new SortedSet<int>();
        for (var z = 0; z < data.Depth; z++)
        {
            for (var y = 0; y < data.Height; y++)
            {
                for (var x = 0; x < data.Width; x++)
                {
                    var value = data[x, y, z];
                    if (!double.IsFinite(value) || value < 0 || value > ushort.MaxValue || value != Math.Truncate(value))
                    {
                        throw new ArgumentException("A label map may contain only integer values from zero through 65,535.", nameof(data));
                    }

                    if (value > 0) labels.Add((int)value);
                }
            }
        }

        return labels;
    }
}

internal static class Geometry
{
    public static void Validate(ImageSpacing inPlane, double sliceSpacing, Point3D origin, PatientOrientation orientation)
    {
        if (!double.IsFinite(inPlane.Row) || inPlane.Row <= 0 || !double.IsFinite(inPlane.Column) || inPlane.Column <= 0)
            throw new ArgumentOutOfRangeException(nameof(inPlane), inPlane, "Pixel spacing must be finite and positive.");
        if (!double.IsFinite(sliceSpacing) || sliceSpacing <= 0)
            throw new ArgumentOutOfRangeException(nameof(sliceSpacing), sliceSpacing, "Slice spacing must be finite and positive.");
        if (!Finite(origin)) throw new ArgumentOutOfRangeException(nameof(origin), origin, "The origin must be finite.");
        if (!Finite(orientation.Across) || !Finite(orientation.Down) || orientation.Across.Length == 0 || orientation.Down.Length == 0 || orientation.Normal.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation needs two finite, independent directions.");
    }

    public static Point3D ToPatient(Point3D local, Point3D origin, PatientOrientation orientation) =>
        origin + (orientation.Across.Normalized() * local.X) + (orientation.Down.Normalized() * local.Y) + (orientation.Normal * local.Z);

    public static Point3D FromPatient(Point3D patient, Point3D origin, PatientOrientation orientation)
    {
        var delta = patient - origin;
        return new Point3D(
            Point3D.Dot(delta, orientation.Across.Normalized()),
            Point3D.Dot(delta, orientation.Down.Normalized()),
            Point3D.Dot(delta, orientation.Normal));
    }

    private static bool Finite(Point3D point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
}
